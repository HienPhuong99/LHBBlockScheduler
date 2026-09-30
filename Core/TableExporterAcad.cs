using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Xuất bảng thống kê bằng AutoCAD Table object (lệnh TABLE), cột Ký hiệu chứa block trong ô
    /// (Middle Center, tỉ lệ tự tính) giống bảng mẫu người dùng.
    ///
    /// Quy tắc chọn block cho ô Ký hiệu, xử lý TUẦN TỰ từng thiết bị:
    ///  - Có ảnh tuỳ chỉnh (CustomImagePath)  -> BTR "LHB_IMG_xxx" chứa 1 RasterImage.
    ///  - Có VisibilityState (dynamic block)   -> LHB_SYM_xxx chứa bản chép các entity đang hiện trong BTR ẩn danh
    ///    của 1 instance (đúng chủng loại), theo toạ độ định nghĩa block = đúng hình trên cột Ký hiệu của form.
    ///  - Không có VisibilityState              -> LHB_SYM_xxx chứa 1 BlockReference tới definition.
    /// Mọi block ký hiệu được chuẩn hoá (tâm hình tại base point, cạnh dài nhất = 1, khung bao vuông 1 x 1 bằng
    /// 2 điểm trên layer tắt LHB_KY_HIEU_KHUNG -> mọi ký hiệu đều cỡ, v9.2) rồi đặt vào ô ở chế độ
    /// AutoFit để ký hiệu co giãn theo ô khi user kéo đổi kích thước dòng/cột (yêu cầu 28/09/2026).
    /// AutoFit đo bằng GeometricExtents của AutoCAD -> block ném eInvalidExtents (ĐÈN EXIT) được phẳng hoá
    /// thành entity rời trước, nếu không AutoFit làm ký hiệu to nhỏ, lệch ô (test 28/09/2026).
    /// Phẳng hoá / chuẩn hoá luôn nhân tỉ lệ vào linetype scale để nét đứt giữ nguyên như trong block (test 29/09/2026).
    /// </summary>
    public static class TableExporterAcad
    {

        /// <param name="insertionPointWcs">Điểm chèn (góc trên-trái bảng) theo WCS.</param>
        public static void ExportTable(Document doc, List<BlockItem> items, Point3d insertionPointWcs, TableExportConfig config = null)
        {
            config ??= new TableExportConfig();
            var db = doc.Database;
            var sortedItems = items.OrderBy(i => i.Order).ToList();

            // 1. Danh sách cột xuất (STT + các cột đang hiển thị trên grid)
            var exportCols = TableExporter.BuildExportColumns(config);
            int nCols = exportCols.Count;
            int symbolCol = exportCols.FindIndex(c => c.Key == "colThumbnail");

            // Mẫu bảng (Premium P10): tiêu đề / dòng phụ / dòng tổng / font / cỡ chữ / màu
            var tpl = config.Template ?? TableTemplate.Current;
            int titleRows = tpl.HideTitle ? 0 : 1;
            int subtitleRow = string.IsNullOrWhiteSpace(tpl.Subtitle) ? -1 : titleRows;
            int headerRow = titleRows + (subtitleRow >= 0 ? 1 : 0);
            int firstDataRow = headerRow + 1;
            int totalRow = tpl.AddTotalRow ? firstDataRow + sortedItems.Count : -1;
            int nRows = firstDataRow + sortedItems.Count + (totalRow >= 0 ? 1 : 0);

            double textHeight = config.ActualTextHeight * tpl.EffectiveTextFactor;
            double rowHeight = config.ActualRowHeight * tpl.EffectiveRowFactor;
            // Độ rộng cột theo cột trên grid của form (kể cả ô Ký hiệu user kéo rộng)
            double[] colWidths = TableExporter.CalculateColumnWidths(exportCols, sortedItems, textHeight, rowHeight, config.GridRowHeightPx);

            Logger.Log($"[TableExporterAcad] Bắt đầu: {sortedItems.Count} dòng, {nCols} cột [{string.Join(", ", exportCols.Select(c => c.Key))}], " +
                       $"scale={config.TableScale}, textH={textHeight}, rowH={rowHeight}, điểm chèn WCS={insertionPointWcs}");

            int okSymbols = 0, failSymbols = 0;
            ObjectId tableId = ObjectId.Null;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            TableExporter.BeginImageExport();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // v9.4: chèn vào không gian đang làm việc (Model hoặc Layout) - trước đây luôn vào Model Space dù đang ở Layout
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                bool inModel = db.CurrentSpaceId == SymbolUtilityServices.GetBlockModelSpaceId(db);
                ObjectId textStyleId = TableExporter.GetOrCreateTextStyle(db, tr, tpl.TextStyleName, tpl.EffectiveFont);

                // 2. Tạo Table rỗng, đưa vào không gian hiện hành trước rồi mới điền nội dung
                var tb = new Table();
                tb.SetDatabaseDefaults(db);
                tb.TableStyle = db.Tablestyle;
                tb.SetSize(nRows, nCols);
                tb.Position = insertionPointWcs;
                space.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tableId = tb.ObjectId;
                Logger.Log($"[TableExporterAcad] Đã tạo Table {nRows}x{nCols}, Handle={tb.Handle}, TableStyle={db.Tablestyle}, " +
                           $"không gian='{TableExporter.SpaceName(tr, space)}'{(inModel ? "" : " (Layout)")}");
                SuppressRegen(tb, true);
                var symbolCache = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

                for (int c = 0; c < nCols; c++) tb.Columns[c].Width = colWidths[c];
                for (int r = 0; r < nRows; r++) tb.Rows[r].Height = rowHeight;

                // 3. Hàng tiêu đề + dòng phụ: gộp hết các cột
                if (titleRows > 0)
                {
                    MergeRow(tb, 0, nCols);
                    SetCellText(tb, 0, 0, tpl.EffectiveTitle, textHeight * 1.2, textStyleId);
                    SetCellFill(tb, 0, 0, tpl.TitleColorIndex);
                }
                if (subtitleRow >= 0)
                {
                    MergeRow(tb, subtitleRow, nCols);
                    SetCellText(tb, subtitleRow, 0, tpl.Subtitle.Trim(), textHeight, textStyleId);
                }

                // 4. Hàng header
                for (int c = 0; c < nCols; c++)
                {
                    string h = exportCols[c].HeaderText ?? "";
                    SetCellText(tb, headerRow, c, tpl.UppercaseHeader ? h.ToUpper() : h, textHeight, textStyleId);
                    SetCellFill(tb, headerRow, c, tpl.HeaderColorIndex);
                }

                // 5. Dữ liệu: từng thiết bị một, tuần tự
                for (int i = 0; i < sortedItems.Count; i++)
                {
                    var item = sortedItems[i];
                    int r = i + firstDataRow;
                    string label = $"Dòng {i + 1}/{sortedItems.Count} '{item.DisplayName ?? item.BlockName}' (vis='{item.VisibilityState}')";

                    for (int c = 0; c < nCols; c++)
                    {
                        string key = exportCols[c].Key;
                        if (key == "colThumbnail") continue;
                        string text = key == "STT" ? (i + 1).ToString() : TableExporter.GetItemTextForColumn(item, key);
                        SetCellText(tb, r, c, text, textHeight, textStyleId, ToCellAlignment(item.GetAlignment(key)));
                    }
                    if (item.CellAlignments != null && item.CellAlignments.Count > 0)
                        Logger.Log($"[TableExporterAcad] {label}: căn lề riêng [{string.Join(", ", item.CellAlignments.Select(kv => kv.Key + "=" + kv.Value))}]");

                    if (symbolCol < 0) continue;

                    try
                    {
                        var sym = ResolveSymbol(db, tr, item, symbolCache);
                        if (sym.BtrId.IsNull)
                        {
                            failSymbols++;
                            Logger.Warn($"[TableExporterAcad] {label}: KHÔNG có block cho ô ký hiệu (mode={sym.Mode}), để trống ô.");
                            continue;
                        }

                        var symAlign = ToCellAlignment(item.GetAlignment("colThumbnail"));
                        double side = SetCellBlock(tb, r, symbolCol, sym, symAlign, colWidths[symbolCol], rowHeight);
                        okSymbols++;
                        Logger.Log($"[TableExporterAcad] {label}: ô ký hiệu OK, mode={sym.Mode}, btr={GetBtrName(tr, sym.BtrId)}, AutoFit trong ô vuông " +
                                   $"cạnh {side:0.#} (ô {colWidths[symbolCol]:0.#}x{rowHeight:0.#}), căn={symAlign}, " +
                                   (sym.IsNormalized ? $"kích thước chuẩn hoá {sym.Width:0.###}x{sym.Height:0.###}" : "KHÔNG chuẩn hoá được (AutoFit theo extents gốc)"));
                    }
                    catch (Exception ex)
                    {
                        failSymbols++;
                        Logger.Error(ex, $"[TableExporterAcad] {label}: lỗi khi đặt block vào ô ký hiệu");
                    }
                }

                // Dòng TỔNG CỘNG (mẫu bảng): cộng cột SL + SL theo khu vực
                if (totalRow >= 0) WriteTotalRow(tb, totalRow, firstDataRow, sortedItems.Count, exportCols.Select(c => c.Key).ToList(),
                                                 tpl, textHeight, textStyleId);

                // Bảng tự cập nhật (Premium P2): lưu vùng quét + khoá từng dòng vào bảng
                if (config.ScanInfo != null)
                {
                    try
                    {
                        var info = config.ScanInfo;
                        info.ColumnKeys = exportCols.Select(c => c.Key).ToList();
                        info.FirstDataRow = firstDataRow;
                        info.HasTotalRow = totalRow >= 0;
                        info.TextHeight = textHeight;
                        info.RowHeight = rowHeight;
                        info.TotalLabel = tpl.EffectiveTotalLabel;
                        info.Rows = sortedItems.Select((it, i) => new TableRowKeys
                        {
                            Keys = (it.Instances ?? new List<BlockInstanceRef>()).Select(x => x.GroupKey).Where(k => !string.IsNullOrEmpty(k))
                                                                             .Distinct().ToList(),
                            Label = TableUpdater.RowLabel(tb, firstDataRow + i, info.ColumnKeys)
                        }).ToList();
                        // v9.4: cấu trúc bảng lúc ghi -> LHBCAPNHAT dừng nếu user thêm / xoá dòng, cột bằng tay
                        info.TableRowCount = nRows;
                        info.TableColumnCount = nCols;
                        info.UpdatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                        DrawingHelper.WriteExtString(tr, tb, TableUpdater.ExtKey, JsonHelper.Serialize(info));
                        Logger.Log($"[TableExporterAcad] Lưu thông tin tự cập nhật: {info.Rows.Count} dòng, vùng quét " +
                                   $"({info.MinX:0},{info.MinY:0})-({info.MaxX:0},{info.MaxY:0}), {info.RootHandles?.Count ?? 0} đối tượng gốc");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "[TableExporterAcad] lưu thông tin tự cập nhật");
                    }
                }

                SuppressRegen(tb, false);
                tb.GenerateLayout();
                tb.RecomputeTableBlock(true);
                Logger.Log($"[TableExporterAcad] Điền bảng xong sau {sw.ElapsedMilliseconds} ms ({symbolCache.Count} block ký hiệu)");

                // Đường dẫn từ mép phải từng dòng thiết bị tới chỗ block trùng (lỗi ở đây không được làm hỏng bảng).
                // Block trùng nằm ở Model -> bảng trên Layout không vẽ đường dẫn (toạ độ giấy khác toạ độ Model).
                if (!inModel)
                {
                    int dupRows = sortedItems.Count(i => i.DuplicateGroups != null && i.DuplicateGroups.Count > 0);
                    if (dupRows > 0) Logger.Log($"[TableExporterAcad] Bảng trên Layout -> không vẽ đường dẫn tới {dupRows} dòng có block trùng (dùng nút 'Tìm trùng' để khoanh ở Model)");
                }
                else try
                {
                    double tableWidth = 0;
                    for (int c = 0; c < nCols; c++) tableWidth += tb.Columns[c].Width;
                    var rowMidY = new double[nRows];
                    double y = tb.Position.Y;
                    for (int r = 0; r < nRows; r++)
                    {
                        double h = tb.Rows[r].Height;
                        rowMidY[r] = y - h / 2.0;
                        y -= h;
                    }
                    var anchors = sortedItems
                        .Select((item, i) => (item, new Point3d(tb.Position.X + tableWidth, rowMidY[i + firstDataRow], tb.Position.Z)))
                        .ToList();
                    DuplicateFinder.DrawTableLeaders(tr, db, space, anchors, rowHeight * 0.5, textHeight);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[TableExporterAcad] lỗi vẽ đường dẫn tới block trùng");
                }

                tr.Commit();
            }

            Logger.Log($"[TableExporterAcad] Hoàn tất: ô ký hiệu OK={okSymbols}, lỗi/trống={failSymbols}");
            TableExporter.EndImageExport(doc);

            // 6. Zoom tới bảng
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var tb = (Table)tr.GetObject(tableId, OpenMode.ForRead);
                    ScheduleManager.ZoomToExtents(doc, tb.GeometricExtents);
                    tr.Commit();
                }
                doc.Editor.WriteMessage($"\nĐã xuất AutoCAD Table gồm {sortedItems.Count} dòng (ô ký hiệu: {okSymbols} OK, {failSymbols} trống).\n");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TableExporterAcad] lỗi zoom tới bảng");
            }
        }

        /// <summary>
        /// Bảng AutoCAD Table đơn giản từ lưới chữ (Premium: nhiều bản vẽ, chiều dài ống / dây). Có mẫu bảng (tiêu đề,
        /// dòng phụ, font, màu header). symbols (tuỳ chọn) = thiết bị của từng dòng để đặt ký hiệu vào cột symbolCol.
        /// sumCols = các cột cộng ở dòng tổng (null = không có dòng tổng). Trả ObjectId bảng.
        /// </summary>
        internal static ObjectId ExportGrid(Document doc, string title, List<string> headers, List<string[]> rows, Point3d pt,
                                            double textHeight, double rowHeight, List<BlockItem> symbols = null, int symbolCol = -1,
                                            HashSet<int> sumCols = null, int[] decimals = null)
        {
            var tpl = TableTemplate.Current;
            textHeight *= tpl.EffectiveTextFactor;
            rowHeight *= tpl.EffectiveRowFactor;
            var db = doc.Database;
            int nCols = headers.Count;
            int titleRows = tpl.HideTitle ? 0 : 1;
            int subtitleRow = string.IsNullOrWhiteSpace(tpl.Subtitle) ? -1 : titleRows;
            int headerRow = titleRows + (subtitleRow >= 0 ? 1 : 0);
            int first = headerRow + 1;
            bool total = sumCols != null && sumCols.Count > 0;
            int nRows = first + rows.Count + (total ? 1 : 0);

            // Độ rộng cột theo chữ dài nhất (giống bảng thống kê)
            var widths = new double[nCols];
            for (int c = 0; c < nCols; c++)
            {
                if (c == symbolCol) { widths[c] = rowHeight * 1.5; continue; }
                double w = TableExporter.EstimateTextWidth(headers[c], textHeight);
                foreach (var r in rows) w = Math.Max(w, TableExporter.EstimateTextWidth(c < r.Length ? r[c] : "", textHeight));
                widths[c] = Math.Max(4.0 * textHeight, Math.Min(w + 3.0 * textHeight, 60.0 * textHeight));
            }

            ObjectId tableId;
            TableExporter.BeginImageExport();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // v9.4: không gian đang làm việc (Model hoặc Layout)
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var styleId = TableExporter.GetOrCreateTextStyle(db, tr, tpl.TextStyleName, tpl.EffectiveFont);
                var tb = new Table();
                tb.SetDatabaseDefaults(db);
                tb.TableStyle = db.Tablestyle;
                tb.SetSize(nRows, nCols);
                tb.Position = pt;
                space.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tableId = tb.ObjectId;
                SuppressRegen(tb, true);
                for (int c = 0; c < nCols; c++) tb.Columns[c].Width = widths[c];
                for (int r = 0; r < nRows; r++) tb.Rows[r].Height = rowHeight;

                if (titleRows > 0)
                {
                    MergeRow(tb, 0, nCols);
                    SetCellText(tb, 0, 0, title, textHeight * 1.2, styleId);
                    SetCellFill(tb, 0, 0, tpl.TitleColorIndex);
                }
                if (subtitleRow >= 0)
                {
                    MergeRow(tb, subtitleRow, nCols);
                    SetCellText(tb, subtitleRow, 0, tpl.Subtitle.Trim(), textHeight, styleId);
                }
                for (int c = 0; c < nCols; c++)
                {
                    SetCellText(tb, headerRow, c, tpl.UppercaseHeader ? headers[c].ToUpper() : headers[c], textHeight, styleId);
                    SetCellFill(tb, headerRow, c, tpl.HeaderColorIndex);
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    for (int c = 0; c < nCols; c++)
                        if (c != symbolCol) SetCellText(tb, first + i, c, c < rows[i].Length ? rows[i][c] : "", textHeight, styleId);
                    if (symbols != null && symbolCol >= 0 && i < symbols.Count && symbols[i] != null)
                        PutSymbol(db, tr, tb, first + i, symbolCol, symbols[i], widths[symbolCol], rowHeight);
                }
                if (total)
                {
                    int tr0 = first + rows.Count;
                    int firstSum = sumCols.Min();
                    if (firstSum > 1) { try { tb.MergeCells(CellRange.Create(tb, tr0, 0, tr0, firstSum - 1)); } catch { } }
                    SetCellText(tb, tr0, 0, tpl.EffectiveTotalLabel, textHeight, styleId);
                    SetCellFill(tb, tr0, 0, tpl.HeaderColorIndex);
                    foreach (int c in sumCols)
                    {
                        double sum = 0;
                        foreach (var r in rows)
                            if (c < r.Length && double.TryParse(r[c], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v)) sum += v;
                        int dec = decimals != null && c < decimals.Length ? decimals[c] : 0;
                        SetCellText(tb, tr0, c, sum.ToString("F" + dec, System.Globalization.CultureInfo.InvariantCulture), textHeight, styleId);
                        SetCellFill(tb, tr0, c, tpl.HeaderColorIndex);
                    }
                }
                SuppressRegen(tb, false);
                tb.GenerateLayout();
                tr.Commit();
            }
            Logger.Log($"[TableExporterAcad.ExportGrid] '{title}': {rows.Count} dòng x {nCols} cột tại {pt}");
            TableExporter.EndImageExport(doc);
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var tb = (Table)tr.GetObject(tableId, OpenMode.ForRead);
                    ScheduleManager.ZoomToExtents(doc, tb.GeometricExtents);
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad.ExportGrid] zoom: {ex.Message}");
            }
            return tableId;
        }

        /// <summary>
        /// Tắt / bật tính lại hình bảng. Bảng đã nằm trong bản vẽ: mỗi lần sửa 1 ô AutoCAD dựng lại cả bảng -> bảng
        /// 100 dòng x 8 cột dựng lại cả nghìn lần, xuất rất chậm. Tắt trong lúc điền ô, bật lại trước GenerateLayout.
        /// Lỗi giữa chừng thì transaction bị huỷ nên không cần try/finally.
        /// </summary>
        internal static void SuppressRegen(Table tb, bool suppress)
        {
            try
            {
                tb.SuppressRegenerateTable(suppress);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad] SuppressRegenerateTable({suppress}) lỗi: {ex.Message} -> điền bảng kiểu cũ (chậm hơn)");
            }
        }

        private static void MergeRow(Table tb, int r, int nCols)
        {
            try
            {
                if (nCols > 1 && tb.Cells[r, 0].IsMerged != true)
                    tb.MergeCells(CellRange.Create(tb, r, 0, r, nCols - 1));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad] Không gộp được hàng {r}: {ex.Message}");
            }
        }

        /// <summary>Tô nền ô theo mã màu AutoCAD (0 = không tô).</summary>
        internal static void SetCellFill(Table tb, int r, int c, short colorIndex)
        {
            if (colorIndex <= 0 || colorIndex > 255) return;
            try
            {
                tb.Cells[r, c].BackgroundColor = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad] Không tô nền ô [{r},{c}]: {ex.Message}");
            }
        }

        /// <summary>Cột cộng được ở dòng tổng: SL, SL theo khu vực.</summary>
        internal static bool IsSummableColumn(string key) =>
            key == "colCount" || (key != null && key.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Dòng TỔNG CỘNG: nhãn gộp từ cột đầu tới trước cột số đầu tiên, các cột SL / khu vực = tổng các dòng dữ liệu
        /// (đọc lại chữ trong ô nên đúng cả sau khi user sửa tay hoặc sau khi cập nhật bảng).
        /// </summary>
        internal static void WriteTotalRow(Table tb, int totalRow, int firstDataRow, int dataRows, List<string> colKeys,
                                           TableTemplate tpl, double textHeight, ObjectId textStyleId)
        {
            int firstNum = colKeys.FindIndex(IsSummableColumn);
            if (firstNum < 0) firstNum = colKeys.Count;
            try
            {
                if (firstNum > 1 && tb.Cells[totalRow, 0].IsMerged != true)
                    tb.MergeCells(CellRange.Create(tb, totalRow, 0, totalRow, firstNum - 1));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad] Không gộp ô nhãn dòng tổng: {ex.Message}");
            }
            SetCellText(tb, totalRow, 0, tpl?.EffectiveTotalLabel ?? "TỔNG CỘNG", textHeight, textStyleId);
            SetCellFill(tb, totalRow, 0, tpl?.HeaderColorIndex ?? 0);
            for (int c = firstNum; c < colKeys.Count; c++)
            {
                if (!IsSummableColumn(colKeys[c])) { SetCellText(tb, totalRow, c, "", textHeight, textStyleId); continue; }
                long sum = 0;
                for (int r = firstDataRow; r < firstDataRow + dataRows; r++)
                {
                    string s = tb.Cells[r, c].TextString;
                    if (long.TryParse((s ?? "").Trim(), out long v)) sum += v;
                }
                SetCellText(tb, totalRow, c, sum.ToString(), textHeight, textStyleId);
                SetCellFill(tb, totalRow, c, tpl?.HeaderColorIndex ?? 0);
            }
        }

        /// <summary>Đặt ký hiệu của 1 thiết bị vào ô (dùng khi cập nhật bảng thêm dòng mới). Trả false nếu không có block.</summary>
        internal static bool PutSymbol(Database db, Transaction tr, Table tb, int r, int c, BlockItem item, double cellWidth, double rowHeight)
        {
            try
            {
                var sym = ResolveSymbol(db, tr, item, null);
                if (sym.BtrId.IsNull) return false;
                SetCellBlock(tb, r, c, sym, CellAlignment.MiddleCenter, cellWidth, rowHeight);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TableExporterAcad.PutSymbol] '{item.BlockName}'");
                return false;
            }
        }

        internal static void SetCellTextPublic(Table tb, int r, int c, string text, double textHeight, ObjectId textStyleId) =>
            SetCellText(tb, r, c, text, textHeight, textStyleId);

        private static void SetCellText(Table tb, int r, int c, string text, double textHeight, ObjectId textStyleId,
                                        CellAlignment align = CellAlignment.MiddleCenter)
        {
            try
            {
                var cell = tb.Cells[r, c];
                // Font Arial cho MỌI ô (kể cả title/header), nếu không chữ có dấu thành ô vuông
                cell.TextStyleId = textStyleId;
                cell.TextHeight = textHeight;
                cell.Alignment = align;
                // Lề trái/phải = nửa chiều cao chữ: căn trái/phải không dính sát đường kẻ (lề mặc định của style chỉ ~1.5)
                SetCellMargins(cell, textHeight * 0.5, null);
                cell.TextString = text ?? "";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TableExporterAcad.SetCellText] ô [{r},{c}] text='{text}'");
            }
        }

        private static CellAlignment ToCellAlignment(CellHAlign align) =>
            align == CellHAlign.Left ? CellAlignment.MiddleLeft
            : align == CellHAlign.Right ? CellAlignment.MiddleRight
            : CellAlignment.MiddleCenter;

        private static bool _loggedMarginError;

        private static void SetCellMargins(Cell cell, double? horizontal, double? vertical)
        {
            try
            {
                if (horizontal.HasValue)
                {
                    cell.Borders.Left.Margin = horizontal;
                    cell.Borders.Right.Margin = horizontal;
                }
                if (vertical.HasValue)
                {
                    cell.Borders.Top.Margin = vertical;
                    cell.Borders.Bottom.Margin = vertical;
                }
            }
            catch (Exception ex)
            {
                // Chỉ log 1 lần / phiên: lỗi này (nếu có) lặp ở mọi ô
                if (!_loggedMarginError)
                {
                    _loggedMarginError = true;
                    Logger.Warn($"[TableExporterAcad.SetCellMargins] Không đặt được lề ô (ngang={horizontal}, dọc={vertical}): {ex.Message}");
                }
            }
        }

        // Lề ô ký hiệu mỗi phía = 7.5% cạnh ngắn của ô -> ký hiệu chiếm ~85% ô, giống ảnh trên form (ZoomExtents 0.85)
        private const double SymbolPaddingRatio = 0.075;

        private class SymbolInfo
        {
            public ObjectId BtrId = ObjectId.Null;
            public string Mode;
            /// <summary>Kích thước hình sau chuẩn hoá (cạnh dài nhất = 1, tâm hình tại gốc toạ độ).</summary>
            public double Width, Height;
            public bool IsNormalized => Width > 0 && Height > 0;
        }

        /// <summary>
        /// Đặt block vào ô ở chế độ AutoFit: ký hiệu tự co giãn theo ô khi user kéo đổi kích thước dòng/cột.
        /// AutoFit co KHUNG BAO của block vào ô. Block ký hiệu có khung bao vuông 1 x 1 (AddSquareFrame) -> khung vuông
        /// co theo cạnh ngắn của ô, mọi ký hiệu có cạnh dài bằng nhau dù cột Ký hiệu rộng hay hẹp.
        /// v7-v9 dùng lề ô để tạo phần trong lề hình vuông, nhưng AutoFit không theo lề đó: ký hiệu dẹt (EXIT chỉ lối,
        /// tỉ lệ ~2.4:1) phóng theo bề ngang ô rộng, to hơn hẳn các dòng khác (ảnh test v8 29/09/2026).
        /// Căn trái/phải: khung vuông dồn sang trái/phải theo căn lề ô. Trả về cạnh khung vuông dự kiến.
        /// </summary>
        private static double SetCellBlock(Table tb, int r, int c, SymbolInfo sym, CellAlignment align, double cellWidth, double rowHeight)
        {
            var cell = tb.Cells[r, c];
            cell.Alignment = align;
            double pad = Math.Min(cellWidth, rowHeight) * SymbolPaddingRatio;
            SetCellMargins(cell, pad, pad);

            try
            {
                cell.BlockTableRecordId = sym.BtrId;
                var content = cell.Contents[0];
                content.IsAutoScale = true;
                content.Rotation = 0;
            }
            catch (Exception ex)
            {
                // Fallback API cũ nếu API mới lỗi trên bản CAD này
                Logger.Warn($"[TableExporterAcad.SetCellBlock] API Cell.Contents lỗi ở ô [{r},{c}]: {ex.Message} -> thử SetBlockTableRecordId");
#pragma warning disable 618
                tb.SetBlockTableRecordId(r, c, sym.BtrId, true);
#pragma warning restore 618
            }
            return Math.Max(Math.Min(cellWidth, rowHeight) - 2 * pad, 0);
        }

        /// <summary>Layer tắt, không in: chứa 2 điểm định khung vuông của block ký hiệu (không thấy trên màn hình, không in).</summary>
        public const string FrameLayerName = "LHB_KY_HIEU_KHUNG";

        /// <summary>
        /// Thêm khung bao vuông 1 x 1 (tâm tại gốc) cho block ký hiệu đã chuẩn hoá: 2 điểm (DBPoint) ở 2 góc chéo
        /// (-0.5,-0.5) và (0.5,0.5) trên layer LHB_KY_HIEU_KHUNG tắt + không in. Hình đã chuẩn hoá (cạnh dài = 1, tâm
        /// tại gốc) nằm gọn trong khung. GeometricExtents (AutoFit dùng) tính cả đối tượng trên layer tắt -> mọi block
        /// ký hiệu có khung bao như nhau. Block đã có khung thì bỏ qua (block ảnh tuỳ chỉnh dùng lại giữa các lần xuất).
        /// </summary>
        private static void EnsureSquareFrame(Database db, Transaction tr, ObjectId symBtrId, string symName)
        {
            ObjectId layerId = GetFrameLayer(db, tr);
            var btr = (BlockTableRecord)tr.GetObject(symBtrId, OpenMode.ForRead);
            var pointClass = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(DBPoint));
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.IsDerivedFrom(pointClass) &&
                    ((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer.StartsWith(FrameLayerName, StringComparison.OrdinalIgnoreCase)) return;
            }

            btr.UpgradeOpen();
            var points = new List<DBPoint>();
            foreach (var p in new[] { new Point3d(-0.5, -0.5, 0), new Point3d(0.5, 0.5, 0) })
            {
                var pt = new DBPoint(p);
                pt.SetDatabaseDefaults(db);
                pt.LayerId = layerId;
                btr.AppendEntity(pt);
                tr.AddNewlyCreatedDBObject(pt, true);
                points.Add(pt);
            }

            // Kiểm tra khung bao block (GeometricExtents = cái AutoFit dùng). Nếu bản CAD này bỏ qua đối tượng trên layer
            // tắt -> khung không vuông -> chuyển 2 điểm sang layer bật (không in, màu xám tối, chỉ là 2 chấm nhỏ trên màn hình)
            string check = ProbeExtents(symBtrId, out bool square);
            if (!square)
            {
                ObjectId visibleLayer = DrawingHelper.GetOrCreateLayer(db, tr, FrameLayerName + "_HIEN", 250, false);
                foreach (var pt in points) pt.LayerId = visibleLayer;
                string recheck = ProbeExtents(symBtrId, out square);
                Logger.Warn($"[TableExporterAcad.SquareFrame] '{symName}': khung bao với điểm trên layer tắt = {check} (không vuông) -> " +
                            $"chuyển điểm sang layer {FrameLayerName}_HIEN (bật, không in): khung bao = {recheck}");
                return;
            }
            Logger.Log($"[TableExporterAcad.SquareFrame] '{symName}': thêm khung vuông 1x1 (2 điểm, layer {FrameLayerName} tắt / không in), " +
                       $"khung bao block = {check}");
        }

        /// <summary>Khung bao của block ký hiệu đặt tại gốc, tỉ lệ 1 (giống AutoFit đo). square = khung 1 x 1.</summary>
        private static string ProbeExtents(ObjectId btrId, out bool square)
        {
            square = false;
            try
            {
                using (var probe = new BlockReference(Point3d.Origin, btrId))
                {
                    if (!ExtentsHelper.TryGetOwnExtents(probe, out var e)) return "KHÔNG tính được";
                    double w = e.MaxPoint.X - e.MinPoint.X, h = e.MaxPoint.Y - e.MinPoint.Y;
                    square = Math.Abs(w - 1) < 1e-3 && Math.Abs(h - 1) < 1e-3;
                    return $"{w:0.###}x{h:0.###}";
                }
            }
            catch (Exception ex)
            {
                // Không đo được thì giữ layer tắt (không làm hỏng bảng)
                square = true;
                return "lỗi đo: " + ex.Message;
            }
        }

        private static ObjectId GetFrameLayer(Database db, Transaction tr)
        {
            ObjectId id = DrawingHelper.GetOrCreateLayer(db, tr, FrameLayerName, 8, false);
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!ltr.IsOff || ltr.IsPlottable)
            {
                ltr.UpgradeOpen();
                ltr.IsOff = true;
                ltr.IsPlottable = false;
                Logger.Log($"[TableExporterAcad] Layer '{FrameLayerName}': tắt + không in");
            }
            return id;
        }

        /// <summary>
        /// Tạo block ký hiệu cho 1 thiết bị. MỌI trường hợp đều ra block "LHB_SYM_&lt;tên&gt;_&lt;hash&gt;" đã chuẩn hoá:
        ///  - Ảnh tuỳ chỉnh   -> "LHB_IMG_&lt;hash&gt;" chứa RasterImage căn giữa gốc.
        ///  - Có visibility    -> chép hình trong BTR ẩn danh của 1 instance (giữ trạng thái visibility, bỏ xoay/tỉ lệ instance).
        ///  - Không visibility -> 1 BlockReference tới block definition (chèn block bình thường).
        /// Tên cố định theo (block gốc, visibility) nên xuất lại sẽ ghi đè, không sinh block rác.
        /// </summary>
        private static SymbolInfo ResolveSymbol(Database db, Transaction tr, BlockItem item, Dictionary<string, SymbolInfo> cache)
        {
            // a) Ảnh tuỳ chỉnh
            if (!string.IsNullOrEmpty(item.CustomImagePath) && File.Exists(item.CustomImagePath))
                return GetOrCreateImageBlock(db, tr, item.CustomImagePath);

            ObjectId defId = !item.DynamicBtrId.IsNull && item.DynamicBtrId.IsValid ? item.DynamicBtrId : item.SourceBtrId;
            string baseName = defId.IsNull ? item.BlockName : GetBtrName(tr, defId);

            // v9.4: block của XREF (đếm trong XREF): không chèn / chép định nghĩa phụ thuộc XREF vào block ký hiệu (gỡ / nạp
            // lại XREF làm hỏng tham chiếu) -> dùng ảnh ký hiệu đã render (chép cạnh DWG như ảnh tuỳ chỉnh)
            if (IsXrefDependent(tr, defId))
            {
                if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                {
                    Logger.Log($"[TableExporterAcad] '{baseName}' là block của XREF -> ô ký hiệu dùng ảnh '{item.ThumbnailPath}'");
                    return GetOrCreateImageBlock(db, tr, item.ThumbnailPath);
                }
                Logger.Warn($"[TableExporterAcad] '{baseName}' là block của XREF, chưa có ảnh ký hiệu -> để trống ô");
                return new SymbolInfo { Mode = "XrefNoImage" };
            }

            // b) Dynamic block (có chủng loại, hoặc v9.6: block động chỉ có tham số độ dài / góc - chủng loại nay để trống
            // nhưng vẫn chép hình từ block thật như trước) -> copy instance
            ObjectId instanceId = ObjectId.Null;
            if (!string.IsNullOrEmpty(item.VisibilityState) || item.IsDynamic)
            {
                instanceId = (item.ObjectIds ?? new List<ObjectId>())
                    .FirstOrDefault(id => !id.IsNull && id.IsValid && !id.IsErased);
                if (instanceId.IsNull)
                    Logger.Warn($"[TableExporterAcad] '{item.BlockName}' (block động, chủng loại '{item.VisibilityState}') không còn instance hợp lệ -> dùng definition.");
            }

            if (instanceId.IsNull && (defId.IsNull || !defId.IsValid))
                return new SymbolInfo { Mode = "NoBlock" };

            var sym = new SymbolInfo { Mode = instanceId.IsNull ? "Definition" : "InstanceBtr" };
            // v9.6: dòng tách theo kích thước có hình riêng (tủ 1200 x 600 khác 600 x 600) -> kích thước vào tên block ký hiệu
            string symKey = baseName + "|" + (item.VisibilityState ?? "") + (!instanceId.IsNull && !string.IsNullOrEmpty(item.Size) ? "|" + item.Size : "");
            string symName = "LHB_SYM_" + SanitizeSymbolName(baseName) + "_" + ShortHash(symKey);
            // Nhiều dòng cùng block + chủng loại (tách theo layer / thuộc tính, dòng gộp): dựng block ký hiệu 1 lần / lần xuất
            if (cache != null && cache.TryGetValue(symName, out var built)) return built;
            ObjectId symBtrId = GetOrResetSymbolBtr(db, tr, symName);

            List<Entity> entities;
            if (!instanceId.IsNull)
            {
                // Dynamic block: chép hình trong BTR ẩn danh của instance (đúng chủng loại) theo toạ độ định nghĩa block.
                // = đúng hình đang thấy trên cột Ký hiệu của form (render cùng BTR này). Trước đây copy instance rồi
                // explode: ký hiệu bị xoay 90° so với block và mũi tên thành nét đứt (explode block có tỉ lệ làm mất
                // tỉ lệ nét của linetype), test 29/09/2026.
                var src = (BlockReference)tr.GetObject(instanceId, OpenMode.ForRead);
                var symBtr = (BlockTableRecord)tr.GetObject(symBtrId, OpenMode.ForWrite);
                entities = CopyInstanceBtr(tr, symBtr, src, symName);
                if (entities.Count == 0)
                {
                    Logger.Warn($"[TableExporterAcad.InstanceBtr] '{symName}': không chép được entity nào từ BTR của instance Handle={src.Handle}.");
                    return new SymbolInfo { Mode = "InstanceBtrFailed" };
                }
            }
            else
            {
                // Chèn block definition bình thường vào block bọc
                var symBtr = (BlockTableRecord)tr.GetObject(symBtrId, OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, defId);
                symBtr.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                Logger.Log($"[TableExporterAcad.Definition] '{symName}' <- chèn block '{baseName}'");

                // AutoFit đo bằng GeometricExtents của AutoCAD: block ném eInvalidExtents -> phẳng hoá để AutoFit đo đúng
                entities = new List<Entity> { br };
                bool ownExtentsOk = ExtentsHelper.TryGetOwnExtents(br, out _);
                if (!ownExtentsOk)
                {
                    var flat = FlattenReference(tr, symBtr, br, symName, ownExtentsOk);
                    if (flat.Count > 0)
                    {
                        br.Erase();
                        entities = flat;
                        sym.Mode += "+Flat";
                    }
                }
            }

            LogLinetypes(tr, entities, symName, "trước chuẩn hoá");
            NormalizeEntities(entities, symName, out sym.Width, out sym.Height);
            if (sym.IsNormalized) EnsureSquareFrame(db, tr, symBtrId, symName);
            sym.BtrId = symBtrId;
            if (cache != null) cache[symName] = sym;
            return sym;
        }

        /// <summary>
        /// Chép các entity ĐANG HIỆN trong BTR thực của instance (với dynamic block là BTR ẩn danh *U của đúng chủng loại)
        /// vào block ký hiệu, giữ nguyên toạ độ định nghĩa block (không xoay, không tỉ lệ của instance).
        /// Layer 0 / ByBlock gán theo instance để giữ màu. Block con ném eInvalidExtents thì phẳng hoá tiếp.
        /// Thuộc tính của instance chép thành chữ, đưa về toạ độ định nghĩa block.
        /// </summary>
        private static List<Entity> CopyInstanceBtr(Transaction tr, BlockTableRecord symBtr, BlockReference src, string symName)
        {
            var kept = new List<Entity>();
            int dropped = 0, exploded = 0;
            var srcBtr = (BlockTableRecord)tr.GetObject(src.BlockTableRecord, OpenMode.ForRead);

            var ids = new ObjectIdCollection();
            foreach (ObjectId id in srcBtr)
            {
                // Entity ẩn (visibility đang tắt) và AttributeDefinition thường (instance không vẽ ra) -> bỏ
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || !ent.Visible || (ent is AttributeDefinition ad && !ad.Constant))
                {
                    dropped++;
                    continue;
                }
                ids.Add(id);
            }

            if (ids.Count > 0)
            {
                var mapping = new IdMapping();
                srcBtr.Database.DeepCloneObjects(ids, symBtr.ObjectId, mapping, false);
                foreach (ObjectId id in ids)
                {
                    var pair = mapping[id];
                    if (!pair.IsCloned || pair.Value.IsNull) { dropped++; continue; }
                    var ent = (Entity)tr.GetObject(pair.Value, OpenMode.ForWrite);
                    InheritFromParent(tr, ent, src);

                    // Block con không có extents (ĐÈN EXIT) -> AutoFit đo sai -> phẳng hoá tiếp
                    if (ent is BlockReference sub && !ExtentsHelper.TryGetOwnExtents(sub, out _))
                    {
                        if (ExplodeInto(tr, symBtr, sub, kept, ref dropped, 1))
                        {
                            sub.Erase();
                            exploded++;
                            continue;
                        }
                    }
                    kept.Add(ent);
                }
            }

            // Thuộc tính của instance: toạ độ WCS -> đưa về toạ độ định nghĩa block
            int attCount = 0;
            var toDef = src.BlockTransform.Inverse();
            foreach (ObjectId attId in src.AttributeCollection)
            {
                try
                {
                    if (!(tr.GetObject(attId, OpenMode.ForRead) is AttributeReference ar)) continue;
                    if (ar.Invisible || !ar.Visible || string.IsNullOrWhiteSpace(ar.TextString)) continue;

                    Entity txt = ar.IsMTextAttribute ? (Entity)ar.MTextAttribute : CopyAttributeAsText(ar);
                    txt.SetPropertiesFrom(ar);
                    InheritFromParent(tr, txt, src);
                    txt.TransformBy(toDef);
                    if (!ExtentsHelper.TryGetOwnExtents(txt, out _))
                    {
                        txt.Dispose();
                        continue;
                    }
                    symBtr.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    kept.Add(txt);
                    attCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[TableExporterAcad.InstanceBtr] '{symName}': bỏ 1 thuộc tính: {ex.Message}");
                }
            }

            Logger.Log($"[TableExporterAcad.InstanceBtr] '{symName}' <- BTR '{srcBtr.Name}' của instance Handle={src.Handle} " +
                       $"(layer='{src.Layer}', xoay instance={src.Rotation * 180.0 / Math.PI:0.##}° -> bỏ, scale instance={src.ScaleFactors.X:0.####} -> bỏ): " +
                       $"giữ {kept.Count} entity (gồm {attCount} thuộc tính, phẳng hoá {exploded} block con), bỏ {dropped} entity ẩn / không có extents");
            return kept;
        }

        /// <summary>Log linetype của các entity nét đứt trong block ký hiệu (để kiểm tra lỗi nét đứt lạ).</summary>
        private static void LogLinetypes(Transaction tr, List<Entity> entities, string symName, string stage)
        {
            try
            {
                var groups = new Dictionary<string, int>();
                foreach (var ent in entities)
                {
                    string lt = ent.Linetype;
                    if (string.Equals(lt, "ByLayer", StringComparison.OrdinalIgnoreCase))
                    {
                        var layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                        lt = "ByLayer=" + ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name;
                    }
                    if (lt.EndsWith("Continuous", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = $"{lt} (ltscale {ent.LinetypeScale:0.####}, {ent.GetType().Name})";
                    groups[key] = groups.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                Logger.Log($"[TableExporterAcad.Linetype] '{symName}' {stage}: " +
                           (groups.Count == 0 ? "toàn nét liền (Continuous)" : string.Join("; ", groups.Select(kv => $"{kv.Value} x {kv.Key}"))));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad.Linetype] '{symName}': không đọc được linetype: {ex.Message}");
            }
        }

        private const int MaxFlattenDepth = 6;

        /// <summary>
        /// Phẳng hoá: explode (đệ quy) BlockReference thành entity rời trong block ký hiệu, chỉ giữ entity đang hiện
        /// và có GeometricExtents hợp lệ. Layer 0 / ByBlock được gán theo block cha để giữ nguyên màu hiển thị.
        /// Thuộc tính (AttributeReference) chép thành chữ. Trả về danh sách rỗng nếu explode thất bại (giữ block copy).
        /// </summary>
        private static List<Entity> FlattenReference(Transaction tr, BlockTableRecord owner, BlockReference br, string symName, bool ownExtentsOk)
        {
            var kept = new List<Entity>();
            int dropped = 0;
            if (!ExplodeInto(tr, owner, br, kept, ref dropped, 0))
            {
                Logger.Warn($"[TableExporterAcad.Flatten] '{symName}': explode thất bại -> giữ block copy, AutoFit có thể to nhỏ sai");
                return kept;
            }

            int attCount = 0;
            foreach (ObjectId attId in br.AttributeCollection)
            {
                try
                {
                    if (!(tr.GetObject(attId, OpenMode.ForRead) is AttributeReference ar)) continue;
                    if (ar.Invisible || !ar.Visible || string.IsNullOrWhiteSpace(ar.TextString)) continue;

                    Entity txt = ar.IsMTextAttribute ? (Entity)ar.MTextAttribute : CopyAttributeAsText(ar);
                    txt.SetPropertiesFrom(ar);
                    InheritFromParent(tr, txt, br);
                    if (!ExtentsHelper.TryGetOwnExtents(txt, out _))
                    {
                        txt.Dispose();
                        continue;
                    }
                    owner.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    kept.Add(txt);
                    attCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[TableExporterAcad.Flatten] '{symName}': bỏ 1 thuộc tính khi phẳng hoá: {ex.Message}");
                }
            }

            Logger.Log($"[TableExporterAcad.Flatten] '{symName}' (extents gốc {(ownExtentsOk ? "hợp lệ" : "LỖI")}): " +
                       $"giữ {kept.Count} entity (gồm {attCount} thuộc tính), bỏ {dropped} entity ẩn / không có extents");
            return kept;
        }

        private static bool ExplodeInto(Transaction tr, BlockTableRecord owner, BlockReference br, List<Entity> kept, ref int dropped, int depth)
        {
            var parts = new DBObjectCollection();
            try
            {
                br.Explode(parts);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TableExporterAcad.Flatten] Explode block '{br.Name}' (tầng {depth}) lỗi: {ex.Message}");
                foreach (DBObject o in parts) o.Dispose();
                return false;
            }

            // Nét đứt trong block co giãn theo tỉ lệ block; explode thì mất -> nhân tỉ lệ block vào linetype scale
            // để nét trông giống hệt lúc còn là block (lỗi mũi tên EXIT thành nét đứt, test 29/09/2026)
            double brScale = Math.Sqrt(Math.Abs(br.ScaleFactors.X * br.ScaleFactors.Y));
            if (double.IsNaN(brScale) || brScale < 1e-9) brScale = 1.0;

            foreach (DBObject o in parts)
            {
                bool appended = false;
                try
                {
                    // Entity ẩn (visibility đang tắt) và AttributeDefinition thường (instance không vẽ ra) -> bỏ
                    if (!(o is Entity ent) || !ent.Visible || (ent is AttributeDefinition ad && !ad.Constant))
                    {
                        dropped++;
                        continue;
                    }

                    InheritFromParent(tr, ent, br);
                    if (!(ent is BlockReference) && Math.Abs(brScale - 1.0) > 1e-9)
                        ent.LinetypeScale = ent.LinetypeScale * brScale;
                    if (ExtentsHelper.TryGetOwnExtents(ent, out _))
                    {
                        owner.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                        kept.Add(ent);
                        appended = true;
                    }
                    else if (!(ent is BlockReference sub && depth < MaxFlattenDepth && ExplodeInto(tr, owner, sub, kept, ref dropped, depth + 1)))
                    {
                        dropped++;
                    }
                }
                catch (Exception ex)
                {
                    dropped++;
                    Logger.Warn($"[TableExporterAcad.Flatten] Bỏ 1 {o.GetType().Name} khi phẳng hoá (tầng {depth}): {ex.Message}");
                }
                finally
                {
                    if (!appended) o.Dispose();
                }
            }
            return true;
        }

        /// <summary>Entity trong block ở layer 0 / ByBlock hiển thị theo block cha -> gán cụ thể trước khi tách khỏi block cha.</summary>
        private static void InheritFromParent(Transaction tr, Entity ent, BlockReference parent)
        {
            var db = parent.LayerId.Database;
            if (ent.LayerId == db.LayerZero) ent.LayerId = parent.LayerId;
            if (ent.Color.IsByBlock) ent.Color = ResolveColor(tr, parent);
            if (ent.LinetypeId == db.ByBlockLinetype) ent.LinetypeId = parent.LinetypeId;
            if (ent.LineWeight == LineWeight.ByBlock) ent.LineWeight = parent.LineWeight;
        }

        // Entity ByBlock trong block cha màu ByLayer hiển thị bằng màu layer của block cha
        private static Color ResolveColor(Transaction tr, Entity parent)
        {
            if (!parent.Color.IsByLayer) return parent.Color;
            try { return ((LayerTableRecord)tr.GetObject(parent.LayerId, OpenMode.ForRead)).Color; }
            catch { return parent.Color; }
        }

        private static DBText CopyAttributeAsText(AttributeReference ar)
        {
            // Đặt 2 mode trước rồi mới đặt AlignmentPoint (như DBText thường)
            var t = new DBText
            {
                Normal = ar.Normal,
                Thickness = ar.Thickness,
                Position = ar.Position,
                Height = ar.Height,
                WidthFactor = ar.WidthFactor,
                Oblique = ar.Oblique,
                Rotation = ar.Rotation,
                TextStyleId = ar.TextStyleId,
                TextString = ar.TextString,
                IsMirroredInX = ar.IsMirroredInX,
                IsMirroredInY = ar.IsMirroredInY,
                HorizontalMode = ar.HorizontalMode,
                VerticalMode = ar.VerticalMode
            };
            if (ar.HorizontalMode != TextHorizontalMode.TextLeft || ar.VerticalMode != TextVerticalMode.TextBase)
                t.AlignmentPoint = ar.AlignmentPoint;
            return t;
        }

        /// <summary>Lấy block ký hiệu theo tên; nếu đã có thì xoá nội dung cũ để làm lại.</summary>
        private static ObjectId GetOrResetSymbolBtr(Database db, Transaction tr, string symName)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(symName))
            {
                ObjectId id = bt[symName];
                var oldBtr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                int erased = 0;
                foreach (ObjectId entId in oldBtr)
                {
                    tr.GetObject(entId, OpenMode.ForWrite).Erase();
                    erased++;
                }
                Logger.Log($"[TableExporterAcad] '{symName}' đã có, xoá {erased} entity cũ để làm lại.");
                return id;
            }

            bt.UpgradeOpen();
            var newBtr = new BlockTableRecord { Name = symName, Origin = Point3d.Origin };
            ObjectId newId = bt.Add(newBtr);
            tr.AddNewlyCreatedDBObject(newBtr, true);
            return newId;
        }

        /// <summary>
        /// Chuẩn hoá hình trong block ký hiệu: co giãn để cạnh dài nhất = 1, dời để TÂM HÌNH nằm tại gốc toạ độ
        /// (= base point của block ký hiệu). Nhờ vậy căn giữa ô đúng dù Table căn theo base point hay theo extents.
        /// </summary>
        private static void NormalizeEntities(List<Entity> entities, string symName, out double width, out double height)
        {
            width = height = 0;
            var ext = new Extents3d();
            bool has = false;
            foreach (var ent in entities)
            {
                if (!ExtentsHelper.TryGetExtents(ent, out var e)) continue;
                if (has) ext.AddExtents(e); else { ext = e; has = true; }
            }
            if (!has)
            {
                Logger.Warn($"[TableExporterAcad.Normalize] '{symName}': không tính được extents -> để nguyên, AutoFit theo extents gốc");
                return;
            }

            double w = ext.MaxPoint.X - ext.MinPoint.X;
            double h = ext.MaxPoint.Y - ext.MinPoint.Y;
            double maxDim = Math.Max(w, h);
            if (maxDim < 1e-9)
            {
                Logger.Warn($"[TableExporterAcad.Normalize] '{symName}': extents bằng 0 ({w}x{h}) -> để nguyên, AutoFit theo extents gốc");
                return;
            }

            // Co giãn quanh gốc rồi dời tâm hình về gốc
            double k = 1.0 / maxDim;
            var center = new Vector3d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, 0);
            var m = Matrix3d.Displacement(-center * k) * Matrix3d.Scaling(k, Point3d.Origin);
            foreach (var ent in entities)
            {
                ent.TransformBy(m);
                // TransformBy không đổi linetype scale -> nhân k để nét đứt giữ đúng tỉ lệ so với hình
                // (BlockReference tự co giãn nét bên trong theo tỉ lệ block nên không cần)
                if (!(ent is BlockReference)) ent.LinetypeScale = ent.LinetypeScale * k;
            }

            width = w * k;
            height = h * k;

            // Kiểm tra lại bằng đúng extents AutoCAD dùng cho AutoFit (không explode) để lần sau debug nhanh
            var own = new Extents3d();
            bool ownHas = false;
            int bad = 0;
            foreach (var ent in entities)
            {
                if (!ExtentsHelper.TryGetOwnExtents(ent, out var e)) { bad++; continue; }
                if (ownHas) own.AddExtents(e); else { own = e; ownHas = true; }
            }
            Logger.Log($"[TableExporterAcad.Normalize] '{symName}': {entities.Count} entity, gốc {w:0.###}x{h:0.###} -> {width:0.###}x{height:0.###}; " +
                       (ownHas
                           ? $"extents AutoFit {own.MaxPoint.X - own.MinPoint.X:0.###}x{own.MaxPoint.Y - own.MinPoint.Y:0.###}, " +
                             $"tâm ({(own.MinPoint.X + own.MaxPoint.X) / 2:0.####}, {(own.MinPoint.Y + own.MaxPoint.Y) / 2:0.####})"
                           : "KHÔNG có extents AutoFit") +
                       (bad > 0 ? $", {bad} entity extents lỗi -> AutoFit có thể to nhỏ sai" : ""));
        }

        /// <summary>BTR "LHB_IMG_&lt;hash&gt;" chứa 1 RasterImage, tâm ảnh tại gốc, cạnh dài nhất = 1.</summary>
        private static SymbolInfo GetOrCreateImageBlock(Database db, Transaction tr, string imagePath)
        {
            string name = "LHB_IMG_" + ShortHash(imagePath.ToLowerInvariant());
            var sym = new SymbolInfo { Mode = "Image" };
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
            {
                sym.BtrId = bt[name];
                var oldBtr = (BlockTableRecord)tr.GetObject(sym.BtrId, OpenMode.ForRead);
                // Bảng xuất trước v9.4: ảnh trỏ vào %APPDATA% -> chép cạnh DWG, trỏ lại
                TableExporter.RelinkImagesToDrawing(db, tr, oldBtr, name);
                if (ExtentsHelper.TryGetBtrExtents(tr, oldBtr, out var e))
                {
                    sym.Width = e.MaxPoint.X - e.MinPoint.X;
                    sym.Height = e.MaxPoint.Y - e.MinPoint.Y;
                }
                // Block ảnh tạo từ bản trước v9.2 chưa có khung vuông -> thêm
                if (sym.IsNormalized) EnsureSquareFrame(db, tr, sym.BtrId, name);
                return sym;
            }

            var imgDictId = RasterImageDef.GetImageDictionary(db);
            if (imgDictId.IsNull) imgDictId = RasterImageDef.CreateImageDictionary(db);
            var imgDict = (DBDictionary)tr.GetObject(imgDictId, OpenMode.ForWrite);

            // v9.4: chép ảnh vào <thư mục DWG>\<tên DWG>_LHBImages\ và trỏ đường dẫn tương đối -> gửi DWG kèm thư mục ảnh
            // không mất ảnh (trước đây trỏ thẳng %APPDATA%\LHBBlockScheduler\CustomImages của máy xuất bảng)
            var imgDef = new RasterImageDef();
            string localPath = TableExporter.LocalizeImage(db, imagePath, out string relativePath);
            TableExporter.SetImageSource(imgDef, localPath, relativePath, name);
            ObjectId imgDefId = imgDict.SetAt(name, imgDef);
            tr.AddNewlyCreatedDBObject(imgDef, true);

            // Giữ đúng tỉ lệ khung ảnh, cạnh dài nhất = 1, tâm tại gốc
            double px = imgDef.Size.X, py = imgDef.Size.Y;
            double w = px >= py ? 1.0 : (py > 0 ? px / py : 1.0);
            double h = px >= py ? (px > 0 ? py / px : 1.0) : 1.0;

            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            sym.BtrId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            var img = new RasterImage
            {
                ImageDefId = imgDefId,
                Orientation = new CoordinateSystem3d(new Point3d(-w / 2, -h / 2, 0), new Vector3d(w, 0, 0), new Vector3d(0, h, 0))
            };
            btr.AppendEntity(img);
            tr.AddNewlyCreatedDBObject(img, true);
            img.AssociateRasterDef(imgDef);

            sym.Width = w;
            sym.Height = h;
            Logger.Log($"[TableExporterAcad.ImageBlock] Tạo '{name}' từ ảnh '{imagePath}' ({px}x{py} px -> {w:0.###}x{h:0.###})");
            EnsureSquareFrame(db, tr, sym.BtrId, name);
            return sym;
        }

        private static bool IsXrefDependent(Transaction tr, ObjectId btrId)
        {
            if (btrId.IsNull || !btrId.IsValid) return false;
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                return btr.IsDependent || btr.IsFromExternalReference;
            }
            catch { return false; }
        }

        private static string GetBtrName(Transaction tr, ObjectId btrId)
        {
            try { return ((BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead)).Name; }
            catch { return btrId.ToString(); }
        }

        private static string SanitizeSymbolName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "BLOCK";
            var sb = new StringBuilder();
            foreach (char ch in name)
                sb.Append("<>/\\\":;?*|,=`".IndexOf(ch) >= 0 || char.IsControl(ch) ? '_' : ch);
            string s = sb.ToString().Trim();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        /// <summary>8 ký tự hex đầu MD5 (giữ đúng tên block ký hiệu như bản trước; máy bật FIPS tự tính MD5 - v9.4).</summary>
        private static string ShortHash(string s) => HashHelper.ShortHash(s);
    }
}

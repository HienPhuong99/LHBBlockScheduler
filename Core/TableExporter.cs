using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    public class ColumnExportDef
    {
        public string Key { get; set; }
        public string HeaderText { get; set; }
        /// <summary>Độ rộng cột trên grid của form (pixel), 0 = không có. User kéo rộng cột trên form -> bảng xuất rộng theo.</summary>
        public double GridWidthPx { get; set; }
    }

    public class TableExportConfig
    {
        public double TableScale { get; set; } = 100.0;
        public double BaseTextHeight { get; set; } = 2.5;
        public double BaseRowHeight { get; set; } = 7.5;
        /// <summary>Chiều cao dòng grid (pixel) - quy đổi pixel cột grid ra đơn vị bản vẽ: 1 dòng grid = 1 dòng bảng.</summary>
        public double GridRowHeightPx { get; set; }
        public List<ColumnExportDef> Columns { get; set; } = new List<ColumnExportDef>();
        /// <summary>Mẫu bảng (Premium P10). Null = mẫu đang chọn trong settings.</summary>
        public TableTemplate Template { get; set; }
        /// <summary>Thông tin vùng quét để bảng tự cập nhật (Premium P2). Null = không lưu.</summary>
        public TableScanInfo ScanInfo { get; set; }

        public double ActualTextHeight => BaseTextHeight * (TableScale > 0 ? TableScale : 100.0);
        public double ActualRowHeight => BaseRowHeight * (TableScale > 0 ? TableScale : 100.0);
    }

    public static class TableExporter
    {
        private const string TextStyleName = "LHB_TABLE";

        /// <summary>
        /// Xuất bảng thống kê ra Model Space dạng Line + DBText + BlockReference rời rạc, gom vào Group.
        /// </summary>
        public static void ExportTable(Document doc, List<BlockItem> items, Point3d insertionPoint, TableExportConfig config = null)
        {
            config ??= new TableExportConfig();
            var db = doc.Database;
            var sortedItems = items.OrderBy(i => i.Order).ToList();
            var createdIds = new List<ObjectId>();

            // 1. Xác định danh sách cột cần xuất (STT + các cột đang hiển thị)
            var exportCols = BuildExportColumns(config);

            // Mẫu bảng (Premium P10): cỡ chữ, chiều cao dòng, font, header chữ hoa, dòng tổng
            var tpl = config.Template ?? TableTemplate.Current;
            double textHeight = config.ActualTextHeight * tpl.EffectiveTextFactor;
            double rowHeight = config.ActualRowHeight * tpl.EffectiveRowFactor;

            // 2. Tính toán độ rộng từng cột tự động co giãn
            double[] colWidths = CalculateColumnWidths(exportCols, sortedItems, textHeight, rowHeight, config.GridRowHeightPx);
            double totalWidth = colWidths.Sum();
            int rowCount = sortedItems.Count + 1 + (tpl.AddTotalRow ? 1 : 0); // +1 cho header, +1 dòng tổng
            double totalHeight = rowCount * rowHeight;

            int expectedEntities = (rowCount + 1) + (colWidths.Length + 1); // lines
            expectedEntities += exportCols.Count; // headers
            expectedEntities += sortedItems.Count * (exportCols.Count - 1); // data cells

            Logger.Log($"TableExporter.ExportTable: xuất {sortedItems.Count} dòng, {exportCols.Count} cột, scale={config.TableScale} tại {insertionPoint}");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Tạo hoặc lấy TextStyle LHB_TABLE font Arial hỗ trợ tiếng Việt
                ObjectId textStyleId = GetOrCreateTextStyle(db, tr, tpl.TextStyleName, tpl.EffectiveFont);

                // 1. Vẽ lưới (Line)
                createdIds.AddRange(DrawGrid(ms, tr, insertionPoint, totalWidth, totalHeight, rowHeight, colWidths, rowCount));

                // 2. Vẽ Header
                string[] headerTexts = exportCols.Select(c => tpl.UppercaseHeader ? (c.HeaderText ?? "").ToUpper() : c.HeaderText).ToArray();
                createdIds.AddRange(DrawRow(ms, tr, insertionPoint, 0, rowHeight, colWidths, textHeight, textStyleId, headerTexts));

                // 3. Vẽ từng dòng dữ liệu
                for (int r = 0; r < sortedItems.Count; r++)
                {
                    var item = sortedItems[r];
                    int rowIndex = r + 1;
                    var cellTexts = new string[exportCols.Count];
                    var cellAligns = new CellHAlign[exportCols.Count];

                    for (int c = 0; c < exportCols.Count; c++)
                    {
                        var col = exportCols[c];
                        cellAligns[c] = item.GetAlignment(col.Key);
                        if (col.Key == "STT")
                        {
                            cellTexts[c] = (r + 1).ToString();
                        }
                        else if (col.Key == "colThumbnail")
                        {
                            cellTexts[c] = ""; // ô ký hiệu chèn block/ảnh bên dưới
                        }
                        else
                        {
                            cellTexts[c] = GetItemTextForColumn(item, col.Key);
                        }
                    }

                    createdIds.AddRange(DrawRow(ms, tr, insertionPoint, rowIndex, rowHeight, colWidths, textHeight, textStyleId, cellTexts, cellAligns));

                    // Xử lý ô KÝ HIỆU (Thumbnail / BlockReference)
                    int thumbColIdx = exportCols.FindIndex(c => c.Key == "colThumbnail");
                    if (thumbColIdx >= 0)
                    {
                        double cellLeft = 0;
                        for (int k = 0; k < thumbColIdx; k++) cellLeft += colWidths[k];
                        double cellWidth = colWidths[thumbColIdx];
                        // Dòng chạy xuống dưới: tâm ô = đỉnh dòng - nửa chiều cao dòng
                        var cellCenter = insertionPoint + new Vector3d(cellLeft + cellWidth / 2.0, -rowIndex * rowHeight - rowHeight / 2.0, 0);

                        // Nếu user có gán ảnh tuỳ chỉnh CustomImagePath -> chèn RasterImage
                        if (!string.IsNullOrEmpty(item.CustomImagePath) && File.Exists(item.CustomImagePath))
                        {
                            // Góc DƯỚI-trái của dòng (ảnh vẽ từ đây lên trên)
                            var rowOrigin = insertionPoint + new Vector3d(0, -(rowIndex + 1) * rowHeight, 0);
                            var imgId = InsertCustomRasterImage(doc, ms, tr, db, item.CustomImagePath, rowOrigin, cellLeft, cellWidth, rowHeight);
                            if (imgId != ObjectId.Null) createdIds.Add(imgId);
                        }
                        else
                        {
                            // Mặc định: Chèn BlockReference vector thật căn giữa ô
                            var brId = InsertBlockReferenceSymbol(ms, tr, item, cellCenter, cellWidth, rowHeight);
                            if (brId != ObjectId.Null) createdIds.Add(brId);
                        }
                    }
                }

                // Dòng tổng (mẫu bảng): nhãn ở cột Tên thiết bị (hoặc cột đầu), cộng cột SL + khu vực
                if (tpl.AddTotalRow)
                {
                    var totals = new string[exportCols.Count];
                    int labelCol = exportCols.FindIndex(c => c.Key == "colDisplayName");
                    totals[labelCol >= 0 ? labelCol : 0] = tpl.EffectiveTotalLabel;
                    for (int c = 0; c < exportCols.Count; c++)
                    {
                        if (!TableExporterAcad.IsSummableColumn(exportCols[c].Key)) continue;
                        long sum = 0;
                        foreach (var it in sortedItems)
                            if (long.TryParse(GetItemTextForColumn(it, exportCols[c].Key), out long v)) sum += v;
                        totals[c] = sum.ToString();
                    }
                    createdIds.AddRange(DrawRow(ms, tr, insertionPoint, sortedItems.Count + 1, rowHeight, colWidths, textHeight, textStyleId, totals));
                }

                // Đường dẫn từ mép phải từng dòng thiết bị tới chỗ block trùng (dòng dữ liệu r nằm ở hàng r+1, sau header)
                try
                {
                    var anchors = sortedItems
                        .Select((item, r) => (item, insertionPoint + new Vector3d(totalWidth, -(r + 1) * rowHeight - rowHeight / 2.0, 0)))
                        .ToList();
                    DuplicateFinder.DrawTableLeaders(tr, db, ms, anchors, rowHeight * 0.5, textHeight);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "TableExporter: lỗi vẽ đường dẫn tới block trùng");
                }

                tr.Commit();
                Logger.Log($"TableExporter.ExportTable: đã hoàn tất vẽ, tạo {createdIds.Count}/{expectedEntities} entity dự kiến.");
            }

            // Gom Group
            if (createdIds.Count > 0)
            {
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                        var group = new Group("Bảng thống kê Block", true);
                        groupDict.SetAt("LHB_SCHEDULE_" + Guid.NewGuid().ToString("N").Substring(0, 8), group);
                        tr.AddNewlyCreatedDBObject(group, true);

                        var idsCollection = new ObjectIdCollection(createdIds.ToArray());
                        group.InsertAt(0, idsCollection);
                        tr.Commit();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "TableExporter: gom Group thất bại");
                }

                // Zoom tới bảng
                try
                {
                    var tableExt = new Extents3d(
                        new Point3d(insertionPoint.X, insertionPoint.Y - totalHeight, 0),
                        new Point3d(insertionPoint.X + totalWidth, insertionPoint.Y, 0));
                    ScheduleManager.ZoomToExtents(doc, tableExt);
                    doc.Editor.WriteMessage($"\nĐã xuất bảng thống kê gồm {sortedItems.Count} dòng ({createdIds.Count} entity).\n");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "TableExporter: lỗi zoom tới bảng");
                }
            }
        }

        /// <summary>Danh sách cột xuất: STT + các cột đang hiển thị (dùng chung cho cả 2 kiểu bảng).</summary>
        internal static List<ColumnExportDef> BuildExportColumns(TableExportConfig config)
        {
            var exportCols = new List<ColumnExportDef> { new ColumnExportDef { Key = "STT", HeaderText = "STT" } };

            if (config.Columns != null && config.Columns.Count > 0)
            {
                foreach (var col in config.Columns)
                {
                    // Loại trừ cột nội bộ UI: colStatus, colSource, colDup (báo trùng chỉ để xem trên form)
                    if (col.Key == "colStatus" || col.Key == "colSource" || col.Key == "colDup") continue;
                    exportCols.Add(col);
                }
            }
            else
            {
                // Bảng chuẩn mặc định 5 cột
                exportCols.Add(new ColumnExportDef { Key = "colThumbnail", HeaderText = "KÝ HIỆU" });
                exportCols.Add(new ColumnExportDef { Key = "colDisplayName", HeaderText = "TÊN THIẾT BỊ" });
                exportCols.Add(new ColumnExportDef { Key = "colVisibility", HeaderText = "CHỦNG LOẠI" });
                exportCols.Add(new ColumnExportDef { Key = "colCount", HeaderText = "SỐ LƯỢNG" });
            }
            return exportCols;
        }

        /// <summary>
        /// Ước lượng bề rộng chuỗi Arial. Chiều cao chữ AutoCAD = chiều cao chữ HOA (~0.72 em),
        /// nên 1 chữ hoa rộng xấp xỉ 0.95 x chiều cao chữ. Hệ số 0.62 cũ quá nhỏ -> chữ tràn ô
        /// ("DIEN TRO CUOI NGUON" tràn sang cột bên cạnh, ảnh test 28/09/2026).
        /// </summary>
        internal static double EstimateTextWidth(string s, double textHeight)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double w = 0;
            foreach (char ch in s)
            {
                if (ch == ' ') w += 0.4;
                else if (char.IsUpper(ch) || char.IsDigit(ch)) w += 0.95;
                else w += 0.78;
            }
            return w * textHeight;
        }

        /// <summary>
        /// Độ rộng cột bảng xuất. Có độ rộng cột trên grid (gridRowHeightPx > 0) thì quy đổi theo tỉ lệ
        /// 1 dòng grid = 1 dòng bảng (vd grid dòng 48px, cột 144px -> cột bảng = 3 x chiều cao dòng):
        ///  - Cột Ký hiệu: lấy đúng độ rộng quy đổi (tối thiểu = ô vuông) -> kéo rộng trên form là bảng rộng theo
        ///    (trước đây luôn = 1.5 x chiều cao dòng, kéo trên form không có tác dụng, test 29/09/2026).
        ///  - Cột chữ: lấy số lớn hơn giữa độ rộng quy đổi và độ rộng đủ chứa chữ -> kéo rộng thì rộng theo,
        ///    kéo hẹp quá thì vẫn giữ đủ chỗ cho chữ.
        /// </summary>
        internal static double[] CalculateColumnWidths(List<ColumnExportDef> cols, List<BlockItem> items, double textHeight, double rowHeight,
                                                       double gridRowHeightPx = 0)
        {
            var widths = new double[cols.Count];
            var fromGrid = new double[cols.Count];

            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (gridRowHeightPx > 0 && col.GridWidthPx > 0)
                    fromGrid[c] = rowHeight * col.GridWidthPx / gridRowHeightPx;

                if (col.Key == "colThumbnail")
                {
                    // Không có độ rộng grid: ô rộng hơn chiều cao dòng một chút cho giống bảng mẫu
                    widths[c] = fromGrid[c] > 0 ? Math.Max(rowHeight, Math.Min(fromGrid[c], 20.0 * rowHeight)) : rowHeight * 1.5;
                    continue;
                }

                // Cột chữ: đo chuỗi rộng nhất trong cột (kể cả header)
                double maxW = EstimateTextWidth(col.HeaderText, textHeight);
                if (col.Key != "STT")
                {
                    foreach (var item in items)
                        maxW = Math.Max(maxW, EstimateTextWidth(GetItemTextForColumn(item, col.Key), textHeight));
                }

                // Padding 1.5 x chiều cao chữ mỗi bên; kẹp trong [4, 60] x chiều cao chữ
                double estW = maxW + 3.0 * textHeight;
                widths[c] = Math.Max(4.0 * textHeight, Math.Min(estW, 60.0 * textHeight));
                if (fromGrid[c] > widths[c]) widths[c] = Math.Min(fromGrid[c], 200.0 * textHeight);
            }

            Logger.Log($"[TableExporter.CalculateColumnWidths] textH={textHeight}, rowH={rowHeight}, dòng grid={gridRowHeightPx}px: " +
                       string.Join(", ", cols.Select((col, i) =>
                           $"{col.Key}={widths[i]:0.#}" + (col.GridWidthPx > 0 ? $" (grid {col.GridWidthPx}px -> {fromGrid[i]:0.#}" + (Math.Abs(widths[i] - fromGrid[i]) < 1e-6 ? ", DÙNG" : "") + ")" : ""))));
            return widths;
        }

        internal static string GetItemTextForColumn(BlockItem item, string colKey)
        {
            switch (colKey)
            {
                case "colBlockName": return item.BlockName;
                case "colDisplayName": return !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.BlockName;
                case "colVisibility": return item.VisibilityState ?? "";
                case "colBlockKind": return item.BlockKind ?? "";
                case "colUnit": return item.Unit ?? "Cái";
                case "colCount": return item.Count.ToString();
                case "colLayer": return item.LayerName ?? "";
                case "colNote": return item.Note ?? "";
                default:
                    // Cột Premium: SL theo khu vực, thuộc tính
                    return item.ExtraValues != null && item.ExtraValues.TryGetValue(colKey, out var v) ? v ?? "" : "";
            }
        }

        internal static ObjectId GetOrCreateTextStyle(Database db, Transaction tr) =>
            GetOrCreateTextStyle(db, tr, TextStyleName, "arial.ttf");

        /// <summary>Kiểu chữ theo font của mẫu bảng (mỗi font 1 kiểu chữ LHB_TABLE_xxx).</summary>
        internal static ObjectId GetOrCreateTextStyle(Database db, Transaction tr, string styleName, string fontFile)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName))
            {
                return tst[styleName];
            }

            tst.UpgradeOpen();
            var ts = new TextStyleTableRecord
            {
                Name = styleName,
                FileName = string.IsNullOrWhiteSpace(fontFile) ? "arial.ttf" : fontFile
            };
            Logger.Log($"[TableExporter] Tạo kiểu chữ '{styleName}' font '{ts.FileName}'");
            var id = tst.Add(ts);
            tr.AddNewlyCreatedDBObject(ts, true);
            return id;
        }

        private static List<ObjectId> DrawGrid(BlockTableRecord ms, Transaction tr, Point3d origin,
            double totalWidth, double totalHeight, double rowHeight, double[] colWidths, int rowCount)
        {
            var ids = new List<ObjectId>();

            // Đường ngang
            for (int r = 0; r <= rowCount; r++)
            {
                double y = -r * rowHeight;
                var line = new Line(origin + new Vector3d(0, y, 0), origin + new Vector3d(totalWidth, y, 0));
                ms.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                ids.Add(line.ObjectId);
            }

            // Đường dọc
            double x = 0;
            for (int c = 0; c <= colWidths.Length; c++)
            {
                var line = new Line(origin + new Vector3d(x, 0, 0), origin + new Vector3d(x, -totalHeight, 0));
                ms.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                ids.Add(line.ObjectId);
                if (c < colWidths.Length) x += colWidths[c];
            }

            return ids;
        }

        private static List<ObjectId> DrawRow(BlockTableRecord ms, Transaction tr, Point3d origin,
            int rowIndex, double rowHeight, double[] colWidths, double textHeight, ObjectId textStyleId, string[] cellTexts,
            CellHAlign[] cellAligns = null)
        {
            var ids = new List<ObjectId>();
            double y = -rowIndex * rowHeight;
            double x = 0;
            var db = ms.Database;

            for (int c = 0; c < colWidths.Length && c < cellTexts.Length; c++)
            {
                double colW = colWidths[c];
                if (!string.IsNullOrEmpty(cellTexts[c]))
                {
                    try
                    {
                        // Lỗi cũ: dùng "y + rowHeight/2" làm chữ nhảy lên 1 dòng, thừa 1 dòng trống ở đáy bảng
                        // Căn trái/phải: điểm neo cách đường kẻ nửa chiều cao chữ (giống lề ô của bản AutoCAD Table)
                        var align = cellAligns != null && c < cellAligns.Length ? cellAligns[c] : CellHAlign.Center;
                        double pad = textHeight * 0.5;
                        double anchorX = align == CellHAlign.Left ? x + pad
                                       : align == CellHAlign.Right ? x + colW - pad
                                       : x + colW / 2.0;
                        var anchor = origin + new Vector3d(anchorX, y - rowHeight / 2.0, 0);
                        var text = new DBText();
                        text.SetDatabaseDefaults(db);
                        text.TextStyleId = textStyleId;
                        text.TextString = cellTexts[c];
                        text.Height = textHeight;
                        text.Position = anchor;

                        // BẮT BUỘC set 2 mode TRƯỚC, rồi mới set AlignmentPoint
                        text.HorizontalMode = align == CellHAlign.Left ? TextHorizontalMode.TextLeft
                                            : align == CellHAlign.Right ? TextHorizontalMode.TextRight
                                            : TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        text.AlignmentPoint = anchor;

                        ms.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                        text.AdjustAlignment(db);

                        ids.Add(text.ObjectId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"DrawRow: lỗi ô [row={rowIndex}, col={c}, text='{cellTexts[c]}']");
                    }
                }
                x += colW;
            }

            return ids;
        }

        private static ObjectId InsertBlockReferenceSymbol(BlockTableRecord ms, Transaction tr, BlockItem item, Point3d cellCenter, double cellWidth, double rowHeight)
        {
            ObjectId btrId = item.SourceBtrId;
            if (btrId.IsNull || !btrId.IsValid)
            {
                Logger.Warn($"InsertBlockReferenceSymbol: Block '{item.BlockName}' có SourceBtrId Null, bỏ qua ô ký hiệu.");
                return ObjectId.Null;
            }

            try
            {
                // 1. BlockReference tạm ở scale 1 để đo extents thật
                using (var brTmp = new BlockReference(Point3d.Origin, btrId))
                {
                    Extents3d ext;
                    try
                    {
                        ext = brTmp.GeometricExtents;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"InsertBlockReferenceSymbol: Không tính được extents cho '{item.BlockName}': {ex.Message}");
                        return ObjectId.Null;
                    }

                    double extW = ext.MaxPoint.X - ext.MinPoint.X;
                    double extH = ext.MaxPoint.Y - ext.MinPoint.Y;

                    if (extW <= 0.0001 && extH <= 0.0001)
                    {
                        return ObjectId.Null;
                    }

                    // 2. Cạnh dài ký hiệu = 80% cạnh ngắn của ô -> mọi ký hiệu đều cỡ, ký hiệu dẹt không phình theo
                    //    cột Ký hiệu rộng (giống bảng AutoCAD Table, test 29/09/2026)
                    double scale = (Math.Min(cellWidth, rowHeight) * 0.8) / Math.Max(extW, extH);
                    if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1.0;

                    // 3. Đặt sao cho TÂM EXTENTS trùng tâm ô
                    var extCenter = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                                                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, 0);

                    var br = new BlockReference(Point3d.Origin, btrId)
                    {
                        ScaleFactors = new Scale3d(scale)
                    };
                    br.Position = cellCenter - (extCenter.GetAsVector() * scale);

                    ms.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    return br.ObjectId;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"InsertBlockReferenceSymbol: lỗi khi chèn block '{item.BlockName}'");
                return ObjectId.Null;
            }
        }

        private static ObjectId InsertCustomRasterImage(Document doc, BlockTableRecord ms, Transaction tr, Database db,
            string imagePath, Point3d rowOrigin, double cellLeft, double imgColWidth, double rowHeight)
        {
            try
            {
                string targetImagePath = imagePath;

                // Copy PNG sang <thư mục chứa DWG>\<tên DWG>_LHBImages\ để đường dẫn nằm cạnh bản vẽ
                if (!string.IsNullOrEmpty(db.Filename) && File.Exists(db.Filename))
                {
                    string dwgDir = Path.GetDirectoryName(db.Filename);
                    string dwgName = Path.GetFileNameWithoutExtension(db.Filename);
                    string localImgDir = Path.Combine(dwgDir, dwgName + "_LHBImages");
                    if (!Directory.Exists(localImgDir)) Directory.CreateDirectory(localImgDir);

                    string destFile = Path.Combine(localImgDir, Path.GetFileName(imagePath));
                    if (!File.Exists(destFile))
                    {
                        File.Copy(imagePath, destFile, true);
                    }
                    targetImagePath = destFile;
                }
                else
                {
                    MessageBox.Show("Bản vẽ hiện tại chưa được lưu ra file (chưa có đường dẫn DWG).\nẢnh tuỳ chỉnh sẽ tạm thời trỏ vào thư mục %APPDATA%. Hãy lưu bản vẽ trước khi chia sẻ file!",
                        "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                var imgDictId = RasterImageDef.GetImageDictionary(db);
                if (imgDictId == ObjectId.Null)
                    imgDictId = RasterImageDef.CreateImageDictionary(db);

                var imgDict = (DBDictionary)tr.GetObject(imgDictId, OpenMode.ForWrite);
                string key = "LHB_IMG_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                var imgDef = new RasterImageDef { SourceFileName = targetImagePath };
                imgDef.Load();
                ObjectId imgDefId = imgDict.SetAt(key, imgDef);
                tr.AddNewlyCreatedDBObject(imgDef, true);

                double margin = Math.Min(imgColWidth, rowHeight) * 0.1;
                double cellX = rowOrigin.X + cellLeft;

                var rasterImg = new RasterImage
                {
                    ImageDefId = imgDefId,
                    Orientation = new CoordinateSystem3d(
                        new Point3d(cellX + margin, rowOrigin.Y + margin, 0),
                        new Vector3d(imgColWidth - 2 * margin, 0, 0),
                        new Vector3d(0, rowHeight - 2 * margin, 0))
                };

                ms.AppendEntity(rasterImg);
                tr.AddNewlyCreatedDBObject(rasterImg, true);
                RasterImage.EnableReactors(true);
                return rasterImg.ObjectId;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"InsertCustomRasterImage('{imagePath}')");
                return ObjectId.Null;
            }
        }
    }
}

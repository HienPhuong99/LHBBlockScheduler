using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    public class TableRowKeys
    {
        public List<string> Keys { get; set; } = new List<string>();
        /// <summary>v9.4: chữ các cột tên / chủng loại... lúc ghi (đối chiếu khi cập nhật, báo dòng bị sửa tay).</summary>
        public string Label { get; set; }
    }

    /// <summary>
    /// Thông tin lưu trong bảng xuất (Extension Dictionary, JSON) để cập nhật lại sau khi sửa bản vẽ (Premium P2).
    /// Không có ObjectId (chỉ handle dạng hex).
    /// </summary>
    public class TableScanInfo
    {
        public int Version { get; set; }
        public int MaxDepth { get; set; }
        public bool CountParentBlocks { get; set; }
        public bool SplitByVisibility { get; set; }
        public bool SplitByLayer { get; set; }
        /// <summary>v9.6: tách theo kích thước (bảng cũ thiếu field -> false).</summary>
        public bool SplitBySize { get; set; }
        /// <summary>v9.4: đếm cả block trong XREF (bảng cũ thiếu field -> false = bỏ qua XREF như mặc định mới).</summary>
        public bool CountXrefBlocks { get; set; }
        public List<string> SplitAttributeKeys { get; set; } = new List<string>();
        public string TemplateSet { get; set; }
        public bool OnlyTemplate { get; set; }
        /// <summary>true = đếm cả block trùng (mặc định false = không đếm, giống ô "Không đếm trùng").</summary>
        public bool IncludeDuplicates { get; set; }
        public bool UseZones { get; set; }
        /// <summary>Đối tượng gốc đã chọn lúc quét (handle hex).</summary>
        public List<string> RootHandles { get; set; } = new List<string>();
        /// <summary>Khung vùng quét (WCS): block đặt mới trong khung này cũng được đếm khi cập nhật.</summary>
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public List<string> ColumnKeys { get; set; } = new List<string>();
        public int FirstDataRow { get; set; }
        public bool HasTotalRow { get; set; }
        public string TotalLabel { get; set; }
        public double TextHeight { get; set; }
        public double RowHeight { get; set; }
        public List<TableRowKeys> Rows { get; set; } = new List<TableRowKeys>();
        public string UpdatedAt { get; set; }
        /// <summary>
        /// v9.4: số dòng / cột của bảng lúc ghi (0 = bảng xuất trước v9.4 -> tính từ FirstDataRow + Rows). Khác lúc cập nhật
        /// = bảng bị thêm / xoá dòng, cột bằng tay -> không cập nhật (trước đây ghi nhầm dòng vì khớp theo chỉ số dòng).
        /// </summary>
        public int TableRowCount { get; set; }
        public int TableColumnCount { get; set; }
        /// <summary>
        /// v9.4: Handseed (handle kế tiếp, hex) lúc quét: khi cập nhật chỉ thêm block MỚI (handle ≥ mức này) nằm trong khung
        /// vùng quét, không kéo thêm block cũ ngoài vùng chọn ban đầu (chọn đa giác / crossing). null = bảng cũ.
        /// </summary>
        public string HandseedAtScan { get; set; }
        /// <summary>v9.4: vùng chọn quá 20000 đối tượng, RootHandles bị cắt -> cập nhật đếm mọi block trong khung như cũ.</summary>
        public bool RootsTruncated { get; set; }
    }

    /// <summary>
    /// Lệnh LHBCAPNHAT: đếm lại bảng AutoCAD Table đã xuất (Premium P2) - giống bảng Count của AutoCAD 2022+ nhưng
    /// chạy trên AutoCAD 2021. Quét lại vùng cũ (đối tượng gốc còn tồn tại + mọi block đặt mới trong khung vùng quét)
    /// với đúng tuỳ chọn lúc xuất, cập nhật cột SL / khu vực / thuộc tính của từng dòng, thêm dòng cho loại block mới,
    /// tô ĐỎ ô thay đổi. Tên, đơn vị, ghi chú user sửa tay trong bảng giữ nguyên.
    /// </summary>
    public static class TableUpdater
    {
        public const string ExtKey = "LHB_SCAN";

        /// <summary>
        /// Phiên bản TableScanInfo bảng mới ghi. 2 = v9 - v9.5; 3 = v9.6 (chủng loại không còn ghép tham số số của block
        /// động, có tách theo kích thước).
        /// </summary>
        public const int ScanInfoVersion = 3;

        public static void RunCommand(Document doc)
        {
            var ed = doc.Editor;
            var opts = new PromptSelectionOptions { MessageForAdding = "\nChọn bảng thống kê cần cập nhật <Enter = mọi bảng LHB trong bản vẽ>: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "ACAD_TABLE") });
            var res = ed.GetSelection(opts, filter);
            List<ObjectId> tables;
            if (res.Status == PromptStatus.OK) tables = res.Value.GetObjectIds().ToList();
            else if (res.Status == PromptStatus.Error || res.Status == PromptStatus.None) tables = FindAllLhbTables(doc.Database);
            else return;

            int updated = 0, skipped = 0, failed = 0;
            foreach (var id in tables)
            {
                try
                {
                    string msg = UpdateTable(doc, id);
                    if (msg == null) skipped++;
                    else
                    {
                        updated++;
                        ed.WriteMessage("\n[LHB] " + msg);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Error(ex, $"[TableUpdater] Cập nhật bảng Handle {id.Handle}");
                    ed.WriteMessage($"\n[LHB] Bảng Handle {id.Handle}: lỗi {ex.Message} (bảng bị xoá / thêm dòng bằng tay?)");
                }
            }
            if (failed > 0) ed.WriteMessage($"\n[LHB] {failed} bảng lỗi - xem log {Logger.GetLogFilePath()}");
            ed.WriteMessage($"\n[LHB] Cập nhật {updated} bảng" + (skipped > 0 ? $", bỏ qua {skipped} bảng không phải bảng LHB Premium (xuất từ v9 trở lên)" : "") + ".\n");
        }

        /// <summary>Mọi bảng LHB (có thông tin tự cập nhật) trong Model VÀ các Layout (v9.4: bảng có thể nằm trên Layout).</summary>
        public static List<ObjectId> FindAllLhbTables(Database db)
        {
            var list = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rx = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Table));
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var space = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!space.IsLayout) continue;
                    foreach (ObjectId id in space)
                    {
                        if (!id.ObjectClass.IsDerivedFrom(rx)) continue;
                        var tb = (Table)tr.GetObject(id, OpenMode.ForRead);
                        if (DrawingHelper.ReadExtString(tr, tb, ExtKey) != null) list.Add(id);
                    }
                }
                tr.Commit();
            }
            Logger.Log($"[TableUpdater] Tìm thấy {list.Count} bảng LHB trong Model + Layout");
            return list;
        }

        /// <summary>Cập nhật 1 bảng. Trả câu tóm tắt, null nếu bảng không có thông tin LHB.</summary>
        public static string UpdateTable(Document doc, ObjectId tableId)
        {
            var db = doc.Database;
            TableScanInfo info;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tb0 = (Table)tr.GetObject(tableId, OpenMode.ForRead);
                string json = DrawingHelper.ReadExtString(tr, tb0, ExtKey);
                tr.Commit();
                if (string.IsNullOrEmpty(json)) return null;
                info = JsonHelper.Deserialize<TableScanInfo>(json);
            }
            if (info == null || info.Rows == null || info.ColumnKeys == null) return null;
            info.SplitAttributeKeys ??= new List<string>();
            info.RootHandles ??= new List<string>();

            // 0. v9.4: bảng bị thêm / xoá dòng, cột bằng tay -> dừng (khớp dòng theo chỉ số sẽ ghi nhầm dòng)
            string structureError = CheckStructure(db, tableId, info);
            if (structureError != null)
            {
                Logger.Warn($"[TableUpdater] Bảng Handle {tableId.Handle}: {structureError}");
                return $"Bảng Handle {tableId.Handle}: KHÔNG cập nhật - {structureError}";
            }

            // 1. Đối tượng cần quét: gốc cũ còn tồn tại + block trong khung vùng quét
            var roots = new HashSet<ObjectId>();
            foreach (var h in info.RootHandles)
            {
                var id = DrawingHelper.FromHandle(db, h);
                if (!id.IsNull) roots.Add(id);
            }
            int oldRootsAlive = roots.Count;
            // v9.4: bảng mới lưu Handseed lúc quét -> chỉ thêm block tạo SAU lúc quét trong khung (không kéo block cũ ngoài
            // vùng chọn đa giác / crossing ban đầu). Bảng cũ / vùng chọn bị cắt: mọi block trong khung như trước.
            long handseed = 0;
            bool onlyNew = !info.RootsTruncated && !string.IsNullOrEmpty(info.HandseedAtScan) &&
                           long.TryParse(info.HandseedAtScan, System.Globalization.NumberStyles.HexNumber, null, out handseed);
            int inBox = 0, oldOutsideSelection = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in BlockExtractor.ModelSpaceBlockIds(db))
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var p = br.Position;
                    if (!(p.X >= info.MinX && p.X <= info.MaxX && p.Y >= info.MinY && p.Y <= info.MaxY)) continue;
                    inBox++;
                    if (onlyNew && id.Handle.Value < handseed && !roots.Contains(id)) { oldOutsideSelection++; continue; }
                    roots.Add(id);
                }
                tr.Commit();
            }
            Logger.Log($"[TableUpdater] Bảng Handle {tableId.Handle}: {oldRootsAlive}/{info.RootHandles.Count} gốc cũ còn lại, {inBox} block trong khung, " +
                       (onlyNew ? $"bỏ {oldOutsideSelection} block cũ không thuộc vùng chọn ban đầu (Handseed {info.HandseedAtScan})" : "bảng cũ: lấy mọi block trong khung"));

            // 2. Quét lại với đúng tuỳ chọn lúc xuất
            var template = TemplateLibraryManager.Load(info.TemplateSet);
            bool onlyTemplate = info.OnlyTemplate && template.Entries.Count > 0;
            var options = new ExtractionOptions
            {
                MaxDepth = info.MaxDepth > 0 ? info.MaxDepth : 2,
                CountParentBlocks = info.CountParentBlocks,
                SplitByVisibility = info.SplitByVisibility,
                SplitByLayer = info.SplitByLayer,
                SplitBySize = info.SplitBySize,
                SplitAttributeKeys = info.SplitAttributeKeys,
                CountXrefBlocks = info.CountXrefBlocks,
                TemplateFilter = onlyTemplate ? template : null,
                // v9.6: bảng xuất từ bản cũ (Version < 3) lưu khoá dòng theo chủng loại kiểu cũ ("Distance1=12320.33") ->
                // quét lại cũng tính chủng loại kiểu cũ để khoá khớp, không ra dòng SL 0 + dòng mới cho cùng thiết bị
                LegacyVariant = info.Version < ScanInfoVersion
            };
            if (options.LegacyVariant)
                Logger.Log($"[TableUpdater] Bảng Handle {tableId.Handle} xuất từ bản trước v9.6 (Version {info.Version}) -> chủng loại block động tính kiểu cũ");
            var items = BlockExtractor.ExtractFromDatabase(db, roots, options);
            items = TemplateLibraryManager.Apply(items, template, onlyTemplate);
            DuplicateFinder.Detect(items, SettingsManager.Current.DuplicateTolerance, SettingsManager.Current.DuplicateOverlapPercent);
            DuplicateFinder.SetExclusion(items, !info.IncludeDuplicates);
            if (info.ColumnKeys.Any(k => k.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase)))
                ZoneManager.ComputeCounts(items, ZoneManager.Load(db));
            var attrKeys = info.ColumnKeys.Where(k => k.StartsWith(PremiumColumns.AttrPrefix, StringComparison.OrdinalIgnoreCase))
                                          .Select(k => k.Substring(PremiumColumns.AttrPrefix.Length)).ToList();
            if (attrKeys.Count > 0) PremiumColumns.ComputeAttributeValues(items, attrKeys);

            var byKey = new Dictionary<string, BlockItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
                foreach (var k in it.Instances.Select(i => i.GroupKey).Where(k => !string.IsNullOrEmpty(k)).Distinct())
                    byKey[k] = it;

            // 3. Ghi vào bảng
            int changedCells = 0, newRows = 0, zeroRows = 0, renamedRows = 0;
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var red = Color.FromColorIndex(ColorMethod.ByAci, 1);
            var normal = Color.FromColorIndex(ColorMethod.ByBlock, 0);
            int countCol = info.ColumnKeys.IndexOf("colCount");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tb = (Table)tr.GetObject(tableId, OpenMode.ForWrite);
                TableExporterAcad.SuppressRegen(tb, true);
                ObjectId styleId = tb.Cells[info.FirstDataRow, 0].TextStyleId ?? ObjectId.Null;
                double textH = info.TextHeight > 0 ? info.TextHeight : (tb.Cells[info.FirstDataRow, 0].TextHeight ?? 250);
                double rowH = info.RowHeight > 0 ? info.RowHeight : tb.Rows[info.FirstDataRow].Height;

                for (int i = 0; i < info.Rows.Count; i++)
                {
                    int r = info.FirstDataRow + i;
                    if (r >= tb.Rows.Count) break;
                    var keys = info.Rows[i].Keys ?? new List<string>();
                    // Chữ tên / chủng loại khác lúc ghi: user sửa tay (được phép) - ghi log để soát khi số liệu lạ
                    if (info.Rows[i].Label != null && info.Rows[i].Label != RowLabel(tb, r, info.ColumnKeys))
                    {
                        if (renamedRows++ < 20) Logger.Log($"[TableUpdater] Dòng {i + 1}: chữ khác lúc ghi '{info.Rows[i].Label}' -> '{RowLabel(tb, r, info.ColumnKeys)}' (sửa tay?)");
                    }
                    if (keys.Count == 0) continue; // dòng nhập tay -> giữ nguyên
                    var matched = keys.Where(byKey.ContainsKey).Select(k => byKey[k]).Distinct().ToList();
                    foreach (var k in keys) usedKeys.Add(k);

                    for (int c = 0; c < info.ColumnKeys.Count && c < tb.Columns.Count; c++)
                    {
                        string ck = info.ColumnKeys[c];
                        string newText;
                        if (ck == "colCount") newText = matched.Sum(m => m.Count).ToString();
                        else if (ck.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            long s = 0;
                            foreach (var m in matched)
                                if (m.ExtraValues.TryGetValue(ck, out var v) && long.TryParse(v, out long n)) s += n;
                            newText = s > 0 ? s.ToString() : "";
                        }
                        else if (ck.StartsWith(PremiumColumns.AttrPrefix, StringComparison.OrdinalIgnoreCase))
                            newText = string.Join("; ", matched.Select(m => TableExporter.GetItemTextForColumn(m, ck)).Where(s => s.Length > 0).Distinct());
                        else continue;

                        string old = tb.Cells[r, c].TextString ?? "";
                        if (old.Trim() != newText)
                        {
                            tb.Cells[r, c].TextString = newText;
                            tb.Cells[r, c].ContentColor = red;
                            changedCells++;
                        }
                        else tb.Cells[r, c].ContentColor = normal;
                    }
                    if (matched.Count == 0) zeroRows++;
                }

                // Loại block mới (chưa có dòng nào) -> thêm dòng trước dòng tổng
                var newItems = items.Where(it => it.Instances.Select(x => x.GroupKey).Distinct().All(k => !usedKeys.Contains(k))).ToList();
                if (newItems.Count > 0)
                {
                    int insertAt = Math.Min(info.FirstDataRow + info.Rows.Count, tb.Rows.Count - (info.HasTotalRow ? 1 : 0));
                    int symbolCol = info.ColumnKeys.IndexOf("colThumbnail");
                    try
                    {
                        tb.InsertRows(insertAt, rowH, newItems.Count);
                    }
                    catch (Exception ex) when (insertAt >= tb.Rows.Count)
                    {
                        // Thêm vào cuối bảng: bản CAD không cho InsertRows sau dòng cuối -> mở rộng bảng
                        Logger.Warn($"[TableUpdater] InsertRows cuối bảng lỗi ({ex.Message}) -> SetSize");
                        int oldRows = tb.Rows.Count;
                        tb.SetSize(oldRows + newItems.Count, tb.Columns.Count);
                        for (int k = oldRows; k < tb.Rows.Count; k++) tb.Rows[k].Height = rowH;
                    }
                    for (int j = 0; j < newItems.Count; j++)
                    {
                        var it = newItems[j];
                        int r = insertAt + j;
                        for (int c = 0; c < info.ColumnKeys.Count && c < tb.Columns.Count; c++)
                        {
                            string ck = info.ColumnKeys[c];
                            if (ck == "colThumbnail") continue;
                            string text = ck == "STT" ? (info.Rows.Count + j + 1).ToString() : TableExporter.GetItemTextForColumn(it, ck);
                            TableExporterAcad.SetCellTextPublic(tb, r, c, text, textH, styleId);
                            tb.Cells[r, c].ContentColor = red;
                        }
                        if (symbolCol >= 0) TableExporterAcad.PutSymbol(db, tr, tb, r, symbolCol, it, tb.Columns[symbolCol].Width, rowH);
                        info.Rows.Add(new TableRowKeys
                        {
                            Keys = it.Instances.Select(x => x.GroupKey).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList(),
                            Label = RowLabel(tb, r, info.ColumnKeys)
                        });
                        newRows++;
                    }
                }

                // Dòng tổng
                if (info.HasTotalRow)
                {
                    int totalRow = info.FirstDataRow + info.Rows.Count;
                    if (totalRow < tb.Rows.Count)
                        TableExporterAcad.WriteTotalRow(tb, totalRow, info.FirstDataRow, info.Rows.Count, info.ColumnKeys,
                                                        new TableTemplate { TotalLabel = info.TotalLabel }, textH, styleId);
                }

                info.UpdatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                info.TableRowCount = tb.Rows.Count;
                info.TableColumnCount = tb.Columns.Count;
                DrawingHelper.WriteExtString(tr, tb, ExtKey, JsonHelper.Serialize(info));
                TableExporterAcad.SuppressRegen(tb, false);
                tb.GenerateLayout();
                tr.Commit();
            }

            var st = options.Stats;
            if (st != null) Logger.Log($"[TableUpdater] Bảng Handle {tableId.Handle}: {st.Summary()}");
            string summary = $"Bảng Handle {tableId.Handle}: quét {roots.Count} đối tượng ({oldRootsAlive} gốc cũ còn lại), " +
                             $"{changedCells} ô thay đổi (tô đỏ), {newRows} loại block mới thêm dòng" +
                             (zeroRows > 0 ? $", {zeroRows} dòng không còn block (SL = 0)" : "") +
                             (renamedRows > 0 ? $", {renamedRows} dòng có chữ tên / chủng loại đã sửa tay (giữ nguyên)" : "");
            Logger.Log("[TableUpdater] " + summary);
            return summary;
        }

        /// <summary>
        /// v9.4: số dòng / cột hiện tại phải đúng như lúc ghi (bảng cũ: tính từ FirstDataRow + số dòng + dòng tổng).
        /// Trả câu lỗi nếu bảng đã bị sửa cấu trúc bằng tay, null nếu dùng được.
        /// </summary>
        private static string CheckStructure(Database db, ObjectId tableId, TableScanInfo info)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tb = (Table)tr.GetObject(tableId, OpenMode.ForRead);
                int rows = tb.Rows.Count, cols = tb.Columns.Count;
                tr.Commit();
                int expRows = info.TableRowCount > 0 ? info.TableRowCount : info.FirstDataRow + info.Rows.Count + (info.HasTotalRow ? 1 : 0);
                int expCols = info.TableColumnCount > 0 ? info.TableColumnCount : info.ColumnKeys.Count;
                if (rows == expRows && cols == expCols) return null;
                return $"bảng đã bị thêm / xoá dòng hoặc cột bằng tay (hiện {rows} dòng x {cols} cột, lúc ghi {expRows} x {expCols}): " +
                       "cập nhật theo vị trí dòng sẽ ghi nhầm dòng. Xuất lại bảng mới từ form thống kê (LHBSCAN).";
            }
        }

        /// <summary>Chữ các cột không bị cập nhật (tên, chủng loại, đơn vị...) của 1 dòng bảng, nối bằng " | ".</summary>
        internal static string RowLabel(Table tb, int row, List<string> columnKeys)
        {
            var parts = new List<string>();
            for (int c = 0; c < columnKeys.Count && c < tb.Columns.Count; c++)
            {
                string k = columnKeys[c];
                if (k == "STT" || k == "colCount" || k == "colThumbnail" ||
                    k.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith(PremiumColumns.AttrPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                try { parts.Add((tb.Cells[row, c].TextString ?? "").Trim()); }
                catch { parts.Add(""); }
            }
            return string.Join(" | ", parts);
        }

        /// <summary>Khung vùng quét = khung bao các đối tượng đã chọn (mở rộng 1%).</summary>
        public static void FillScanBox(Database db, IEnumerable<ObjectId> roots, TableScanInfo info)
        {
            Extents3d? box = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in roots)
                {
                    if (id.IsNull || id.IsErased || !id.IsValid) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!ExtentsHelper.TryGetExtents(ent, out var e))
                    {
                        if (ent is BlockReference br) e = new Extents3d(br.Position, br.Position);
                        else continue;
                    }
                    if (box == null) box = e;
                    else { var b = box.Value; b.AddExtents(e); box = b; }
                }
                tr.Commit();
            }
            if (box == null) return;
            double mx = (box.Value.MaxPoint.X - box.Value.MinPoint.X) * 0.01 + 1;
            double my = (box.Value.MaxPoint.Y - box.Value.MinPoint.Y) * 0.01 + 1;
            info.MinX = box.Value.MinPoint.X - mx;
            info.MinY = box.Value.MinPoint.Y - my;
            info.MaxX = box.Value.MaxPoint.X + mx;
            info.MaxY = box.Value.MaxPoint.Y + my;
        }
    }
}

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

        public static List<ObjectId> FindAllLhbTables(Database db)
        {
            var list = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var rx = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Table));
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(rx)) continue;
                    var tb = (Table)tr.GetObject(id, OpenMode.ForRead);
                    if (DrawingHelper.ReadExtString(tr, tb, ExtKey) != null) list.Add(id);
                }
                tr.Commit();
            }
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

            // 1. Đối tượng cần quét: gốc cũ còn tồn tại + block trong khung vùng quét
            var roots = new HashSet<ObjectId>();
            foreach (var h in info.RootHandles)
            {
                var id = DrawingHelper.FromHandle(db, h);
                if (!id.IsNull) roots.Add(id);
            }
            int oldRootsAlive = roots.Count;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in BlockExtractor.ModelSpaceBlockIds(db))
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var p = br.Position;
                    if (p.X >= info.MinX && p.X <= info.MaxX && p.Y >= info.MinY && p.Y <= info.MaxY) roots.Add(id);
                }
                tr.Commit();
            }

            // 2. Quét lại với đúng tuỳ chọn lúc xuất
            var options = new ExtractionOptions
            {
                MaxDepth = info.MaxDepth > 0 ? info.MaxDepth : 2,
                CountParentBlocks = info.CountParentBlocks,
                SplitByVisibility = info.SplitByVisibility,
                SplitByLayer = info.SplitByLayer,
                SplitAttributeKeys = info.SplitAttributeKeys
            };
            var items = BlockExtractor.ExtractFromDatabase(db, roots, options);
            var template = TemplateLibraryManager.Load(info.TemplateSet);
            bool onlyTemplate = info.OnlyTemplate && template.Entries.Count > 0;
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
            int changedCells = 0, newRows = 0, zeroRows = 0;
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var red = Color.FromColorIndex(ColorMethod.ByAci, 1);
            var normal = Color.FromColorIndex(ColorMethod.ByBlock, 0);
            int countCol = info.ColumnKeys.IndexOf("colCount");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tb = (Table)tr.GetObject(tableId, OpenMode.ForWrite);
                ObjectId styleId = tb.Cells[info.FirstDataRow, 0].TextStyleId ?? ObjectId.Null;
                double textH = info.TextHeight > 0 ? info.TextHeight : (tb.Cells[info.FirstDataRow, 0].TextHeight ?? 250);
                double rowH = info.RowHeight > 0 ? info.RowHeight : tb.Rows[info.FirstDataRow].Height;

                for (int i = 0; i < info.Rows.Count; i++)
                {
                    int r = info.FirstDataRow + i;
                    if (r >= tb.Rows.Count) break;
                    var keys = info.Rows[i].Keys ?? new List<string>();
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
                        info.Rows.Add(new TableRowKeys { Keys = it.Instances.Select(x => x.GroupKey).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList() });
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
                DrawingHelper.WriteExtString(tr, tb, ExtKey, JsonHelper.Serialize(info));
                tb.GenerateLayout();
                tr.Commit();
            }

            string summary = $"Bảng Handle {tableId.Handle}: quét {roots.Count} đối tượng ({oldRootsAlive} gốc cũ còn lại), " +
                             $"{changedCells} ô thay đổi (tô đỏ), {newRows} loại block mới thêm dòng" +
                             (zeroRows > 0 ? $", {zeroRows} dòng không còn block (SL = 0)" : "");
            Logger.Log("[TableUpdater] " + summary);
            return summary;
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

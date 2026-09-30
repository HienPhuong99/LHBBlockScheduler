using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Lệnh LHBKHOPCOT (v9.6, user hỏi 30/09/2026 "bảng co giãn, tự fix như Excel"): khớp lại độ rộng cột của AutoCAD Table
    /// theo chữ đang có trong cột - như double-click mép cột trong Excel. Dùng sau khi sửa chữ trong bảng, sau LHBCAPNHAT
    /// thêm dòng tên dài, hoặc sau khi kéo cột lệch. Cùng công thức độ rộng với lúc xuất bảng (TableExporter.CalculateColumnWidths):
    /// chữ dài nhất + lề, kẹp [4, 60] x chiều cao chữ. Cột ký hiệu (ô chứa block) và ô gộp nhiều cột (tiêu đề, "TỔNG CỘNG")
    /// giữ nguyên. Dòng cao lên vì chữ xuống dòng tự thấp lại khi cột đủ rộng (AutoCAD tự tính).
    /// Kéo tay vẫn như cũ: chọn bảng, kéo grip trên đầu cột (chữ tự xuống dòng, ký hiệu AutoFit co theo ô).
    /// </summary>
    public static class TableAutoFit
    {
        public static void RunCommand(Document doc)
        {
            var ed = doc.Editor;
            var opts = new PromptSelectionOptions { MessageForAdding = "\nChọn bảng cần khớp độ rộng cột theo chữ <Enter = mọi bảng LHB trong bản vẽ>: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "ACAD_TABLE") });
            var res = ed.GetSelection(opts, filter);
            List<ObjectId> tables;
            if (res.Status == PromptStatus.OK) tables = res.Value.GetObjectIds().ToList();
            else if (res.Status == PromptStatus.Error || res.Status == PromptStatus.None) tables = TableUpdater.FindAllLhbTables(doc.Database);
            else return;

            if (tables.Count == 0)
            {
                ed.WriteMessage("\n[LHB] Không có bảng nào (Enter chỉ tìm bảng LHB xuất kiểu AutoCAD Table từ v9). Chọn bảng trực tiếp để khớp bảng khác.\n");
                return;
            }
            int changed = 0, failed = 0;
            foreach (var id in tables)
            {
                try
                {
                    string msg = FitColumns(doc, id, out int cols);
                    if (cols > 0) changed++;
                    ed.WriteMessage("\n[LHB] " + msg);
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Error(ex, $"[TableAutoFit] Bảng Handle {id.Handle}");
                    ed.WriteMessage($"\n[LHB] Bảng Handle {id.Handle}: lỗi {ex.Message}");
                }
            }
            ed.WriteMessage($"\n[LHB] Khớp cột {tables.Count} bảng: {changed} bảng đổi độ rộng" + (failed > 0 ? $", {failed} bảng lỗi (xem {Logger.GetLogFilePath()})" : "") + ".\n");
        }

        /// <summary>Khớp độ rộng các cột chữ của 1 bảng. Trả câu tóm tắt; changedCols = số cột đổi độ rộng.</summary>
        public static string FitColumns(Document doc, ObjectId tableId, out int changedCols)
        {
            changedCols = 0;
            var db = doc.Database;
            var log = new List<string>();
            int skippedSymbol = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tb = (Table)tr.GetObject(tableId, OpenMode.ForWrite);
                int rows = tb.Rows.Count, cols = tb.Columns.Count;
                TableExporterAcad.SuppressRegen(tb, true);
                try
                {
                    for (int c = 0; c < cols; c++)
                    {
                        double maxW = 0, textH = 0;
                        bool hasBlock = false;
                        string header = null;
                        for (int r = 0; r < rows; r++)
                        {
                            var cell = tb.Cells[r, c];
                            // Ô gộp (tiêu đề bảng, nhãn "TỔNG CỘNG" trải nhiều cột) không quyết định độ rộng 1 cột
                            if (IsMerged(cell)) continue;
                            if (HasBlock(cell)) { hasBlock = true; continue; }
                            string text = CellText(cell);
                            if (text.Length == 0) continue;
                            double h = cell.TextHeight ?? 0;
                            if (h <= 0) continue;
                            header ??= text;
                            textH = Math.Max(textH, h);
                            foreach (var line in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                                maxW = Math.Max(maxW, TableExporter.EstimateTextWidth(line.Trim(), h) + 3.0 * h);
                        }
                        // Cột ký hiệu (block trong ô, AutoFit theo ô) và cột không có chữ: giữ độ rộng
                        if (hasBlock) { skippedSymbol++; continue; }
                        if (textH <= 0) continue;

                        double width = Math.Max(4.0 * textH, Math.Min(maxW, 60.0 * textH));
                        double old = tb.Columns[c].Width;
                        if (Math.Abs(width - old) <= 0.01 * textH) continue;
                        tb.Columns[c].Width = width;
                        changedCols++;
                        log.Add($"cột {c + 1} '{Short(header)}' {old:0.#} -> {width:0.#}");
                    }
                }
                finally
                {
                    TableExporterAcad.SuppressRegen(tb, false);
                }
                tb.GenerateLayout();
                tr.Commit();
            }

            string summary = changedCols > 0
                ? $"Bảng Handle {tableId.Handle}: khớp {changedCols} cột theo chữ" + (skippedSymbol > 0 ? $", giữ {skippedSymbol} cột ký hiệu" : "")
                : $"Bảng Handle {tableId.Handle}: các cột đã vừa chữ, không đổi";
            Logger.Log($"[TableAutoFit] {summary}" + (log.Count > 0 ? ": " + string.Join("; ", log) : ""));
            return summary;
        }

        private static bool IsMerged(Cell cell)
        {
            try { return cell.IsMerged == true; }
            catch { return false; }
        }

        private static bool HasBlock(Cell cell)
        {
            try
            {
                foreach (CellContent cc in cell.Contents)
                    if (cc.ContentTypes == CellContentTypes.Block) return true;
            }
            catch
            {
                // Ô không đọc được nội dung: coi như ô chữ
            }
            return false;
        }

        /// <summary>Chữ trong ô bỏ mã định dạng MText (user sửa ô có thể thêm \P xuống dòng, font...).</summary>
        private static string CellText(Cell cell)
        {
            string text;
            try { text = cell.GetTextString(FormatOption.IgnoreMtextFormat); }
            catch { text = null; }
            if (string.IsNullOrEmpty(text))
            {
                try { text = VietnameseHelper.CleanMTextString(cell.TextString ?? ""); }
                catch { text = ""; }
            }
            return (text ?? "").Replace("\\P", "\n").Trim();
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length <= 20 ? s : s.Substring(0, 20) + "...";
    }
}

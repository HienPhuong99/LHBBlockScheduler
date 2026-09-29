using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Xuất bảng thống kê ra Excel (Premium P9) theo đúng các cột đang hiện trên form (kể cả cột khu vực, thuộc tính),
    /// cột Ký hiệu là ảnh của thiết bị, tiêu đề / dòng phụ / dòng tổng theo mẫu bảng.
    /// </summary>
    public static class ExcelExporter
    {
        public class Col
        {
            public string Key;
            public string Header;
            public double WidthPx;
        }

        /// <summary>Sheet thống kê thiết bị. Trả số dòng đã ghi.</summary>
        public static int AddItemsSheet(XlsxWriter w, string sheetName, List<Col> cols, List<BlockItem> items)
        {
            var tpl = TableTemplate.Current;
            var sh = w.AddSheet(sheetName);
            var all = new List<Col> { new Col { Key = "STT", Header = "STT", WidthPx = 45 } };
            all.AddRange(cols.Where(c => c.Key != "STT"));
            foreach (var c in all) sh.ColWidthsPx.Add(c.Key == "colThumbnail" ? 80 : Math.Max(50, Math.Min(c.WidthPx > 0 ? c.WidthPx : 100, 400)));
            int n = all.Count;
            var numeric = all.Select(c => TableExporterAcad.IsSummableColumn(c.Key)).ToArray();

            if (!tpl.HideTitle)
            {
                int r = sh.AddRow(Pad(new object[] { tpl.EffectiveTitle }, n), XlsxWriter.XlStyle.Title, 30);
                sh.Merge(r, 0, r, n - 1);
            }
            if (!string.IsNullOrWhiteSpace(tpl.Subtitle))
            {
                int r = sh.AddRow(Pad(new object[] { tpl.Subtitle.Trim() }, n), XlsxWriter.XlStyle.Subtitle, 22);
                sh.Merge(r, 0, r, n - 1);
            }
            sh.AddRow(all.Select(c => (object)(tpl.UppercaseHeader ? (c.Header ?? "").ToUpper() : c.Header)).ToArray(),
                      Enumerable.Repeat(XlsxWriter.XlStyle.Header, n).ToArray(), 30);

            var sorted = items.OrderBy(i => i.Order).ToList();
            var sums = new double[n];
            for (int i = 0; i < sorted.Count; i++)
            {
                var it = sorted[i];
                var values = new object[n];
                var styles = new XlsxWriter.XlStyle[n];
                for (int c = 0; c < n; c++)
                {
                    string key = all[c].Key;
                    styles[c] = XlsxWriter.XlStyle.Cell;
                    if (key == "STT") values[c] = i + 1;
                    else if (key == "colThumbnail") values[c] = "";
                    else
                    {
                        string text = TableExporter.GetItemTextForColumn(it, key);
                        if (numeric[c] && double.TryParse(text, out double v)) { values[c] = v; sums[c] += v; styles[c] = XlsxWriter.XlStyle.Number; }
                        else
                        {
                            values[c] = text;
                            if (key == "colDisplayName" || key == "colNote" || key == "colBlockName") styles[c] = XlsxWriter.XlStyle.CellLeft;
                        }
                    }
                }
                int row = sh.AddRow(values, styles, 48);
                int symCol = all.FindIndex(c => c.Key == "colThumbnail");
                if (symCol >= 0)
                {
                    string path = !string.IsNullOrEmpty(it.CustomImagePath) && File.Exists(it.CustomImagePath) ? it.CustomImagePath : it.ThumbnailPath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try { sh.AddImage(row, symCol, PngBytes(path), 80, 48); }
                        catch (Exception ex) { Logger.Warn($"[ExcelExporter] Ảnh '{path}': {ex.Message}"); }
                    }
                }
            }

            if (tpl.AddTotalRow)
            {
                var values = new object[n];
                var styles = Enumerable.Repeat(XlsxWriter.XlStyle.TotalNumber, n).ToArray();
                int labelCol = all.FindIndex(c => c.Key == "colDisplayName");
                values[labelCol >= 0 ? labelCol : 0] = tpl.EffectiveTotalLabel;
                for (int c = 0; c < n; c++) if (numeric[c]) values[c] = sums[c];
                sh.AddRow(values, styles, 26);
            }
            Logger.Log($"[ExcelExporter] Sheet '{sh.Name}': {sorted.Count} dòng, {n} cột [{string.Join(", ", all.Select(c => c.Key))}]");
            return sorted.Count;
        }

        /// <summary>Sheet từ lưới chữ (chiều dài ống / dây, nhiều bản vẽ, danh sách lỗi). numericCols = cột ghi số.</summary>
        public static void AddGridSheet(XlsxWriter w, string sheetName, string title, List<string> headers, List<object[]> rows,
                                        List<double> widthsPx = null, bool totalRow = false, HashSet<int> sumCols = null,
                                        List<byte[]> images = null, int imageCol = -1)
        {
            var tpl = TableTemplate.Current;
            var sh = w.AddSheet(sheetName);
            int n = headers.Count;
            for (int c = 0; c < n; c++) sh.ColWidthsPx.Add(widthsPx != null && c < widthsPx.Count ? widthsPx[c] : 120);
            if (!string.IsNullOrEmpty(title))
            {
                int r = sh.AddRow(Pad(new object[] { title }, n), XlsxWriter.XlStyle.Title, 30);
                sh.Merge(r, 0, r, n - 1);
            }
            if (!string.IsNullOrWhiteSpace(tpl.Subtitle))
            {
                int r = sh.AddRow(Pad(new object[] { tpl.Subtitle.Trim() }, n), XlsxWriter.XlStyle.Subtitle, 22);
                sh.Merge(r, 0, r, n - 1);
            }
            sh.AddRow(headers.Cast<object>().ToArray(), Enumerable.Repeat(XlsxWriter.XlStyle.Header, n).ToArray(), 30);
            var sums = new double[n];
            for (int i = 0; i < rows.Count; i++)
            {
                var v = Pad(rows[i], n);
                var st = v.Select(x => x is double || x is int ? XlsxWriter.XlStyle.Number : XlsxWriter.XlStyle.Cell).ToArray();
                for (int c = 0; c < n; c++) if (v[c] is double d) sums[c] += d; else if (v[c] is int k) sums[c] += k;
                int row = sh.AddRow(v, st, images != null ? 48 : 0);
                if (images != null && imageCol >= 0 && i < images.Count && images[i] != null) sh.AddImage(row, imageCol, images[i], (int)sh.ColWidthsPx[imageCol], 48);
            }
            if (totalRow && sumCols != null)
            {
                var v = new object[n];
                v[0] = tpl.EffectiveTotalLabel;
                foreach (int c in sumCols) v[c] = Math.Round(sums[c], 3);
                sh.AddRow(v, Enumerable.Repeat(XlsxWriter.XlStyle.TotalNumber, n).ToArray(), 26);
            }
        }

        private static object[] Pad(object[] v, int n)
        {
            if (v.Length >= n) return v;
            var r = new object[n];
            Array.Copy(v, r, v.Length);
            return r;
        }

        /// <summary>Ảnh bất kỳ -> PNG (ảnh tuỳ chỉnh có thể là JPG).</summary>
        public static byte[] PngBytes(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length > 8 && raw[0] == 0x89 && raw[1] == 0x50) return raw;
            using (var ms = new MemoryStream(raw))
            using (var img = System.Drawing.Image.FromStream(ms))
            using (var outMs = new MemoryStream())
            {
                img.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                return outMs.ToArray();
            }
        }

        /// <summary>Mở file sau khi xuất (Excel / chương trình mặc định).</summary>
        public static void Open(string path)
        {
            try { System.Diagnostics.Process.Start(path); }
            catch (Exception ex) { Logger.Warn($"[ExcelExporter] Không mở được '{path}': {ex.Message}"); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Ghi file Excel .xlsx (Premium P9) bằng cách tự tạo các phần OpenXML trong file zip: không cần cài Excel,
    /// không thêm thư viện ngoài (thư viện ngoài dễ xung đột phiên bản trong acad.exe).
    /// Hỗ trợ: nhiều sheet, độ rộng cột, chiều cao dòng, gộp ô, 9 kiểu ô (viền, header tô màu, tiêu đề, số), ảnh PNG
    /// đặt giữa ô (cột Ký hiệu). Không phụ thuộc AutoCAD -> kiểm tra được ngoài CAD.
    /// </summary>
    public class XlsxWriter
    {
        public enum XlStyle { Normal = 0, Cell = 1, Header = 2, Title = 3, CellLeft = 4, Number = 5, Subtitle = 6, TotalLabel = 7, TotalNumber = 8 }

        internal class Img
        {
            public int Row, Col, BoxW, BoxH;
            public byte[] Png;
        }

        public class Sheet
        {
            public string Name;
            public List<double> ColWidthsPx = new List<double>();
            internal readonly List<(object[] Values, XlStyle[] Styles, double HeightPx)> Rows = new List<(object[], XlStyle[], double)>();
            internal readonly List<string> Merges = new List<string>();
            internal readonly List<Img> Images = new List<Img>();

            /// <summary>Thêm 1 dòng, trả chỉ số dòng (0 = dòng đầu). Giá trị số (int, double...) ghi thành số Excel.</summary>
            public int AddRow(IEnumerable<object> values, XlStyle style, double heightPx = 0)
            {
                var v = values.ToArray();
                return AddRow(v, Enumerable.Repeat(style, v.Length).ToArray(), heightPx);
            }

            public int AddRow(object[] values, XlStyle[] styles, double heightPx = 0)
            {
                Rows.Add((values, styles, heightPx));
                return Rows.Count - 1;
            }

            public void Merge(int row1, int col1, int row2, int col2) =>
                Merges.Add($"{CellRef(row1, col1)}:{CellRef(row2, col2)}");

            /// <summary>Ảnh PNG đặt giữa ô [row, col], thu nhỏ vừa khung boxW x boxH pixel (giữ tỉ lệ).</summary>
            public void AddImage(int row, int col, byte[] png, int boxW, int boxH)
            {
                if (png == null || png.Length < 24) return;
                Images.Add(new Img { Row = row, Col = col, Png = png, BoxW = boxW, BoxH = boxH });
            }
        }

        private readonly List<Sheet> _sheets = new List<Sheet>();

        public Sheet AddSheet(string name)
        {
            string clean = new string((name ?? "Sheet").Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
            if (clean.Length == 0) clean = "Sheet";
            if (clean.Length > 31) clean = clean.Substring(0, 31);
            string unique = clean;
            for (int i = 2; _sheets.Any(s => string.Equals(s.Name, unique, StringComparison.OrdinalIgnoreCase)); i++)
                unique = (clean.Length > 27 ? clean.Substring(0, 27) : clean) + " " + i;
            var sh = new Sheet { Name = unique };
            _sheets.Add(sh);
            return sh;
        }

        public static string ColName(int col)
        {
            string s = "";
            col++;
            while (col > 0)
            {
                int m = (col - 1) % 26;
                s = (char)('A' + m) + s;
                col = (col - 1) / 26;
            }
            return s;
        }

        public static string CellRef(int row, int col) => ColName(col) + (row + 1).ToString(CultureInfo.InvariantCulture);

        public void Save(string path)
        {
            if (_sheets.Count == 0) AddSheet("Sheet1");
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                int imageNo = 0, drawingNo = 0;
                var ct = new StringBuilder();
                ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                ct.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
                ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                ct.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");

                var wb = new StringBuilder();
                wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                wb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                var wbRels = new StringBuilder();
                wbRels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                wbRels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

                for (int s = 0; s < _sheets.Count; s++)
                {
                    var sh = _sheets[s];
                    int n = s + 1;
                    ct.Append($"<Override PartName=\"/xl/worksheets/sheet{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    wb.Append($"<sheet name=\"{Esc(sh.Name)}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>");
                    wbRels.Append($"<Relationship Id=\"rId{n}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{n}.xml\"/>");

                    string drawingRel = null;
                    if (sh.Images.Count > 0)
                    {
                        drawingNo++;
                        drawingRel = "rId1";
                        ct.Append($"<Override PartName=\"/xl/drawings/drawing{drawingNo}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                        Write(zip, $"xl/worksheets/_rels/sheet{n}.xml.rels",
                            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                            $"<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing{drawingNo}.xml\"/></Relationships>");
                        WriteDrawing(zip, sh, drawingNo, ref imageNo);
                    }
                    Write(zip, $"xl/worksheets/sheet{n}.xml", SheetXml(sh, drawingRel));
                }

                wb.Append("</sheets></workbook>");
                int stylesRel = _sheets.Count + 1;
                wbRels.Append($"<Relationship Id=\"rId{stylesRel}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                wbRels.Append("</Relationships>");
                ct.Append("</Types>");

                Write(zip, "[Content_Types].xml", ct.ToString());
                Write(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
                Write(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>Bảng thống kê LHB</dc:title><dc:creator>LHB Block Scheduler Premium</dc:creator>" +
                    $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created></cp:coreProperties>");
                Write(zip, "xl/workbook.xml", wb.ToString());
                Write(zip, "xl/_rels/workbook.xml.rels", wbRels.ToString());
                Write(zip, "xl/styles.xml", StylesXml);
            }
        }

        private static string SheetXml(Sheet sh, string drawingRel)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            if (sh.ColWidthsPx.Count > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < sh.ColWidthsPx.Count; c++)
                {
                    double w = Math.Max(1, (sh.ColWidthsPx[c] - 5) / 7.0);
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < sh.Rows.Count; r++)
            {
                var (values, styles, h) = sh.Rows[r];
                sb.Append($"<row r=\"{r + 1}\"");
                if (h > 0) sb.Append($" ht=\"{(h * 0.75).ToString("0.##", CultureInfo.InvariantCulture)}\" customHeight=\"1\"");
                sb.Append(">");
                for (int c = 0; c < values.Length; c++)
                {
                    int st = (int)(c < styles.Length ? styles[c] : XlStyle.Cell);
                    string cref = CellRef(r, c);
                    object v = values[c];
                    if (v == null || (v is string es && es.Length == 0))
                        sb.Append($"<c r=\"{cref}\" s=\"{st}\"/>");
                    else if (v is int || v is long || v is double || v is float || v is decimal || v is short)
                        sb.Append($"<c r=\"{cref}\" s=\"{st}\"><v>{Convert.ToDouble(v).ToString("0.###", CultureInfo.InvariantCulture)}</v></c>");
                    else
                        sb.Append($"<c r=\"{cref}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(v.ToString())}</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (sh.Merges.Count > 0)
            {
                sb.Append($"<mergeCells count=\"{sh.Merges.Count}\">");
                foreach (var m in sh.Merges) sb.Append($"<mergeCell ref=\"{m}\"/>");
                sb.Append("</mergeCells>");
            }
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            if (drawingRel != null) sb.Append($"<drawing r:id=\"{drawingRel}\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void WriteDrawing(ZipArchive zip, Sheet sh, int drawingNo, ref int imageNo)
        {
            const long Emu = 9525; // 1 pixel ở 96 dpi
            var d = new StringBuilder();
            var rels = new StringBuilder();
            d.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            d.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

            for (int i = 0; i < sh.Images.Count; i++)
            {
                var img = sh.Images[i];
                imageNo++;
                string rid = "rId" + (i + 1);
                WriteBytes(zip, $"xl/media/image{imageNo}.png", img.Png);
                rels.Append($"<Relationship Id=\"{rid}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image{imageNo}.png\"/>");

                // Kích thước PNG trong header (IHDR): rộng byte 16-19, cao byte 20-23 (big-endian)
                int pw = (img.Png[16] << 24) | (img.Png[17] << 16) | (img.Png[18] << 8) | img.Png[19];
                int ph = (img.Png[20] << 24) | (img.Png[21] << 16) | (img.Png[22] << 8) | img.Png[23];
                if (pw <= 0 || ph <= 0) { pw = img.BoxW; ph = img.BoxH; }
                double k = Math.Min((img.BoxW - 6.0) / pw, (img.BoxH - 6.0) / ph);
                int w = Math.Max(1, (int)(pw * k)), h = Math.Max(1, (int)(ph * k));
                long offX = (img.BoxW - w) / 2 * Emu, offY = (img.BoxH - h) / 2 * Emu;

                d.Append("<xdr:oneCellAnchor>");
                d.Append($"<xdr:from><xdr:col>{img.Col}</xdr:col><xdr:colOff>{offX}</xdr:colOff><xdr:row>{img.Row}</xdr:row><xdr:rowOff>{offY}</xdr:rowOff></xdr:from>");
                d.Append($"<xdr:ext cx=\"{w * Emu}\" cy=\"{h * Emu}\"/>");
                d.Append("<xdr:pic><xdr:nvPicPr>");
                d.Append($"<xdr:cNvPr id=\"{i + 2}\" name=\"Ky hieu {i + 1}\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>");
                d.Append($"<xdr:blipFill><a:blip r:embed=\"{rid}\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>");
                d.Append($"<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{w * Emu}\" cy=\"{h * Emu}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>");
                d.Append("</xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
            }
            d.Append("</xdr:wsDr>");
            rels.Append("</Relationships>");
            Write(zip, $"xl/drawings/drawing{drawingNo}.xml", d.ToString());
            Write(zip, $"xl/drawings/_rels/drawing{drawingNo}.xml.rels", rels.ToString());
        }

        // Font 0 thường, 1 đậm, 2 đậm 14 (tiêu đề), 3 nghiêng. Fill 2 = xanh nhạt (header), 3 = vàng nhạt (dòng tổng).
        // cellXfs theo thứ tự enum XlStyle.
        private const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"4\">" +
            "<font><sz val=\"11\"/><name val=\"Arial\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Arial\"/></font>" +
            "<font><b/><sz val=\"14\"/><name val=\"Arial\"/></font>" +
            "<font><i/><sz val=\"11\"/><name val=\"Arial\"/></font>" +
            "</fonts>" +
            "<fills count=\"4\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E1F2\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"thin\"><color auto=\"1\"/></top><bottom style=\"thin\"><color auto=\"1\"/></bottom><diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"9\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Bỏ ký tự điều khiển không hợp lệ trong XML (vd mã định dạng MText còn sót)
            var clean = new string(s.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20).ToArray());
            return SecurityElement.Escape(clean);
        }

        private static void Write(ZipArchive zip, string name, string content) =>
            WriteBytes(zip, name, new UTF8Encoding(false).GetBytes(content));

        private static void WriteBytes(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open()) s.Write(data, 0, data.Length);
        }
    }
}

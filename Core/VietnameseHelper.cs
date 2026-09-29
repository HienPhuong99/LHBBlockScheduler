using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LHBBlockScheduler.Core
{
    public static class VietnameseHelper
    {
        /// <summary>
        /// Bỏ dấu tiếng Việt, dùng cho search realtime không cần gõ đúng dấu.
        /// VD: "Cong tac" vẫn khớp với "Công tắc".
        /// </summary>
        public static string RemoveDiacritics(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Chữ Đ/đ không được NFD decompose đúng cách trong .NET nên xử lý riêng
            string s = input.Replace('Đ', 'D').Replace('đ', 'd');

            string normalized = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Làm sạch chuỗi lấy từ Table cell / MText: giải mã "\U+1ECD" -> "ọ", bỏ mã định dạng MText
        /// (\P, {\fArial|b0;...}, \H2.5;, \L, \O...). Cell.TextString trả nguyên chuỗi thô nên header
        /// "Minh họa" bị hiện thành "Minh h\U+1ECDa" (xác nhận qua LHBDIAG 28/09/2026).
        /// </summary>
        public static string CleanMTextString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            const string bsHolder = "\u0001", lbHolder = "\u0002", rbHolder = "\u0003";

            // Giữ lại ký tự escape thật: \\ \{ \}
            string s = raw.Replace(@"\\", bsHolder).Replace(@"\{", lbHolder).Replace(@"\}", rbHolder);

            // \U+XXXX -> ký tự Unicode
            s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})",
                m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());

            // Xuống dòng / khoảng trắng không ngắt
            s = Regex.Replace(s, @"\\[Pp]", " ");
            s = s.Replace(@"\~", " ");

            // Mã định dạng có tham số kết thúc bằng ';' (font, chiều cao, màu, căn lề, độ rộng...)
            s = Regex.Replace(s, @"\\[ACFHQTWfhqtwacp][^;\\]*;", "");
            // Mã bật/tắt gạch chân, gạch trên, gạch ngang
            s = Regex.Replace(s, @"\\[LlOoKk]", "");
            // Phân số \S1/2;
            s = Regex.Replace(s, @"\\S([^;]*);", m => m.Groups[1].Value.Replace('^', '/').Replace('#', '/'));

            s = s.Replace("{", "").Replace("}", "");
            s = s.Replace(bsHolder, @"\").Replace(lbHolder, "{").Replace(rbHolder, "}");
            return s.Trim();
        }

        /// <summary>So khớp không phân biệt hoa/thường và không phân biệt dấu.</summary>
        public static bool ContainsIgnoreCaseAndDiacritics(string source, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return true;
            if (string.IsNullOrEmpty(source)) return false;

            string a = RemoveDiacritics(source).ToLowerInvariant();
            string b = RemoveDiacritics(keyword).ToLowerInvariant();
            return a.Contains(b);
        }
    }
}

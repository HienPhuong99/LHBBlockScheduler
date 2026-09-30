using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Hàm thuần (không gọi AutoCAD) cho tham số block động (v9.6): định dạng kích thước, khớp / chuẩn hoá chủng loại lưu
    /// từ bản cũ. Dùng chung add-in + tests/LHBCoreTests (chạy được ngoài AutoCAD).
    /// </summary>
    public static class DynamicParamText
    {
        /// <summary>
        /// Số kích thước để hiện và tách dòng: ~4 chữ số có nghĩa, số lớn làm tròn tới đơn vị -> 12320.3286822983 = "12320",
        /// 1200 = "1200", 12.3204 = "12.32", 0.6 = "0.6". Dấu chấm thập phân cố định (khoá dòng lưu trong bảng không phụ thuộc
        /// cài đặt vùng của máy).
        /// </summary>
        public static string FormatSize(double v)
        {
            double a = Math.Abs(v);
            string fmt = a >= 1000 ? "0" : a >= 100 ? "0.#" : a >= 10 ? "0.##" : "0.###";
            string s = v.ToString(fmt, CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>Nhiều tham số độ dài: "1200 x 600" (theo thứ tự tham số trong block).</summary>
        public static string JoinSizes(IEnumerable<double> values) => string.Join(" x ", (values ?? Enumerable.Empty<double>()).Select(FormatSize));

        /// <summary>Số đầu tiên trong chuỗi kích thước ("1200 x 600" -> 1200) để sắp xếp; không có số -> double.MaxValue.</summary>
        public static double SizeSortKey(string size)
        {
            if (string.IsNullOrWhiteSpace(size)) return double.MaxValue;
            var m = Regex.Match(size, @"-?\d+(\.\d+)?");
            return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.MaxValue;
        }

        /// <summary>
        /// Kích thước của 1 dòng từ kích thước từng block: giống nhau -> kích thước đó; khác nhau (không tách theo kích thước,
        /// gộp dòng) -> các kích thước tăng dần nối "; " (tối đa maxShown, thừa thêm "...").
        /// </summary>
        public static string GroupSize(IEnumerable<string> sizes, int maxShown = 3)
        {
            var list = (sizes ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(SizeSortKey).ToList();
            if (list.Count <= 1) return list.FirstOrDefault() ?? "";
            return string.Join("; ", list.Take(maxShown)) + (list.Count > maxShown ? "; ..." : "");
        }

        /// <summary>"Position1 X", "Vị trí Y"... = toạ độ của tham số Point (AutoCAD đặt tên: tên tham số + " X" / " Y").</summary>
        public static bool IsPointCoordinate(string name) =>
            name != null && name.Length > 2 && name[name.Length - 2] == ' ' && "XYZxyz".IndexOf(name[name.Length - 1]) >= 0;

        /// <summary>
        /// Bỏ các phần "Tên=Giá trị" của tham số SỐ (tên nằm trong numericNames) khỏi chủng loại lưu kiểu cũ (v9 - v9.5 ghép
        /// mọi tham số của block động không có Visibility, nối " - "). "Distance1=47116.93" -> "";
        /// "Lookup1=Loại A - Distance1=500" -> "Lookup1=Loại A". numericNames rỗng / null -> giữ nguyên.
        /// </summary>
        public static string StripNumericParts(string variant, ICollection<string> numericNames)
        {
            variant ??= "";
            if (numericNames == null || numericNames.Count == 0 || variant.IndexOf('=') < 0) return variant;
            var names = numericNames as HashSet<string> ?? new HashSet<string>(numericNames, StringComparer.OrdinalIgnoreCase);
            var kept = variant.Split(new[] { " - " }, StringSplitOptions.None).Where(part =>
            {
                int eq = part.IndexOf('=');
                return eq <= 0 || !names.Contains(part.Substring(0, eq).Trim());
            }).ToList();
            return string.Join(" - ", kept);
        }

        /// <summary>
        /// Chọn block mẫu khớp trong các block mẫu CÙNG TÊN (chủng loại từng mẫu = entryVariants): ưu tiên mẫu cùng chủng loại,
        /// sau đó mẫu không ghi chủng loại (mọi chủng loại). Chủng loại mẫu lưu kiểu cũ được bỏ phần tham số số trước khi so
        /// (numericNames = tên tham số số của block đang xét, chỉ khi block không có Visibility). Trả chỉ số, -1 = không khớp.
        /// </summary>
        public static int MatchVariant(IList<string> entryVariants, string variant, ICollection<string> numericNames)
        {
            if (entryVariants == null) return -1;
            var normalized = entryVariants.Select(v => StripNumericParts(v, numericNames)).ToList();
            for (int i = 0; i < normalized.Count; i++)
                if (normalized[i].Length > 0 && string.Equals(normalized[i], variant ?? "", StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < normalized.Count; i++)
                if (normalized[i].Length == 0) return i;
            return -1;
        }
    }
}

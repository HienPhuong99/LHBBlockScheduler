using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LHBBlockScheduler.Core;

namespace LHBCoreTests
{
    /// <summary>
    /// Kiểm thử v9.6 (không cần AutoCAD): chủng loại không chứa tham số số của block động, kích thước "Tách theo kích thước",
    /// block mẫu lưu từ bản cũ (chủng loại "Distance1=47116.93") vẫn khớp. v9.7: thư mục gốc add-in khi DLL nằm thư mục con
    /// net8 / net10 (AutoCAD 2025+). Thoát 0 = đạt hết.
    /// </summary>
    internal static class Program
    {
        private static int _pass, _fail;

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            // Máy dùng dấu phẩy thập phân (vi-VN) không được làm đổi kích thước / khoá dòng
            Thread.CurrentThread.CurrentCulture = new CultureInfo("vi-VN");

            Section("Kích thước (FormatSize / JoinSizes)");
            Eq(DynamicParamText.FormatSize(12320.3286822983), "12320", "đầu báo tia chiếu 12320.3286822983 (ảnh test 30/09)");
            Eq(DynamicParamText.FormatSize(47116.9332343), "47117", "47116.93 làm tròn tới đơn vị");
            Eq(DynamicParamText.FormatSize(1200), "1200", "số nguyên giữ nguyên");
            Eq(DynamicParamText.FormatSize(600.04), "600", "600.04 -> 600");
            Eq(DynamicParamText.FormatSize(600.5), "600.5", "600.5 giữ 1 số lẻ");
            Eq(DynamicParamText.FormatSize(12.3204), "12.32", "bản vẽ đơn vị m: 12.3204 -> 12.32");
            Eq(DynamicParamText.FormatSize(0.6), "0.6", "0.6");
            Eq(DynamicParamText.FormatSize(-0.0001), "0", "-0.0001 -> 0 (không ra -0)");
            Eq(DynamicParamText.FormatSize(12320.33), DynamicParamText.FormatSize(12320.2), "12320.33 và 12320.2 cùng 1 dòng");
            Eq(DynamicParamText.JoinSizes(new[] { 1200.0, 600.0 }), "1200 x 600", "2 tham số: rộng x cao");
            Eq(DynamicParamText.JoinSizes(new double[0]), "", "không có tham số độ dài -> rỗng");

            Section("Sắp xếp / gộp kích thước");
            Check(DynamicParamText.SizeSortKey("1200 x 600") == 1200, "khoá sắp xếp '1200 x 600' = 1200");
            Check(DynamicParamText.SizeSortKey("12.5") == 12.5, "khoá sắp xếp '12.5' = 12.5 (dấu chấm, máy vi-VN)");
            Check(DynamicParamText.SizeSortKey("") == double.MaxValue, "không kích thước xếp cuối");
            Eq(DynamicParamText.GroupSize(new[] { "12320", "12320" }), "12320", "các block cùng kích thước");
            Eq(DynamicParamText.GroupSize(new[] { "15000", "8000", "12320", "20000" }), "8000; 12320; 15000; ...", "khác kích thước: tăng dần, tối đa 3");
            Eq(DynamicParamText.GroupSize(new[] { "", null, "500" }), "500", "bỏ block không có kích thước");

            Section("Toạ độ Point không phải kích thước");
            Check(DynamicParamText.IsPointCoordinate("Position1 X"), "'Position1 X' là toạ độ");
            Check(DynamicParamText.IsPointCoordinate("Vị trí Y"), "'Vị trí Y' là toạ độ");
            Check(!DynamicParamText.IsPointCoordinate("Distance1"), "'Distance1' không phải toạ độ");
            Check(!DynamicParamText.IsPointCoordinate("X"), "'X' (tên 1 ký tự) không phải toạ độ");
            Check(!DynamicParamText.IsPointCoordinate("Horizontal"), "'Horizontal' (XY) là kích thước");

            Section("Chủng loại lưu từ bản cũ (StripNumericParts)");
            var dist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Distance1" };
            Eq(DynamicParamText.StripNumericParts("Distance1=47116.9332343", dist), "", "'Distance1=47116.93' -> trống (ảnh 1)");
            Eq(DynamicParamText.StripNumericParts("distance1=5", dist), "", "không phân biệt hoa thường");
            Eq(DynamicParamText.StripNumericParts("Lookup1=Loại A - Distance1=500", dist), "Lookup1=Loại A", "giữ tham số chữ (Lookup)");
            var flipDist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flip state1", "Distance1", "Angle1" };
            Eq(DynamicParamText.StripNumericParts("Flip state1=0 - Distance1=12 - Angle1=1.57", flipDist), "", "lật + độ dài + góc -> trống");
            Eq(DynamicParamText.StripNumericParts("K=80", null), "K=80", "không có tên tham số số -> giữ (trạng thái Visibility 'K=80')");
            Eq(DynamicParamText.StripNumericParts("K=80", dist), "K=80", "'K' không phải tham số số của block -> giữ");
            Eq(DynamicParamText.StripNumericParts("TỦ 2 CUỘN VÒI", dist), "TỦ 2 CUỘN VÒI", "chủng loại Visibility thường giữ nguyên");
            Eq(DynamicParamText.StripNumericParts(null, dist), "", "null -> rỗng");

            Section("Khớp block mẫu (MatchVariant)");
            Check(DynamicParamText.MatchVariant(new[] { "Distance1=47116.9332343" }, "", dist) == 0,
                  "mẫu cũ 'Distance1=47116.93' khớp đầu báo tia chiếu mọi độ dài (chủng loại mới trống)");
            Check(DynamicParamText.MatchVariant(new[] { "Distance1=47116.9332343" }, "", null) == -1,
                  "không biết tên tham số số (block có Visibility) -> không đoán");
            Check(DynamicParamText.MatchVariant(new[] { "2 HƯỚNG", "" }, "2 HƯỚNG", null) == 0, "ưu tiên mẫu cùng chủng loại");
            Check(DynamicParamText.MatchVariant(new[] { "2 HƯỚNG", "" }, "1 HƯỚNG", null) == 1, "khác chủng loại -> mẫu không ghi chủng loại");
            Check(DynamicParamText.MatchVariant(new[] { "K=80", "K=115" }, "K=115", null) == 1, "trạng thái Visibility dạng 'K=115' khớp đúng mẫu");
            Check(DynamicParamText.MatchVariant(new[] { "K=80" }, "K=115", null) == -1, "'K=80' không khớp 'K=115'");
            Check(DynamicParamText.MatchVariant(new[] { "bột abc 8kg" }, "BỘT ABC 8KG", null) == 0, "không phân biệt hoa thường");
            Check(DynamicParamText.MatchVariant(new[] { "Lookup1=Loại A - Distance1=5" }, "Lookup1=Loại A", dist) == 0, "mẫu cũ Lookup + độ dài khớp chủng loại mới");
            Check(DynamicParamText.MatchVariant(new string[0], "", dist) == -1, "không có mẫu cùng tên -> -1");

            Section("Thư mục gốc add-in theo bản DLL (v9.7, AddinPaths.ResolveRoot)");
            string root = Path.Combine(Path.GetTempPath(), "LHBBlockScheduler");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(root, "LHB.lsp"), Path.Combine(root, "LHBBlockScheduler.dll"),
            };
            Func<string, bool> exists = files.Contains;
            Eq(AddinPaths.ResolveRoot(root, exists), root, "bản 2021 - 2024 (thư mục gốc) -> chính thư mục đó");
            Eq(AddinPaths.ResolveRoot(Path.Combine(root, "net8"), exists), root, "bản 2025 - 2026 (net8) -> thư mục gốc (ThuVienMau, log, LHBDIAG dùng chung)");
            Eq(AddinPaths.ResolveRoot(Path.Combine(root, "NET10") + Path.DirectorySeparatorChar, exists), root, "net10 (chữ hoa, có dấu phân cách cuối) -> thư mục gốc");
            string build = Path.Combine(Path.GetTempPath(), "repo", "bin", "Release", "net8");
            Eq(AddinPaths.ResolveRoot(build, exists), build, "thư mục build bin/Release/net8 (thư mục cha không phải add-in) -> giữ nguyên");
            Eq(AddinPaths.ResolveRoot(Path.Combine(root, "net9"), exists), Path.Combine(root, "net9"), "thư mục con lạ (net9) -> giữ nguyên");
            Check(AddinPaths.ResolveRoot(null, exists) == null, "không biết thư mục DLL -> null");
            var onlyDll = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(root, "LHBBlockScheduler.dll") };
            Eq(AddinPaths.ResolveRoot(Path.Combine(root, "net8"), onlyDll.Contains), root, "thư mục gốc có DLL nhưng mất LHB.lsp vẫn nhận");

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? $"ĐẠT HẾT: {_pass} kiểm tra" : $"LỖI: {_fail} / {_pass + _fail} kiểm tra");
            return _fail == 0 ? 0 : 1;
        }

        private static void Section(string name) => Console.WriteLine($"\n== {name}");

        private static void Check(bool ok, string what)
        {
            if (ok) { _pass++; Console.WriteLine("  OK   " + what); }
            else { _fail++; Console.WriteLine("  LỖI  " + what); }
        }

        private static void Eq(string actual, string expected, string what) =>
            Check(actual == expected, what + (actual == expected ? "" : $" (ra '{actual}', cần '{expected}')"));
    }
}

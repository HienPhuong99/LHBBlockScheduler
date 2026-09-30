using System;
using System.IO;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// v9.7: thư mục add-in khi có nhiều bản DLL theo dòng AutoCAD. Hàm thuần (không gọi AutoCAD), dùng chung add-in +
    /// tests/LHBCoreTests. Bố cục gói:
    ///   &lt;thư mục gốc&gt;\LHB.lsp, LHBBlockScheduler.dll (AutoCAD 2021 - 2024), ThuVienMau\, log.txt, LHBDIAG_*.txt
    ///   &lt;thư mục gốc&gt;\net8\LHBBlockScheduler.dll (AutoCAD 2025 - 2026), &lt;thư mục gốc&gt;\net10\... (AutoCAD 2027)
    /// </summary>
    public static class AddinPaths
    {
        /// <summary>Thư mục con chứa bản DLL cho AutoCAD 2025+ (LHB.lsp chọn theo ACADVER).</summary>
        public static readonly string[] TargetSubfolders = { "net8", "net10" };

        /// <summary>
        /// Thư mục gốc add-in từ thư mục DLL đang chạy: DLL nằm trong thư mục con net8 / net10 và thư mục cha là thư mục
        /// add-in (có LHB.lsp hoặc bản DLL gốc) -> thư mục cha; còn lại -> chính thư mục DLL (bản 2021 - 2024, thư mục build).
        /// </summary>
        public static string ResolveRoot(string dllDir, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(dllDir)) return dllDir;
            string trimmed = dllDir.TrimEnd('\\', '/');
            if (trimmed.Length == 0) return dllDir;
            string name = Path.GetFileName(trimmed);
            if (!Array.Exists(TargetSubfolders, s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) return dllDir;
            string parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent)) return dllDir;
            bool isAddin = fileExists(Path.Combine(parent, "LHB.lsp")) || fileExists(Path.Combine(parent, "LHBBlockScheduler.dll"));
            return isAddin ? parent : dllDir;
        }
    }
}

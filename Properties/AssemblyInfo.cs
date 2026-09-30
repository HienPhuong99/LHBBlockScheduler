using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;
using LHBBlockScheduler;

[assembly: AssemblyTitle("LHB Block Scheduler")]
[assembly: AssemblyDescription("Thống kê, gộp nhóm và xuất bảng khối lượng Block cho AutoCAD")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("LHBBlockScheduler")]
[assembly: AssemblyCopyright("")]
[assembly: ComVisible(false)]
// v9.4: số phiên bản lấy từ 1 chỗ duy nhất (MyApp.Version) - trước đây viết cứng ở 4 nơi, hay quên sửa 1 chỗ
[assembly: AssemblyVersion(MyApp.AssemblyVersionText)]
[assembly: AssemblyFileVersion(MyApp.AssemblyVersionText)]
[assembly: AssemblyInformationalVersion(MyApp.DisplayVersion)]
#if NET
// v9.7: bản AutoCAD 2025+ (.NET 8 / 10) chỉ chạy trên Windows. GenerateAssemblyInfo=false nên SDK không tự ghi thuộc tính này
// (thiếu thì bộ phân tích CA1416 cảnh báo mọi API Windows: Registry, System.Drawing...)
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif

// Đăng ký toàn bộ command trong Commands.cs với AutoCAD
[assembly: CommandClass(typeof(Commands))]

// Chạy Initialize() khi assembly được NETLOAD, Terminate() khi unload
[assembly: ExtensionApplication(typeof(MyApp))]

namespace LHBBlockScheduler
{
    public class MyApp : IExtensionApplication
    {
        /// <summary>Phiên bản (sửa DUY NHẤT ở đây khi ra bản mới): tiêu đề form, dòng lệnh, AssemblyVersion, LHB.lsp (build.ps1 ghi vào).</summary>
        public const string Version = "9.7";
        public const string AssemblyVersionText = Version + ".0.0";
        /// <summary>"v9.4 Premium" - hiện trên tiêu đề form "Thống kê Block v9.4 Premium" và dòng lệnh lúc nạp.</summary>
        public const string DisplayVersion = "v" + Version + " Premium";

        // v9.7: mỗi dòng AutoCAD 1 bản DLL (csproj đa đích). Bản gốc thư mục add-in = net48, thư mục con net8 / net10.
#if NET10_0_OR_GREATER
        public const string BuildTarget = "AutoCAD 2027 (.NET 10)";
        private const int AcadMajorMin = 26, AcadMajorMax = 26;
#elif NET8_0_OR_GREATER
        public const string BuildTarget = "AutoCAD 2025 - 2026 (.NET 8)";
        private const int AcadMajorMin = 25, AcadMajorMax = 25;
#else
        public const string BuildTarget = "AutoCAD 2021 - 2024 (.NET Framework 4.8)";
        private const int AcadMajorMin = 24, AcadMajorMax = 24;
#endif

        /// <summary>.NET đang chạy trong AutoCAD (vd ".NET Framework 4.8.9290.0", ".NET 8.0.20").</summary>
        public static string RuntimeText
        {
            get
            {
                try { return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; }
                catch { return System.Environment.Version.ToString(); }
            }
        }

        public void Initialize()
        {
            Core.Logger.Log($"=== LHBBlockScheduler {DisplayVersion} [{BuildTarget}] đã nạp, {RuntimeText}, AutoCAD {AcadVersionText} ===");
            CheckAcadVersion();
            RememberInstallDir();
            RegisterAliases();
            try
            {
                UI.RibbonBuilder.Init();
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage($"\n[LHB] {DisplayVersion} - {Core.LicenseManager.StatusText}. Tab Ribbon 'LHB Premium', gõ LHBPALETTE mở bảng công cụ.");
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error(ex, "[MyApp] Khởi tạo Ribbon");
            }
        }

        /// <summary>Đăng ký phím tắt lệnh (bảng LHBLENH) và in ra dòng lệnh để user biết gõ gì.</summary>
        private static void RegisterAliases()
        {
            try
            {
                Core.CommandAliasManager.RegisterAll();
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage($"\n[LHB] {Core.CommandAliasManager.Summary()}");
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error(ex, "[MyApp] Đăng ký phím tắt lệnh thất bại");
            }
        }

        /// <summary>Phiên bản AutoCAD đang chạy (Application.Version, vd "25.0.66.0" = AutoCAD 2025).</summary>
        public static string AcadVersionText
        {
            get
            {
                try { return Autodesk.AutoCAD.ApplicationServices.Application.Version.ToString(); }
                catch (System.Exception ex) { return "? (" + ex.Message + ")"; }
            }
        }

        /// <summary>
        /// v9.7: DLL nạp nhầm dòng AutoCAD (vd NETLOAD tay bản net48 ở thư mục gốc vào AutoCAD 2025) có thể nạp được nhưng
        /// chạy sai từng phần -> báo trên dòng lệnh, chỉ cách nạp đúng (kéo thả LHB.lsp tự chọn thư mục net8 / net10).
        /// AutoCAD mới hơn bản đã build (vd 2028) chỉ ghi log: .NET cho nạp, chờ bản build riêng.
        /// </summary>
        private static void CheckAcadVersion()
        {
            try
            {
                int major = Autodesk.AutoCAD.ApplicationServices.Application.Version.Major;
                if (major >= AcadMajorMin && major <= AcadMajorMax)
                {
                    Core.Logger.Log($"[MyApp] Đúng bản DLL cho AutoCAD R{major} ({BuildTarget})");
                    return;
                }
                string msg = major > AcadMajorMax && AcadMajorMax >= 26
                    ? $"AutoCAD R{major} mới hơn các bản đã build (tới AutoCAD 2027) -> đang dùng bản {BuildTarget}, chưa kiểm trên phiên bản này"
                    : $"DLL này là bản cho {BuildTarget} nhưng AutoCAD đang chạy là R{major} -> có thể lỗi. " +
                      "Tắt hẳn AutoCAD, mở lại rồi kéo thả LHB.lsp (tự chọn đúng bản: thư mục gốc = 2021 - 2024, net8 = 2025 - 2026, net10 = 2027)";
                Core.Logger.Warn("[MyApp] " + msg);
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage($"\n[LHB CẢNH BÁO] {msg}.");
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error(ex, "[MyApp] Kiểm tra phiên bản AutoCAD");
            }
        }

        /// <summary>
        /// Ghi thư mục gốc add-in (thư mục có LHB.lsp; v9.7: bản AutoCAD 2025+ nằm thư mục con net8 / net10 thì ghi thư mục
        /// cha) vào HKCU\Software\LHBBlockScheduler\InstallDir để LHB.lsp tìm được DLL khi kéo thả (LISP không tự biết đường
        /// dẫn của chính nó). Chỉ ghi khi thư mục có LHB.lsp, tức là thư mục phân phối thật, không phải bản shadow-copy.
        /// </summary>
        private static void RememberInstallDir()
        {
            try
            {
                string dir = Core.Logger.AddinRootFolder;
                if (string.IsNullOrEmpty(dir) || !System.IO.File.Exists(System.IO.Path.Combine(dir, "LHB.lsp")))
                {
                    Core.Logger.Log($"[MyApp] Không ghi InstallDir: thư mục add-in '{dir}' không có LHB.lsp");
                    return;
                }
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\LHBBlockScheduler"))
                {
                    key?.SetValue("InstallDir", dir);
                }
                Core.Logger.Log($"[MyApp] Đã ghi InstallDir='{dir}' vào HKCU\\Software\\LHBBlockScheduler");
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error(ex, "[MyApp] Ghi InstallDir vào registry thất bại");
            }
        }

        public void Terminate()
        {
            Core.Logger.Log("=== LHBBlockScheduler đã unload ===");
            Core.Logger.Flush();
        }
    }
}

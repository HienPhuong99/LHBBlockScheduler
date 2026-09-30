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

// Đăng ký toàn bộ command trong Commands.cs với AutoCAD
[assembly: CommandClass(typeof(Commands))]

// Chạy Initialize() khi assembly được NETLOAD, Terminate() khi unload
[assembly: ExtensionApplication(typeof(MyApp))]

namespace LHBBlockScheduler
{
    public class MyApp : IExtensionApplication
    {
        /// <summary>Phiên bản (sửa DUY NHẤT ở đây khi ra bản mới): tiêu đề form, dòng lệnh, AssemblyVersion, LHB.lsp (build.ps1 ghi vào).</summary>
        public const string Version = "9.5";
        public const string AssemblyVersionText = Version + ".0.0";
        /// <summary>"v9.4 Premium" - hiện trên tiêu đề form "Thống kê Block v9.4 Premium" và dòng lệnh lúc nạp.</summary>
        public const string DisplayVersion = "v" + Version + " Premium";

        public void Initialize()
        {
            Core.Logger.Log($"=== LHBBlockScheduler {DisplayVersion} đã nạp ===");
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

        /// <summary>
        /// Ghi thư mục chứa DLL vào HKCU\Software\LHBBlockScheduler\InstallDir để LHB.lsp tìm được
        /// DLL khi kéo thả (LISP không tự biết đường dẫn của chính nó). Chỉ ghi khi cạnh DLL có
        /// LHB.lsp, tức là thư mục phân phối thật, không phải bản shadow-copy.
        /// </summary>
        private static void RememberInstallDir()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "LHB.lsp")))
                {
                    Core.Logger.Log($"[MyApp] Không ghi InstallDir: thư mục DLL '{dir}' không có LHB.lsp");
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

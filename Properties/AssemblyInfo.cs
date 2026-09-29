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
[assembly: AssemblyVersion("9.1.0.0")]
[assembly: AssemblyFileVersion("9.1.0.0")]

// Đăng ký toàn bộ command trong Commands.cs với AutoCAD
[assembly: CommandClass(typeof(Commands))]

// Chạy Initialize() khi assembly được NETLOAD, Terminate() khi unload
[assembly: ExtensionApplication(typeof(MyApp))]

namespace LHBBlockScheduler
{
    public class MyApp : IExtensionApplication
    {
        public void Initialize()
        {
            Core.Logger.Log("=== LHBBlockScheduler đã nạp ===");
            RememberInstallDir();
            RegisterAliases();
            try
            {
                UI.RibbonBuilder.Init();
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage($"\n[LHB] v9.1 Premium - {Core.LicenseManager.StatusText}. Tab Ribbon 'LHB Premium', gõ LHBPALETTE mở bảng công cụ.");
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;

[assembly: ExtensionApplication(typeof(LHBBlockScheduler.Loader))]

namespace LHBBlockScheduler
{
    public class RuntimeVersionInfo
    {
        public string DirectoryPath { get; set; } = "";
        public string Configuration { get; set; } = "Unknown";
        public string BuildTime { get; set; } = "Unknown";
        public long DllSize { get; set; }
        public string Md5Hash { get; set; } = "Unknown";
        public bool Md5Mismatch { get; set; }
        public string ExpectedMd5 { get; set; } = "";
    }

    /// <summary>
    /// Shadow-copy loader mỏng cho AutoCAD:
    /// Cho phép nạp LHBBlockScheduler.dll từ thư mục Runtime (Debug/Release, timestamped) trong %APPDATA%.
    /// Giúp file DLL trong thư mục build không bị AutoCAD lock, cho phép dotnet build chạy được ngay cả khi CAD đang mở.
    /// </summary>
    public class Loader : IExtensionApplication
    {
        private static RuntimeVersionInfo _currentVersion;

        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            ed?.WriteMessage("\n[LHBLoader] Shadow-copy loader đã khởi động. Gõ LHBRELOAD để nạp bản mới nhất.\n");
            LoadLatestRuntime(true);
        }

        public void Terminate()
        {
        }

        [CommandMethod("LHBRELOAD")]
        public static void ReloadCommand()
        {
            LoadLatestRuntime(false, null);
        }

        [CommandMethod("LHBRELOADDEBUG")]
        public static void ReloadDebugCommand()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SetActiveConfig(appData, "Debug");
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor?.WriteMessage("\n[LHBLoader] Đã chuyển cấu hình hoạt động sang 'Debug'.");
            LoadLatestRuntime(false, "Debug");
        }

        [CommandMethod("LHBRELOADRELEASE")]
        public static void ReloadReleaseCommand()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SetActiveConfig(appData, "Release");
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor?.WriteMessage("\n[LHBLoader] Đã chuyển cấu hình hoạt động sang 'Release'.");
            LoadLatestRuntime(false, "Release");
        }


        public static void LoadLatestRuntime(bool isInitial, string targetConfig = null)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;

            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string runtimeBase = Path.Combine(appData, "LHBBlockScheduler", "Runtime");

                if (!Directory.Exists(runtimeBase))
                {
                    ed?.WriteMessage($"\n[LHBLoader] Chưa tìm thấy thư mục Runtime: {runtimeBase}\n");
                    return;
                }

                // Xác định cấu hình hoạt động: nếu không chỉ định thủ công thì đọc từ active-config.txt
                string activeConfig = targetConfig;
                if (string.IsNullOrEmpty(activeConfig))
                {
                    activeConfig = GetActiveConfig(appData, ed);
                }

                string configDir = Path.Combine(runtimeBase, activeConfig);
                if (!Directory.Exists(configDir))
                {
                    ed?.WriteMessage($"\n[LHBLoader] Không tìm thấy thư mục cấu hình '{activeConfig}' tại: {configDir}\n");
                    return;
                }

                // CHỈ quét các thư mục timestamp trong ĐÚNG cấu hình active đó, KHÔNG so chéo cấu hình khác
                var validDirs = Directory.GetDirectories(configDir)
                    .Where(d => File.Exists(Path.Combine(d, "LHBBlockScheduler.dll")))
                    .OrderByDescending(d => Path.GetFileName(d))
                    .ToList();

                if (validDirs.Count == 0)
                {
                    ed?.WriteMessage($"\n[LHBLoader] Không tìm thấy thư mục timestamp nào cho cấu hình '{activeConfig}' tại: {configDir}\n");
                    return;
                }

                string latestDir = validDirs[0];
                string dllPath = Path.Combine(latestDir, "LHBBlockScheduler.dll");

                var info = ReadVersionInfo(latestDir, dllPath, activeConfig);

                // Kiểm tra mã MD5: nếu có file build-info.txt mà MD5 tính lại từ DLL thực tế không khớp -> in CẢNH BÁO NỔI BẬT
                if (info.Md5Mismatch)
                {
                    ed?.WriteMessage("\n******************************************************************");
                    ed?.WriteMessage("\n* [LHBLoader] !!! CẢNH BÁO LỖI: MÃ HASH MD5 KHÔNG KHỚP !!!        *");
                    ed?.WriteMessage($"\n*  - Thư mục nạp              : {latestDir}");
                    ed?.WriteMessage($"\n*  - MD5 trong build-info.txt : {info.ExpectedMd5}");
                    ed?.WriteMessage($"\n*  - MD5 tính từ DLL thực tế  : {info.Md5Hash}");
                    ed?.WriteMessage("\n*  -> File DLL có thể bị copy dở dang, lỗi cache hoặc bị can thiệp! *");
                    ed?.WriteMessage("\n******************************************************************\n");
                }

                ed?.WriteMessage($"\n[LHBLoader] Đang nạp phiên bản [{Path.GetFileName(latestDir)}] ({info.Configuration})...");
                var asm = Assembly.LoadFrom(dllPath);

                // Khởi chạy IExtensionApplication nếu có trong assembly
                try
                {
                    var extTypes = asm.GetTypes().Where(t => typeof(IExtensionApplication).IsAssignableFrom(t) 
                        && !t.IsInterface && !t.IsAbstract && t.FullName != typeof(Loader).FullName);
                    foreach (var t in extTypes)
                    {
                        var ext = (IExtensionApplication)Activator.CreateInstance(t);
                        ext.Initialize();
                    }
                }
                catch
                {
                    // Bỏ qua nếu assembly không có IExtensionApplication bổ sung
                }

                _currentVersion = info;

                ed?.WriteMessage("\n==================================================================");
                ed?.WriteMessage($"\n[LHBLoader] ĐÃ NẠP THÀNH CÔNG PHIÊN BẢN MỚI NHẤT ({info.Configuration}):");
                ed?.WriteMessage($"\n  - Thư mục       : {info.DirectoryPath}");
                ed?.WriteMessage($"\n  - Cấu hình      : {info.Configuration}");
                ed?.WriteMessage($"\n  - Thời gian build: {info.BuildTime}");
                ed?.WriteMessage($"\n  - Kích thước DLL : {info.DllSize:N0} bytes");
                ed?.WriteMessage($"\n  - Mã băm MD5    : {info.Md5Hash}" + (info.Md5Mismatch ? " [KHÔNG KHỚP BUILD-INFO!]" : " [KHỚP]"));
                ed?.WriteMessage("\n  - Lệnh khả dụng : LHBSCAN, LHBMAU, LHBLENH, LHBLEGEND, LHBDIAG,");
                ed?.WriteMessage("\n                    LHBCLEARCACHE, LHBLOG, LHBLOGPATH, LHBRELOAD, LHBVERSION");
                ed?.WriteMessage("\n==================================================================\n");
            }
            catch (Exception ex)
            {
                ed?.WriteMessage($"\n[LHBLoader] Lỗi khi nạp assembly: {ex.Message}\n");
            }
        }

        private static string GetActiveConfig(string appData, Editor ed)
        {
            string configPath = Path.Combine(appData, "LHBBlockScheduler", "active-config.txt");
            if (File.Exists(configPath))
            {
                try
                {
                    string cfg = File.ReadAllText(configPath).Trim();
                    if (cfg.Equals("Release", StringComparison.OrdinalIgnoreCase)) return "Release";
                    if (cfg.Equals("Debug", StringComparison.OrdinalIgnoreCase)) return "Debug";
                }
                catch { }
            }

            ed?.WriteMessage("\n[LHBLoader] CẢNH BÁO: Chưa tìm thấy 'active-config.txt'. Mặc định sử dụng cấu hình 'Debug'.\n");
            return "Debug";
        }

        private static void SetActiveConfig(string appData, string config)
        {
            try
            {
                string dir = Path.Combine(appData, "LHBBlockScheduler");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string configPath = Path.Combine(dir, "active-config.txt");
                File.WriteAllText(configPath, config.Trim(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static RuntimeVersionInfo ReadVersionInfo(string dir, string dllPath, string fallbackConfig)
        {
            var info = new RuntimeVersionInfo
            {
                DirectoryPath = dir,
                Configuration = fallbackConfig
            };

            if (File.Exists(dllPath))
            {
                var fi = new FileInfo(dllPath);
                info.DllSize = fi.Length;
                info.BuildTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            // Tính MD5 thực tế từ file DLL trên đĩa
            string actualMd5 = CalculateMd5(dllPath);
            info.Md5Hash = actualMd5;

            // Đọc file build-info.txt nếu có
            string buildInfoPath = Path.Combine(dir, "build-info.txt");
            if (File.Exists(buildInfoPath))
            {
                try
                {
                    foreach (var rawLine in File.ReadAllLines(buildInfoPath))
                    {
                        var line = rawLine?.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        int colonIdx = line.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = line.Substring(0, colonIdx).Trim();
                            string val = line.Substring(colonIdx + 1).Trim();
                            if (key.Equals("Configuration", StringComparison.OrdinalIgnoreCase))
                                info.Configuration = val;
                            else if (key.Equals("BuildTime", StringComparison.OrdinalIgnoreCase))
                                info.BuildTime = val;
                            else if (key.Equals("DllSize", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long s))
                                info.DllSize = s;
                            else if (key.Equals("MD5", StringComparison.OrdinalIgnoreCase))
                                info.ExpectedMd5 = val.Trim().ToUpperInvariant();
                        }
                    }

                    // So khớp MD5 thực tế và MD5 trong build-info.txt
                    if (!string.IsNullOrEmpty(info.ExpectedMd5) && !string.IsNullOrEmpty(actualMd5))
                    {
                        if (!string.Equals(info.ExpectedMd5, actualMd5, StringComparison.OrdinalIgnoreCase))
                        {
                            info.Md5Mismatch = true;
                        }
                    }
                }
                catch { }
            }

            return info;
        }

        private static string CalculateMd5(string filePath)
        {
            if (!File.Exists(filePath)) return "FILE_NOT_FOUND";
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                }
            }
            catch (Exception ex)
            {
                return $"ERROR_{ex.Message}";
            }
        }
    }
}

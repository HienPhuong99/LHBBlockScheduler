using System;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Ghi log song song ra 2 nơi:
    /// 1. %APPDATA%\LHBBlockScheduler\log.txt (mặc định cho máy cài đặt)
    /// 2. <Thư mục chứa DLL>\log.txt (thuận tiện mang thư mục sang máy khác test, zip lại có luôn log)
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();

        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler");

        public static string AppDataLogFilePath => Path.Combine(AppDataFolder, "log.txt");

        public static string DllFolder
        {
            get
            {
                try
                {
                    string loc = typeof(Logger).Assembly.Location;
                    return !string.IsNullOrEmpty(loc) ? Path.GetDirectoryName(loc) : null;
                }
                catch { return null; }
            }
        }

        public static string LocalLogFilePath
        {
            get
            {
                try
                {
                    string dir = DllFolder;
                    return !string.IsNullOrEmpty(dir) ? Path.Combine(dir, "log.txt") : null;
                }
                catch { return null; }
            }
        }

        private static void EnsureAppDataFolder()
        {
            if (!Directory.Exists(AppDataFolder))
                Directory.CreateDirectory(AppDataFolder);
        }

        private static void Write(string level, string message)
        {
            lock (_lock)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

                // 1. Ghi vào %APPDATA%\LHBBlockScheduler\log.txt
                try
                {
                    EnsureAppDataFolder();
                    using (var fs = new FileStream(AppDataLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                        sw.Flush();
                    }
                }
                catch (Exception ex)
                {
                    // Fallback in ra Command line của AutoCAD nếu ghi AppData thất bại
                    try
                    {
                        var doc = Application.DocumentManager?.MdiActiveDocument;
                        var ed = doc?.Editor;
                        ed?.WriteMessage($"\n[LHB LOG FALLBACK - Ghi AppData thất bại: {ex.Message}]\n{line}\n");
                    }
                    catch { }
                }

                // 2. Ghi song song vào <Thư mục chứa DLL>\log.txt
                try
                {
                    string localLog = LocalLogFilePath;
                    if (!string.IsNullOrEmpty(localLog))
                    {
                        using (var fs = new FileStream(localLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        using (var sw = new StreamWriter(fs, Encoding.UTF8))
                        {
                            sw.WriteLine(line);
                            sw.Flush();
                        }
                    }
                }
                catch
                {
                    // Nếu thư mục chứa DLL bị read-only (chặn quyền ghi), bỏ qua im lặng không throw
                }
            }
        }

        public static void Log(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(Exception ex, string context = null)
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";
            string msg = $"{prefix}{ex.GetType().Name}: {ex.Message}\nStackTrace:\n{ex.StackTrace}";
            if (ex.InnerException != null)
            {
                msg += $"\nInnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" +
                       $"\n{ex.InnerException.StackTrace}";
            }
            Write("ERROR", msg);
        }

        /// <summary>Xoá log cũ - xoá ở cả 2 vị trí nếu có.</summary>
        public static void ClearLog()
        {
            lock (_lock)
            {
                try
                {
                    EnsureAppDataFolder();
                    using (var fs = new FileStream(AppDataLogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Flush();
                    }
                }
                catch { }

                try
                {
                    string localLog = LocalLogFilePath;
                    if (!string.IsNullOrEmpty(localLog) && File.Exists(localLog))
                    {
                        using (var fs = new FileStream(localLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        using (var sw = new StreamWriter(fs, Encoding.UTF8))
                        {
                            sw.Flush();
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>Trả về file log ưu tiên (ưu tiên local log cạnh DLL nếu tồn tại, ngược lại trả về AppData log).</summary>
        public static string GetLogFilePath()
        {
            string local = LocalLogFilePath;
            if (!string.IsNullOrEmpty(local) && File.Exists(local))
                return local;
            return AppDataLogFilePath;
        }
    }
}

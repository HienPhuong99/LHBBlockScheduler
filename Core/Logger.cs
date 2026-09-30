using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Ghi log song song ra 2 nơi:
    /// 1. %APPDATA%\LHBBlockScheduler\log.txt (mặc định cho máy cài đặt)
    /// 2. <Thư mục gốc add-in>\log.txt (thuận tiện mang thư mục sang máy khác test, zip lại có luôn log; v9.7: bản
    ///    AutoCAD 2025+ ở thư mục con net8 / net10 cũng ghi vào thư mục gốc)
    /// v9.1: giữ file mở trong lúc ghi dồn dập (mỗi dòng vẫn flush ngay, AutoCAD crash không mất log), tự đóng file
    /// sau 1 giây không ghi để Notepad / nén zip đọc được. Trước đây mỗi dòng mở + đóng 2 file -> xuất bảng
    /// vài trăm dòng log chậm thấy rõ. File log quá 5 MB thì đổi tên thành log.old.txt lúc nạp add-in.
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private const long MaxLogBytes = 5L * 1024 * 1024;
        private const int IdleCloseMs = 1000;

        private static StreamWriter _appWriter, _localWriter;
        private static Timer _closeTimer;
        private static bool _initialized, _localDisabled;
        private static string _dllFolder, _addinRoot, _localLogPath;

        public static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler");

        public static string AppDataLogFilePath => Path.Combine(AppDataFolder, "log.txt");

        public static string DllFolder
        {
            get
            {
                if (_dllFolder != null) return _dllFolder;
                try
                {
                    string loc = typeof(Logger).Assembly.Location;
                    _dllFolder = !string.IsNullOrEmpty(loc) ? Path.GetDirectoryName(loc) : null;
                }
                catch { _dllFolder = null; }
                return _dllFolder;
            }
        }

        /// <summary>
        /// v9.7: thư mục gốc add-in (thư mục có LHB.lsp). Bản AutoCAD 2025+ nằm thư mục con net8 / net10 của thư mục gốc ->
        /// thư viện block mẫu (ThuVienMau), log.txt, LHBDIAG dùng chung thư mục gốc cho mọi phiên bản AutoCAD trên máy.
        /// Bản AutoCAD 2021 - 2024 nằm ngay thư mục gốc -> = DllFolder như trước. Quy tắc: AddinPaths.ResolveRoot.
        /// </summary>
        public static string AddinRootFolder
        {
            get
            {
                if (_addinRoot != null) return _addinRoot;
                try { _addinRoot = AddinPaths.ResolveRoot(DllFolder, File.Exists); }
                catch { _addinRoot = DllFolder; }
                return _addinRoot;
            }
        }

        public static string LocalLogFilePath
        {
            get
            {
                if (_localLogPath != null) return _localLogPath;
                string dir = AddinRootFolder;
                _localLogPath = !string.IsNullOrEmpty(dir) ? Path.Combine(dir, "log.txt") : null;
                return _localLogPath;
            }
        }

        /// <summary>Lần ghi đầu tiên trong phiên: tạo thư mục APPDATA, đổi tên log quá lớn.</summary>
        private static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            try { Directory.CreateDirectory(AppDataFolder); } catch { }
            RotateIfTooBig(AppDataLogFilePath);
            RotateIfTooBig(LocalLogFilePath);
            _closeTimer = new Timer(_ => CloseWriters(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private static void RotateIfTooBig(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || new FileInfo(path).Length < MaxLogBytes) return;
                string old = Path.Combine(Path.GetDirectoryName(path), "log.old.txt");
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch
            {
                // Không đổi tên được (file đang mở ở chỗ khác) -> ghi tiếp vào file cũ
            }
        }

        private static StreamWriter Open(string path)
        {
            // Cho phép chương trình khác đọc / ghi / xoá file trong lúc add-in đang giữ file
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
        }

        private static void CloseWriters()
        {
            lock (_lock)
            {
                try { _appWriter?.Dispose(); } catch { }
                try { _localWriter?.Dispose(); } catch { }
                _appWriter = null;
                _localWriter = null;
            }
        }

        private static void Write(string level, string message)
        {
            lock (_lock)
            {
                EnsureInit();
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

                // 1. Ghi vào %APPDATA%\LHBBlockScheduler\log.txt
                try
                {
                    _appWriter ??= Open(AppDataLogFilePath);
                    _appWriter.WriteLine(line);
                }
                catch (Exception ex)
                {
                    try { _appWriter?.Dispose(); } catch { }
                    _appWriter = null;
                    // Fallback in ra Command line của AutoCAD nếu ghi AppData thất bại
                    try
                    {
                        var doc = Application.DocumentManager?.MdiActiveDocument;
                        doc?.Editor?.WriteMessage($"\n[LHB LOG FALLBACK - Ghi AppData thất bại: {ex.Message}]\n{line}\n");
                    }
                    catch { }
                }

                // 2. Ghi song song vào <thư mục gốc add-in>\log.txt
                if (!_localDisabled)
                {
                    try
                    {
                        string localLog = LocalLogFilePath;
                        if (string.IsNullOrEmpty(localLog)) _localDisabled = true;
                        else
                        {
                            _localWriter ??= Open(localLog);
                            _localWriter.WriteLine(line);
                        }
                    }
                    catch
                    {
                        // Thư mục chứa DLL read-only (chặn quyền ghi) -> bỏ ghi log cạnh DLL cả phiên, không throw
                        try { _localWriter?.Dispose(); } catch { }
                        _localWriter = null;
                        _localDisabled = true;
                    }
                }

                // Hết ghi dồn dập 1 giây thì đóng file
                _closeTimer.Change(IdleCloseMs, Timeout.Infinite);
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

        /// <summary>Đóng file log (lúc unload add-in).</summary>
        public static void Flush() => CloseWriters();

        /// <summary>Trả về file log ưu tiên (ưu tiên local log cạnh DLL nếu tồn tại, ngược lại trả về AppData log).</summary>
        public static string GetLogFilePath()
        {
            string local = LocalLogFilePath;
            if (!string.IsNullOrEmpty(local) && File.Exists(local))
                return local;
            return AppDataLogFilePath;
        }

        /// <summary>
        /// N dòng cuối của file log. Mở chia sẻ đọc / ghi: File.ReadAllLines báo "file đang được dùng" khi Logger
        /// đang giữ file.
        /// </summary>
        public static List<string> ReadTail(string path, int lines)
        {
            var result = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                var queue = new Queue<string>(lines + 1);
                string l;
                while ((l = sr.ReadLine()) != null)
                {
                    queue.Enqueue(l);
                    if (queue.Count > lines) queue.Dequeue();
                }
                result.AddRange(queue);
            }
            return result;
        }
    }
}

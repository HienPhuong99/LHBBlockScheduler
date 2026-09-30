using System;
using System.IO;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Ghi file an toàn (v9.4): trước đây settings.json / bộ block mẫu ghi thẳng bằng File.WriteAllText -> CAD crash hoặc
    /// mất điện giữa lúc ghi làm file hỏng, lần sau nạp mặc định (mất phím tắt, mẫu bảng, mã kích hoạt...).
    /// Nay ghi ra file tạm, đẩy xuống đĩa, rồi thay file cũ (bản cũ giữ ở &lt;file&gt;.bak để đọc lại khi file chính hỏng).
    /// </summary>
    public static class FileHelper
    {
        public static string BackupPath(string path) => path + ".bak";

        public static void WriteAllTextAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Tên file tạm riêng mỗi lần ghi: 2 AutoCAD cùng mở không ghi đè file tạm của nhau
            string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.Write(content ?? "");
                    sw.Flush();
                    fs.Flush(true);
                }

                if (!File.Exists(path))
                {
                    File.Move(tmp, path);
                    return;
                }
                try
                {
                    File.Replace(tmp, path, BackupPath(path), true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
                {
                    // File.Replace không chạy trên một số ổ mạng / FAT32 -> chép bản cũ sang .bak rồi chép đè
                    Logger.Warn($"[FileHelper] File.Replace '{path}' lỗi ({ex.GetType().Name}: {ex.Message}) -> chép đè thường");
                    File.Copy(path, BackupPath(path), true);
                    File.Copy(tmp, path, true);
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        /// <summary>
        /// Đọc + phân tích file; file chính không có / hỏng (parse lỗi hoặc ra null) thì thử bản &lt;file&gt;.bak.
        /// Trả default nếu cả 2 đều không dùng được.
        /// </summary>
        public static T ReadWithBackup<T>(string path, Func<string, T> parse, string what) where T : class
        {
            foreach (var candidate in new[] { path, BackupPath(path) })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var result = parse(File.ReadAllText(candidate));
                    if (result != null)
                    {
                        if (candidate != path) Logger.Warn($"[FileHelper] {what}: file chính '{path}' hỏng / không đọc được -> dùng bản dự phòng '{candidate}'");
                        return result;
                    }
                    Logger.Warn($"[FileHelper] {what}: '{candidate}' rỗng / sai định dạng");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[FileHelper] {what}: đọc '{candidate}' thất bại");
                }
            }
            return null;
        }
    }
}

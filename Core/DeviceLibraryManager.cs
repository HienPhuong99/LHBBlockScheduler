using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    public static class DeviceLibraryManager
    {
        public static string LibrariesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler", "Libraries");

        public static List<string> ListLibraries()
        {
            try
            {
                Logger.Log($"[DeviceLibraryManager.ListLibraries] Bắt đầu quét thư viện. Thư mục: '{LibrariesFolder}', Directory.Exists={Directory.Exists(LibrariesFolder)}");
                if (!Directory.Exists(LibrariesFolder))
                    Directory.CreateDirectory(LibrariesFolder);

                var files = Directory.GetFiles(LibrariesFolder, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToList();

                Logger.Log($"[DeviceLibraryManager.ListLibraries] Thư mục quét: '{LibrariesFolder}', tìm thấy {files.Count} file: [{string.Join(", ", files)}]");

                if (files.Count == 0)
                {
                    // Tạo thư viện default rỗng nếu chưa có gì
                    var defLib = new DeviceLibrary { Name = "default" };
                    SaveLibrary(defLib);
                    files.Add("default");
                    Logger.Log("[DeviceLibraryManager.ListLibraries] Đã tạo thư viện 'default' rỗng khởi tạo.");
                }
                return files;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DeviceLibraryManager.ListLibraries");
                return new List<string> { "default" };
            }
        }

        public static DeviceLibrary LoadLibrary(string name)
        {
            try
            {
                if (!Directory.Exists(LibrariesFolder)) Directory.CreateDirectory(LibrariesFolder);
                string path = Path.Combine(LibrariesFolder, name + ".json");
                Logger.Log($"[DeviceLibraryManager.LoadLibrary] Đọc thư viện '{name}' từ file: '{path}', File.Exists={File.Exists(path)}");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var lib = NormalizeLibrary(JsonHelper.Deserialize<DeviceLibrary>(json), name);
                    Logger.Log($"[DeviceLibraryManager.LoadLibrary] Đọc thành công '{name}': {lib?.Entries?.Count ?? 0} entries");
                    return lib;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"DeviceLibraryManager.LoadLibrary('{name}')");
            }
            return new DeviceLibrary { Name = name };
        }

        public static void SaveLibrary(DeviceLibrary lib)
        {
            string path = Path.Combine(LibrariesFolder, lib.Name + ".json");
            int entryCount = lib.Entries?.Count ?? 0;
            Logger.Log($"[DeviceLibraryManager.SaveLibrary] Đường dẫn file sắp ghi: '{path}', số entries={entryCount}");
            try
            {
                if (!Directory.Exists(LibrariesFolder)) Directory.CreateDirectory(LibrariesFolder);
                string json = JsonHelper.Serialize(lib);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                Logger.Log($"[DeviceLibraryManager.SaveLibrary] Ghi file THÀNH CÔNG: '{path}' ({json.Length} bytes, {entryCount} mục)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[DeviceLibraryManager.SaveLibrary] Ghi file THẤT BẠI: '{path}' - Exception: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// DataContractJsonSerializer không chạy property initializer khi đọc JSON,
        /// nên List/chuỗi mặc định có thể null -> bù lại giá trị mặc định ở đây.
        /// </summary>
        private static DeviceLibrary NormalizeLibrary(DeviceLibrary lib, string fallbackName)
        {
            if (lib == null) return null;
            if (string.IsNullOrWhiteSpace(lib.Name)) lib.Name = fallbackName ?? "default";
            if (lib.Entries == null) lib.Entries = new List<DeviceLibraryEntry>();
            lib.Entries.RemoveAll(e => e == null);
            foreach (var e in lib.Entries)
            {
                if (e.KnownBlockNames == null) e.KnownBlockNames = new List<string>();
                if (string.IsNullOrWhiteSpace(e.Unit)) e.Unit = "Cái";
            }
            return lib;
        }

        public static void ExportLibraryToFile(DeviceLibrary lib, string filePath)
        {
            File.WriteAllText(filePath, JsonHelper.Serialize(lib), System.Text.Encoding.UTF8);
            Logger.Log($"[DeviceLibraryManager.ExportLibraryToFile] Đã xuất '{lib?.Name}' ({lib?.Entries?.Count ?? 0} mục) ra '{filePath}'");
        }

        public static DeviceLibrary ImportLibraryFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath);
            var lib = NormalizeLibrary(JsonHelper.Deserialize<DeviceLibrary>(json), Path.GetFileNameWithoutExtension(filePath));
            Logger.Log($"[DeviceLibraryManager.ImportLibraryFromFile] Đọc '{filePath}': {lib?.Entries?.Count ?? 0} mục");
            if (lib != null)
            {
                string name = Path.GetFileNameWithoutExtension(filePath);
                lib.Name = name;
                SaveLibrary(lib);
            }
            return lib;
        }

        /// <summary>
        /// Thực hiện "Quy hoạch" danh sách block theo thư viện thiết bị.
        /// Thứ tự ưu tiên:
        /// 1. ShapeHash Hamming distance <= threshold (mặc định 5). Khớp xong tự thêm tên vào KnownBlockNames.
        /// 2. Tên block nằm trong KnownBlockNames (không phân biệt hoa thường, bỏ dấu).
        /// 3. Không khớp -> IsMatchedByLibrary = false, đẩy xuống cuối danh sách.
        /// </summary>
        public static List<BlockItem> ApplyLibrary(List<BlockItem> items, DeviceLibrary library, int hashThreshold = 5)
        {
            if (library == null || library.Entries == null || library.Entries.Count == 0)
            {
                Logger.Log("DeviceLibraryManager.ApplyLibrary: thư viện rỗng, giữ nguyên danh sách.");
                return items;
            }

            Logger.Log($"DeviceLibraryManager.ApplyLibrary: bắt đầu quy hoạch {items.Count} dòng với thư viện '{library.Name}' ({library.Entries.Count} entries, threshold={hashThreshold})");

            bool libraryModified = false;

            foreach (var item in items)
            {
                DeviceLibraryEntry matchedEntry = null;
                string matchReason = null;
                int bestDistance = int.MaxValue;

                // 1. Khớp theo cặp (ShapeHash, VisibilityState) (nếu item có ShapeHash khác 0)
                if (item.ShapeHash != 0)
                {
                    foreach (var entry in library.Entries)
                    {
                        if (entry.ShapeHash == 0) continue;

                        // So khớp bằng cặp (ShapeHash, VisibilityState):
                        // Khác VisibilityState thì không thể khớp cùng 1 entry
                        bool visMatches = string.Equals(entry.VisibilityState ?? "", item.VisibilityState ?? "", StringComparison.OrdinalIgnoreCase);
                        if (!visMatches) continue;

                        int dist = ThumbnailGenerator.HammingDistance(item.ShapeHash, entry.ShapeHash);
                        if (dist <= hashThreshold && dist < bestDistance)
                        {
                            bestDistance = dist;
                            matchedEntry = entry;
                            matchReason = $"Cặp (ShapeHash, VisibilityState) (khoảng cách Hamming={dist})";
                        }
                    }

                    // Tự động ghi nhớ tên block vào KnownBlockNames của entry
                    if (matchedEntry != null && !string.IsNullOrEmpty(item.BlockName))
                    {
                        if (matchedEntry.KnownBlockNames == null)
                            matchedEntry.KnownBlockNames = new List<string>();

                        if (!matchedEntry.KnownBlockNames.Any(n => string.Equals(n, item.BlockName, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchedEntry.KnownBlockNames.Add(item.BlockName);
                            libraryModified = true;
                            Logger.Log($"DeviceLibraryManager: tự động học tên block '{item.BlockName}' vào entry '{matchedEntry.StandardName}'");
                        }
                    }
                }

                // 2. Nếu chưa khớp bằng hash, thử khớp theo KnownBlockNames kết hợp VisibilityState
                if (matchedEntry == null)
                {
                    foreach (var entry in library.Entries)
                    {
                        bool visMatches = string.Equals(entry.VisibilityState ?? "", item.VisibilityState ?? "", StringComparison.OrdinalIgnoreCase);
                        if (!visMatches) continue;

                        if (entry.KnownBlockNames != null && entry.KnownBlockNames.Any(n =>
                            VietnameseHelper.ContainsIgnoreCaseAndDiacritics(n, item.BlockName) &&
                            VietnameseHelper.ContainsIgnoreCaseAndDiacritics(item.BlockName, n)))
                        {
                            matchedEntry = entry;
                            matchReason = "KnownBlockNames + VisibilityState";
                            break;
                        }
                    }
                }

                // Áp dụng kết quả khớp
                if (matchedEntry != null)
                {
                    item.IsMatchedByLibrary = true;
                    item.DisplayName = matchedEntry.StandardName;
                    if (!string.IsNullOrEmpty(matchedEntry.VisibilityState))
                        item.VisibilityState = matchedEntry.VisibilityState;
                    if (!string.IsNullOrEmpty(matchedEntry.Unit))
                        item.Unit = matchedEntry.Unit;
                    if (!string.IsNullOrEmpty(matchedEntry.Note))
                        item.Note = matchedEntry.Note;
                    item.Order = matchedEntry.Order;

                    Logger.Log($"Quy hoạch: block '{item.BlockName}' -> '{matchedEntry.StandardName}' [đường {matchReason}]");
                }
                else
                {
                    item.IsMatchedByLibrary = false;
                    item.Order = 99999; // đẩy xuống cuối
                    Logger.Log($"Quy hoạch: block '{item.BlockName}' KHÔNG khớp với thư viện.");
                }
            }

            if (libraryModified)
            {
                SaveLibrary(library);
            }

            // Sắp xếp: các item khớp thư viện xếp trước theo Order, chưa khớp xếp sau theo tên alphabet
            var sorted = items
                .OrderBy(i => i.IsMatchedByLibrary ? 0 : 1)
                .ThenBy(i => i.Order)
                .ThenBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Đánh lại số Order liên tục 0, 1, 2...
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Order = i;
            }

            return sorted;
        }
    }
}

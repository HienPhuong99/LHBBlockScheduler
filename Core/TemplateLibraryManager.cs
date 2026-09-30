using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Thư viện block mẫu (yêu cầu 29/09/2026, giống hộp thoại "Thông tin Block mẫu" trong video mẫu):
    ///  - Lưu CẠNH ADD-IN: &lt;thư mục DLL&gt;\ThuVienMau\&lt;bộ&gt;.json (tên thống kê, đơn vị, ảnh ký hiệu base64)
    ///    + &lt;bộ&gt;.dwg (định nghĩa block). Mang cả thư mục add-in sang máy khác là có đủ block mẫu.
    ///  - Bản sao dự phòng ở %APPDATA%\LHBBlockScheduler\ThuVienMau: giải nén bản add-in mới sang thư mục khác
    ///    thì tự chép thư viện từ bản dự phòng sang, không mất block mẫu.
    ///  - Thư mục DLL không ghi được (vd Program Files) -> dùng thẳng thư mục APPDATA.
    /// </summary>
    public static class TemplateLibraryManager
    {
        public const string FolderName = "ThuVienMau";
        public const string DefaultSetName = "Data1";

        private static string _folder;

        public static string BackupFolder => Path.Combine(Logger.AppDataFolder, FolderName);

        /// <summary>Thư mục thư viện đang dùng (cạnh DLL nếu ghi được, không thì APPDATA).</summary>
        public static string Folder
        {
            get
            {
                if (_folder != null) return _folder;
                string dllDir = Logger.DllFolder;
                string candidate = string.IsNullOrEmpty(dllDir) ? null : Path.Combine(dllDir, FolderName);
                if (candidate != null && CanWrite(candidate))
                {
                    _folder = candidate;
                }
                else
                {
                    _folder = BackupFolder;
                    Directory.CreateDirectory(_folder);
                    Logger.Warn($"[TemplateLibrary] Không ghi được thư mục cạnh DLL '{candidate}' -> lưu thư viện mẫu ở '{_folder}'");
                }
                MigrateFromBackupIfEmpty();
                Logger.Log($"[TemplateLibrary] Thư mục thư viện block mẫu: '{_folder}'");
                MigrateDeviceLibraries();
                return _folder;
            }
        }

        /// <summary>Thư mục thư viện thiết bị CŨ (trước v9.1): chỉ đọc để chuyển sang block mẫu.</summary>
        public static string OldLibrariesFolder => Path.Combine(Logger.AppDataFolder, "Libraries");

        public const string OldLibrarySetPrefix = "TV cu ";

        /// <summary>
        /// v9.1 bỏ thư viện thiết bị cũ (nút Quy hoạch / Thêm vào TV / Chỉ đếm block có trong TV), chỉ còn thư viện
        /// block mẫu. Chuyển 1 lần: mỗi file cũ có dữ liệu thành bộ mẫu "TV cu &lt;tên&gt;" (không đụng bộ đang dùng, không
        /// làm đổi kết quả quét), mỗi tên block đã biết của 1 thiết bị thành 1 block mẫu. Thiết bị cũ chỉ có hình, không
        /// có tên block thì không chuyển được (block mẫu khớp theo tên) -> ghi log. Xong ghi settings
        /// DeviceLibrariesMigrated = true.
        /// </summary>
        private static void MigrateDeviceLibraries()
        {
            if (SettingsManager.Current.DeviceLibrariesMigrated) return;
            try
            {
                if (Directory.Exists(OldLibrariesFolder))
                {
                    foreach (var file in Directory.GetFiles(OldLibrariesFolder, "*.json"))
                    {
                        string oldName = Path.GetFileNameWithoutExtension(file);
                        DeviceLibrary old;
                        try
                        {
                            old = JsonHelper.Deserialize<DeviceLibrary>(File.ReadAllText(file));
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[TemplateLibrary.Migrate] Bỏ qua '{file}': đọc lỗi {ex.Message}");
                            continue;
                        }
                        var entries = (old?.Entries ?? new List<DeviceLibraryEntry>()).Where(e => e != null).OrderBy(e => e.Order).ToList();
                        if (entries.Count == 0) continue;

                        string setName = OldLibrarySetPrefix + oldName;
                        if (File.Exists(JsonPath(setName))) continue;
                        var lib = new TemplateLibrary { Name = setName };
                        int noName = 0;
                        foreach (var e in entries)
                        {
                            var names = (e.KnownBlockNames ?? new List<string>())
                                .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("*"))
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                            if (names.Count == 0) { noName++; continue; }
                            foreach (var n in names)
                            {
                                if (lib.Entries.Any(x => string.Equals(x.BlockName, n, StringComparison.OrdinalIgnoreCase) &&
                                                         string.Equals(x.VisibilityState, e.VisibilityState ?? "", StringComparison.OrdinalIgnoreCase)))
                                    continue;
                                lib.Entries.Add(new TemplateEntry
                                {
                                    BlockName = n,
                                    VisibilityState = e.VisibilityState ?? "",
                                    DisplayName = string.IsNullOrWhiteSpace(e.StandardName) ? n : e.StandardName,
                                    Unit = string.IsNullOrWhiteSpace(e.Unit) ? "Cái" : e.Unit,
                                    Note = e.Note ?? "",
                                    ThumbnailBase64 = e.ThumbnailBase64
                                });
                            }
                        }
                        if (lib.Entries.Count > 0) Save(lib);
                        Logger.Log($"[TemplateLibrary.Migrate] Thư viện cũ '{oldName}' ({entries.Count} thiết bị) -> bộ mẫu '{setName}': " +
                                   $"{lib.Entries.Count} block mẫu" + (noName > 0 ? $", {noName} thiết bị không có tên block (chỉ có hình) không chuyển được" : ""));
                    }
                }
                SettingsManager.Current.DeviceLibrariesMigrated = true;
                SettingsManager.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TemplateLibrary.Migrate] Chuyển thư viện thiết bị cũ sang block mẫu");
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".lhb_write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Thư mục cạnh DLL chưa có bộ nào (vừa giải nén bản mới) mà bản dự phòng có -> chép sang.</summary>
        private static void MigrateFromBackupIfEmpty()
        {
            try
            {
                if (string.Equals(_folder, BackupFolder, StringComparison.OrdinalIgnoreCase)) return;
                if (Directory.GetFiles(_folder, "*.json").Length > 0 || !Directory.Exists(BackupFolder)) return;
                var files = Directory.GetFiles(BackupFolder).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                                                        f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)).ToList();
                if (files.Count == 0) return;
                foreach (var f in files) File.Copy(f, Path.Combine(_folder, Path.GetFileName(f)), false);
                Logger.Log($"[TemplateLibrary] Thư mục add-in mới chưa có thư viện mẫu -> chép {files.Count} file từ bản dự phòng '{BackupFolder}'");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TemplateLibrary] Chép thư viện mẫu từ bản dự phòng thất bại");
            }
        }

        private static void CopyToBackup(string file)
        {
            try
            {
                if (string.Equals(Path.GetDirectoryName(file), BackupFolder, StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(BackupFolder);
                File.Copy(file, Path.Combine(BackupFolder, Path.GetFileName(file)), true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[TemplateLibrary] Không chép được bản dự phòng '{file}': {ex.Message}");
            }
        }

        public static string JsonPath(string setName) => Path.Combine(Folder, setName + ".json");
        public static string DwgPath(string setName) => Path.Combine(Folder, setName + ".dwg");

        public static List<string> ListSets()
        {
            var sets = Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (sets.Count == 0) sets.Add(DefaultSetName);
            return sets;
        }

        public static TemplateLibrary Load(string setName)
        {
            if (string.IsNullOrWhiteSpace(setName)) setName = DefaultSetName;
            string path = JsonPath(setName);
            // v9.4: file hỏng thì đọc bản dự phòng <bộ>.json.bak (ghi an toàn giữ lại bản trước)
            TemplateLibrary lib = FileHelper.ReadWithBackup(path, JsonHelper.Deserialize<TemplateLibrary>, $"[TemplateLibrary] bộ '{setName}'");

            // DataContractJsonSerializer không chạy property initializer -> bù giá trị mặc định
            lib ??= new TemplateLibrary();
            lib.Name = setName;
            lib.Entries ??= new List<TemplateEntry>();
            lib.Entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.BlockName));
            foreach (var e in lib.Entries)
            {
                e.VisibilityState ??= "";
                if (string.IsNullOrWhiteSpace(e.DisplayName)) e.DisplayName = e.BlockName;
                if (string.IsNullOrWhiteSpace(e.Unit)) e.Unit = "Cái";
                e.Note ??= "";
            }
            return lib;
        }

        /// <summary>
        /// Xoá cả bộ mẫu: file .json + .dwg ở thư mục add-in VÀ bản dự phòng APPDATA (không thì lần sau
        /// MigrateFromBackupIfEmpty chép bộ đã xoá quay lại). Trả số file đã xoá.
        /// </summary>
        public static int DeleteSet(string setName)
        {
            int deleted = 0;
            foreach (var dir in new[] { Folder, BackupFolder }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // .json.bak: bản dự phòng của ghi an toàn (v9.4) - không xoá thì Load đọc lại bộ đã xoá
                foreach (var ext in new[] { ".json", ".dwg", ".json.bak" })
                {
                    string path = Path.Combine(dir, setName + ext);
                    if (!File.Exists(path)) continue;
                    File.Delete(path);
                    deleted++;
                    Logger.Log($"[TemplateLibrary] Xoá bộ '{setName}': đã xoá '{path}'");
                }
            }
            return deleted;
        }

        public static void Save(TemplateLibrary lib)
        {
            string path = JsonPath(lib.Name);
            // v9.4: ghi file tạm rồi thay (giữ .bak) -> mất điện / CAD crash giữa lúc ghi không làm hỏng bộ mẫu
            FileHelper.WriteAllTextAtomic(path, JsonHelper.Serialize(lib));
            CopyToBackup(path);
            Logger.Log($"[TemplateLibrary] Lưu bộ '{lib.Name}': {lib.Entries.Count} block mẫu -> '{path}'");
        }

        /// <summary>Có lọc "chỉ quét block mẫu" không: ô tick bật và bộ mẫu có ít nhất 1 block.</summary>
        public static bool IsFilterActive(TemplateLibrary lib) =>
            !SettingsManager.Current.ScanAllBlocks && lib != null && lib.Entries != null && lib.Entries.Count > 0;

        /// <summary>
        /// Block mẫu khớp: cùng tên block; ưu tiên cùng chủng loại, sau đó mẫu không ghi chủng loại (mọi chủng loại).
        /// v9.4: block trong XREF tên "XREF|TÊN" -> không khớp tên đầy đủ thì thử lại với "TÊN".
        /// </summary>
        public static TemplateEntry Match(TemplateLibrary lib, string blockName, string visibility)
        {
            if (lib?.Entries == null || string.IsNullOrEmpty(blockName)) return null;
            var e = MatchName(lib, blockName, visibility);
            if (e != null) return e;
            string bare = StripXrefPrefix(blockName);
            return bare.Length != blockName.Length ? MatchName(lib, bare, visibility) : null;
        }

        private static TemplateEntry MatchName(TemplateLibrary lib, string blockName, string visibility)
        {
            var sameName = lib.Entries.Where(e => string.Equals(e.BlockName, blockName, StringComparison.OrdinalIgnoreCase)).ToList();
            return sameName.FirstOrDefault(e => !string.IsNullOrEmpty(e.VisibilityState) &&
                                                string.Equals(e.VisibilityState, visibility ?? "", StringComparison.OrdinalIgnoreCase))
                   ?? sameName.FirstOrDefault(e => string.IsNullOrEmpty(e.VisibilityState));
        }

        /// <summary>
        /// Tên block trong XREF (chưa bind) có dạng "TÊN_XREF|TÊN_BLOCK" -> "TÊN_BLOCK". Ký tự '|' không được dùng trong tên
        /// block thường nên chỉ gặp ở block phụ thuộc XREF.
        /// </summary>
        public static string StripXrefPrefix(string blockName)
        {
            if (string.IsNullOrEmpty(blockName)) return blockName ?? "";
            int i = blockName.LastIndexOf('|');
            return i >= 0 && i < blockName.Length - 1 ? blockName.Substring(i + 1) : blockName;
        }

        /// <summary>
        /// Đặt tên thống kê / đơn vị / ghi chú theo block mẫu, xếp theo thứ tự thư viện (block mẫu lên trước).
        /// onlyTemplate: bỏ dòng không phải block mẫu (block con không phải mẫu nằm trong block cha được chọn).
        /// </summary>
        public static List<BlockItem> Apply(List<BlockItem> items, TemplateLibrary lib, bool onlyTemplate)
        {
            if (items == null || lib?.Entries == null || lib.Entries.Count == 0) return items;

            var matched = new List<(BlockItem Item, int Index)>();
            var others = new List<BlockItem>();
            var removed = new List<string>();
            foreach (var item in items)
            {
                var e = Match(lib, item.BlockName, item.VisibilityState);
                if (e != null)
                {
                    item.DisplayName = string.IsNullOrWhiteSpace(e.DisplayName) ? item.BlockName : e.DisplayName;
                    if (!string.IsNullOrWhiteSpace(e.Unit)) item.Unit = e.Unit;
                    if (!string.IsNullOrWhiteSpace(e.Note)) item.Note = e.Note;
                    item.IsMatchedByLibrary = true;
                    matched.Add((item, lib.Entries.IndexOf(e)));
                }
                else if (onlyTemplate)
                {
                    removed.Add($"{item.BlockName}" + (string.IsNullOrEmpty(item.VisibilityState) ? "" : $" [{item.VisibilityState}]") + $" x{item.Count}");
                }
                else
                {
                    others.Add(item);
                }
            }

            var result = matched.OrderBy(m => m.Index).Select(m => m.Item).Concat(others.OrderBy(i => i.Order)).ToList();
            for (int i = 0; i < result.Count; i++) result[i].Order = i;
            Logger.Log($"[TemplateLibrary.Apply] bộ '{lib.Name}': {matched.Count} dòng khớp block mẫu, {others.Count} dòng khác giữ lại" +
                       (removed.Count > 0 ? $", bỏ {removed.Count} dòng không phải block mẫu [{string.Join(", ", removed)}]" : ""));
            return result;
        }

        // ============================== FILE DWG ĐỊNH NGHĨA BLOCK ==============================

        /// <summary>
        /// Chép định nghĩa block (BTR gốc, dynamic block kèm đủ chủng loại) từ bản vẽ đang mở vào &lt;bộ&gt;.dwg.
        /// Block trùng tên trong file được thay bằng bản mới. Trả về số block đã chép.
        /// </summary>
        public static int SaveBlockDefinitions(Document doc, string setName, IEnumerable<ObjectId> btrIds)
        {
            var ids = new ObjectIdCollection();
            foreach (var id in btrIds.Distinct())
                if (!id.IsNull && id.IsValid && !id.IsErased && id.Database == doc.Database) ids.Add(id);
            if (ids.Count == 0) return 0;

            string path = DwgPath(setName);
            bool exists = File.Exists(path);
            using (doc.LockDocument())
            using (var dest = new Database(!exists, true))
            {
                if (exists)
                {
                    dest.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                    dest.CloseInput(true);
                }
                var mapping = new IdMapping();
                doc.Database.WblockCloneObjects(ids, dest.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                dest.SaveAs(path, DwgVersion.Current);
            }
            CopyToBackup(path);
            Logger.Log($"[TemplateLibrary] Chép {ids.Count} định nghĩa block vào '{path}' (file {(exists ? "đã có, cập nhật" : "mới")})");
            return ids.Count;
        }

        /// <summary>Tên các block có trong &lt;bộ&gt;.dwg (rỗng nếu chưa có file).</summary>
        public static HashSet<string> ListDwgBlocks(string setName)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = DwgPath(setName);
            if (!File.Exists(path)) return names;
            try
            {
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId id in bt)
                        {
                            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                            if (!btr.IsLayout && !btr.IsAnonymous) names.Add(btr.Name);
                        }
                        tr.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TemplateLibrary] Đọc '{path}' thất bại");
            }
            return names;
        }

        /// <summary>
        /// Lấy định nghĩa block cho bản vẽ đang mở: bản vẽ đã có thì dùng luôn, chưa có thì chép từ &lt;bộ&gt;.dwg.
        /// Gọi trong LockDocument. Trả ObjectId.Null nếu không tìm thấy.
        /// </summary>
        public static ObjectId ImportBlock(Document doc, string setName, string blockName)
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName))
                {
                    var id = bt[blockName];
                    tr.Commit();
                    Logger.Log($"[TemplateLibrary.ImportBlock] '{blockName}' đã có trong bản vẽ -> dùng định nghĩa sẵn có");
                    return id;
                }
                tr.Commit();
            }

            string path = DwgPath(setName);
            if (!File.Exists(path))
            {
                Logger.Warn($"[TemplateLibrary.ImportBlock] '{blockName}': bản vẽ chưa có và chưa có file '{path}'");
                return ObjectId.Null;
            }

            using (var src = new Database(false, true))
            {
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                ObjectId srcId;
                using (var tr = src.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(src.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(blockName))
                    {
                        Logger.Warn($"[TemplateLibrary.ImportBlock] '{blockName}' không có trong '{path}'");
                        return ObjectId.Null;
                    }
                    srcId = bt[blockName];
                    tr.Commit();
                }
                var mapping = new IdMapping();
                src.WblockCloneObjects(new ObjectIdCollection(new[] { srcId }), db.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
                var pair = mapping[srcId];
                Logger.Log($"[TemplateLibrary.ImportBlock] Chép '{blockName}' từ '{path}' vào bản vẽ: {(pair.IsCloned ? "OK" : "KHÔNG chép được")}");
                return pair.IsCloned ? pair.Value : ObjectId.Null;
            }
        }

        /// <summary>Chèn 1 block mẫu vào không gian hiện hành: tỉ lệ lúc thêm vào thư viện, đúng chủng loại, có thuộc tính.
        /// Gọi trong LockDocument.</summary>
        public static void InsertBlock(Document doc, ObjectId btrId, TemplateEntry entry, Point3d ptWcs)
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                double s = entry.InsertScale > 0 ? entry.InsertScale : 1.0;

                var br = new BlockReference(ptWcs, btrId);
                br.SetDatabaseDefaults(db);
                br.ScaleFactors = new Scale3d(s);
                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                int atts = 0;
                if (btr.HasAttributeDefinitions)
                {
                    foreach (ObjectId id in btr)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                        var ar = new AttributeReference();
                        ar.SetAttributeFromBlock(ad, br.BlockTransform);
                        ar.TextString = ad.TextString;
                        br.AttributeCollection.AppendAttribute(ar);
                        tr.AddNewlyCreatedDBObject(ar, true);
                        atts++;
                    }
                }

                string visResult = "";
                if (!string.IsNullOrEmpty(entry.VisibilityState) && br.IsDynamicBlock)
                {
                    visResult = "không có tham số Visibility";
                    foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                    {
                        if (p.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) < 0 || p.ReadOnly) continue;
                        try
                        {
                            p.Value = entry.VisibilityState;
                            visResult = "đặt chủng loại OK";
                        }
                        catch (Exception ex)
                        {
                            visResult = $"đặt chủng loại lỗi: {ex.Message}";
                        }
                        break;
                    }
                }
                tr.Commit();
                Logger.Log($"[TemplateLibrary.InsertBlock] Chèn '{entry.BlockName}' ({entry.VisibilityState}) tại {ptWcs}, tỉ lệ {s}, {atts} thuộc tính" +
                           (visResult.Length > 0 ? ", " + visResult : ""));
            }
        }
    }

    /// <summary>
    /// Lọc lúc quét chọn (Editor.SelectionAdded): chỉ giữ block mẫu, hoặc block cha / ARRAY / MINSERT có block mẫu bên
    /// trong (mọi tầng - khớp cách quét ở chế độ block mẫu) để block mẫu lồng vẫn được đếm. Đối tượng khác bị bỏ khỏi
    /// vùng chọn ngay, không sáng lên. v9.4: bỏ block con đang ẩn theo visibility, bỏ bảng, XREF chỉ xét khi bật
    /// "Đếm trong XREF", khớp tên block trong XREF bỏ tiền tố "XREF|".
    /// </summary>
    public sealed class TemplateSelectionFilter
    {
        private readonly Database _db;
        private readonly TemplateLibrary _lib;
        private readonly bool _countXrefs;
        private readonly HashSet<string> _names;
        private readonly Dictionary<ObjectId, bool> _containsCache = new Dictionary<ObjectId, bool>();

        public int Kept { get; private set; }
        public int KeptAsParent { get; private set; }
        public int Removed { get; private set; }

        public TemplateSelectionFilter(Database db, TemplateLibrary lib, bool countXrefs)
        {
            _db = db;
            _lib = lib;
            _countXrefs = countXrefs;
            _names = new HashSet<string>(lib.Entries.Select(e => e.BlockName), StringComparer.OrdinalIgnoreCase);
        }

        public void OnSelectionAdded(object sender, SelectionAddedEventArgs e)
        {
            try
            {
                using (var tr = _db.TransactionManager.StartOpenCloseTransaction())
                {
                    for (int i = e.AddedObjects.Count - 1; i >= 0; i--)
                    {
                        var so = e.AddedObjects[i];
                        if (so == null) continue;
                        var id = so.ObjectId;
                        if (id.IsNull || !id.IsValid || id.IsErased) continue;
                        bool keep = false, parent = false;
                        if (id.ObjectClass.IsDerivedFrom(BlockExtractor.BlockRefClass) && tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                            keep = Keep(tr, br, out parent);
                        if (!keep)
                        {
                            e.Remove(i);
                            Removed++;
                        }
                        else
                        {
                            Kept++;
                            if (parent) KeptAsParent++;
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                // Lỗi lọc không được làm hỏng lệnh chọn: phần chưa xét giữ nguyên, sau khi quét vẫn lọc lại theo block mẫu
                Logger.Error(ex, "[TemplateSelectionFilter] lỗi khi lọc vùng chọn");
            }
        }

        /// <summary>Giữ block này: là block mẫu, hoặc bên trong (mọi tầng) có block mẫu. parent = giữ vì chứa block mẫu.</summary>
        private bool Keep(Transaction tr, BlockReference br, out bool parent)
        {
            parent = false;
            if (!br.Visible) return false;
            var kind = BlockExtractor.Classify(tr, br, out var def);
            if (kind == BlockExtractor.RefKind.Table) return false;
            if (kind == BlockExtractor.RefKind.Xref && !_countXrefs) return false;
            if (kind == BlockExtractor.RefKind.Block && IsTemplate(br, def.Name)) return true;
            parent = ContainsTemplate(tr, br.BlockTableRecord);
            return parent;
        }

        private bool IsTemplate(BlockReference br, string name)
        {
            if (!_names.Contains(name) && !_names.Contains(TemplateLibraryManager.StripXrefPrefix(name))) return false;
            return TemplateLibraryManager.Match(_lib, name, BlockExtractor.ReadVisibility(br, name)) != null;
        }

        /// <summary>Định nghĩa block (BTR của instance) có block mẫu ở bất kỳ tầng nào - nhớ theo BTR.</summary>
        private bool ContainsTemplate(Transaction tr, ObjectId btrId)
        {
            if (_containsCache.TryGetValue(btrId, out bool cached)) return cached;
            _containsCache[btrId] = false; // chống vòng lặp (bản vẽ lỗi có block tự chứa chính nó)

            bool found = false;
            if (tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr && !btr.IsLayout)
            {
                foreach (ObjectId id in btr)
                {
                    // Lọc theo loại trước khi mở: định nghĩa block cha có nhiều nét, ít block con
                    if (!id.ObjectClass.IsDerivedFrom(BlockExtractor.BlockRefClass)) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference child)) continue;
                    if (Keep(tr, child, out _))
                    {
                        found = true;
                        break;
                    }
                }
            }
            _containsCache[btrId] = found;
            return found;
        }
    }
}

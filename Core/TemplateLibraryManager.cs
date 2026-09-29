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
                return _folder;
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
            TemplateLibrary lib = null;
            try
            {
                if (File.Exists(path)) lib = JsonHelper.Deserialize<TemplateLibrary>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TemplateLibrary] Đọc '{path}' thất bại");
            }

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
                foreach (var ext in new[] { ".json", ".dwg" })
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
            File.WriteAllText(path, JsonHelper.Serialize(lib), System.Text.Encoding.UTF8);
            CopyToBackup(path);
            Logger.Log($"[TemplateLibrary] Lưu bộ '{lib.Name}': {lib.Entries.Count} block mẫu -> '{path}'");
        }

        /// <summary>Có lọc "chỉ quét block mẫu" không: ô tick bật và bộ mẫu có ít nhất 1 block.</summary>
        public static bool IsFilterActive(TemplateLibrary lib) =>
            !SettingsManager.Current.ScanAllBlocks && lib != null && lib.Entries != null && lib.Entries.Count > 0;

        /// <summary>Block mẫu khớp: cùng tên block; ưu tiên cùng chủng loại, sau đó mẫu không ghi chủng loại (mọi chủng loại).</summary>
        public static TemplateEntry Match(TemplateLibrary lib, string blockName, string visibility)
        {
            if (lib?.Entries == null || string.IsNullOrEmpty(blockName)) return null;
            var sameName = lib.Entries.Where(e => string.Equals(e.BlockName, blockName, StringComparison.OrdinalIgnoreCase)).ToList();
            return sameName.FirstOrDefault(e => !string.IsNullOrEmpty(e.VisibilityState) &&
                                                string.Equals(e.VisibilityState, visibility ?? "", StringComparison.OrdinalIgnoreCase))
                   ?? sameName.FirstOrDefault(e => string.IsNullOrEmpty(e.VisibilityState));
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
    /// Lọc lúc quét chọn (Editor.SelectionAdded): chỉ giữ block mẫu, hoặc block cha có block mẫu bên trong (trong độ sâu
    /// quét) để block mẫu lồng vẫn được đếm. Đối tượng khác bị bỏ khỏi vùng chọn ngay, không sáng lên.
    /// </summary>
    public sealed class TemplateSelectionFilter
    {
        private readonly Database _db;
        private readonly TemplateLibrary _lib;
        private readonly int _maxDepth;
        private readonly HashSet<string> _names;
        private readonly Dictionary<(ObjectId, int), bool> _containsCache = new Dictionary<(ObjectId, int), bool>();

        public int Kept { get; private set; }
        public int KeptAsParent { get; private set; }
        public int Removed { get; private set; }

        public TemplateSelectionFilter(Database db, TemplateLibrary lib, int maxDepth)
        {
            _db = db;
            _lib = lib;
            _maxDepth = maxDepth;
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
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                        {
                            keep = IsTemplate(tr, br);
                            if (!keep && _maxDepth > 1) keep = parent = ContainsTemplate(tr, br.BlockTableRecord, 1);
                        }
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

        private bool IsTemplate(Transaction tr, BlockReference br)
        {
            var def = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
            if (!_names.Contains(def.Name)) return false;
            return TemplateLibraryManager.Match(_lib, def.Name, BlockExtractor.ReadVisibility(br, def.Name)) != null;
        }

        private bool ContainsTemplate(Transaction tr, ObjectId btrId, int childDepth)
        {
            var key = (btrId, childDepth);
            if (_containsCache.TryGetValue(key, out bool cached)) return cached;
            _containsCache[key] = false;

            bool found = false;
            if (tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr && !btr.IsLayout)
            {
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference child)) continue;
                    if (IsTemplate(tr, child) ||
                        (childDepth + 1 < _maxDepth && ContainsTemplate(tr, child.BlockTableRecord, childDepth + 1)))
                    {
                        found = true;
                        break;
                    }
                }
            }
            _containsCache[key] = found;
            return found;
        }
    }
}

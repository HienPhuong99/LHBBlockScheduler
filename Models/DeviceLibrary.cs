using System.Collections.Generic;

namespace LHBBlockScheduler.Models
{
    /// <summary>
    /// Bản ghi của Thư viện thiết bị CŨ (trước v9.1). Chỉ còn dùng để đọc file cũ và chuyển sang thư viện block mẫu
    /// (TemplateLibraryManager.MigrateDeviceLibraries).
    /// </summary>
    public class DeviceLibraryEntry
    {
        public string StandardName { get; set; }
        public string VisibilityState { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
        public int Order { get; set; }
        public ulong ShapeHash { get; set; }
        public List<string> KnownBlockNames { get; set; }
        public string ThumbnailBase64 { get; set; }
    }

    /// <summary>Thư viện thiết bị CŨ, file %APPDATA%\LHBBlockScheduler\Libraries\&lt;tên&gt;.json.</summary>
    public class DeviceLibrary
    {
        public string Name { get; set; }
        public List<DeviceLibraryEntry> Entries { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace LHBBlockScheduler.Models
{
    /// <summary>
    /// Bản ghi thiết bị trong Thư viện thiết bị (Device Library).
    /// </summary>
    public class DeviceLibraryEntry
    {
        public string StandardName { get; set; }
        public string VisibilityState { get; set; }
        public string Unit { get; set; } = "Cái";
        public string Note { get; set; }
        public int Order { get; set; }
        public ulong ShapeHash { get; set; }
        public List<string> KnownBlockNames { get; set; } = new List<string>();
        public string ThumbnailBase64 { get; set; }
    }

    /// <summary>
    /// Thư viện thiết bị chuẩn hoá, lưu ở %APPDATA%\LHBBlockScheduler\Libraries\<tên>.json
    /// </summary>
    public class DeviceLibrary
    {
        public string Name { get; set; } = "default";
        public List<DeviceLibraryEntry> Entries { get; set; } = new List<DeviceLibraryEntry>();
    }
}

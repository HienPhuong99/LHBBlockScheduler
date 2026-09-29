using System.Collections.Generic;

namespace LHBBlockScheduler.Models
{
    /// <summary>
    /// 1 block mẫu trong thư viện block mẫu. Khớp với block trên bản vẽ theo tên block (+ chủng loại nếu có).
    /// </summary>
    public class TemplateEntry
    {
        /// <summary>Tên block gốc (DynamicBlockTableRecord).</summary>
        public string BlockName { get; set; }

        /// <summary>Chủng loại (visibility state). Rỗng = mọi chủng loại của block này.</summary>
        public string VisibilityState { get; set; }

        /// <summary>Tên thống kê: tên hiện trên form và bảng xuất.</summary>
        public string DisplayName { get; set; }

        /// <summary>Tĩnh / Động / Có thuộc tính.</summary>
        public string BlockKind { get; set; }

        public string Unit { get; set; }
        public string Note { get; set; }

        /// <summary>Ảnh ký hiệu PNG (base64) - mang sang máy khác vẫn thấy hình.</summary>
        public string ThumbnailBase64 { get; set; }

        /// <summary>Tỉ lệ của block lúc thêm vào thư viện - dùng khi chèn block mẫu vào bản vẽ. 0 = 1.</summary>
        public double InsertScale { get; set; }
    }

    /// <summary>
    /// Bộ block mẫu, lưu cạnh add-in: &lt;thư mục DLL&gt;\ThuVienMau\&lt;tên&gt;.json (thông tin) + &lt;tên&gt;.dwg (định nghĩa block).
    /// Thứ tự Entries = thứ tự dòng trên form thống kê và bảng xuất.
    /// </summary>
    public class TemplateLibrary
    {
        public string Name { get; set; }
        public List<TemplateEntry> Entries { get; set; } = new List<TemplateEntry>();
    }
}

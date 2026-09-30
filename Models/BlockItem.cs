using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.AutoCAD.DatabaseServices;

namespace LHBBlockScheduler.Models
{
    /// <summary>Căn lề ngang của 1 ô (như Excel), dùng chung cho grid và bảng xuất CAD.</summary>
    public enum CellHAlign { Center, Left, Right }

    /// <summary>
    /// 1 dòng trong bảng thống kê. Implement INotifyPropertyChanged để
    /// DataGridView tự refresh khi sửa DisplayName trực tiếp trên UI.
    /// </summary>
    public class BlockItem : INotifyPropertyChanged
    {
        private string _displayName;
        private int _order;

        /// <summary>Tên gốc của Block trong AutoCAD (hoặc chuỗi ghép nếu là dòng đã merge).</summary>
        public string BlockName { get; set; }

        /// <summary>Tên hiển thị - alias do người dùng đặt, chỉ tồn tại trong bộ nhớ / template JSON.</summary>
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        public int Count { get; set; }

        /// <summary>Thứ tự hiển thị / STT khi xuất bảng.</summary>
        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(nameof(Order)); }
        }

        /// <summary>ObjectId của từng Block Reference thuộc dòng này - dùng để Zoom/Highlight.
        /// KHÔNG serialize ra JSON (ObjectId chỉ hợp lệ trong phiên hiện tại).</summary>
        public List<ObjectId> ObjectIds { get; set; } = new List<ObjectId>();

        /// <summary>Đường dẫn file PNG thumbnail đã cache (rỗng nếu chưa generate).</summary>
        public string ThumbnailPath { get; set; }

        /// <summary>Ảnh do user gán hoặc chụp, ưu tiên hơn ThumbnailPath.</summary>
        public string CustomImagePath { get; set; }

        /// <summary>Đơn vị tính - mặc định "Cái".</summary>
        public string Unit { get; set; } = "Cái";

        /// <summary>Ghi chú của dòng thiết bị.</summary>
        public string Note { get; set; }

        /// <summary>Phân loại block: "Tĩnh" | "Động" | "Có thuộc tính".</summary>
        public string BlockKind { get; set; } = "Tĩnh";

        /// <summary>Chủng loại (giá trị visibility state của dynamic block).</summary>
        public string VisibilityState { get; set; }

        /// <summary>
        /// v9.6: kích thước theo tham số độ dài của block động ("12320", "1200 x 600") - cột "Kích thước" khi bật
        /// "Tách theo kích thước". Rỗng = block không có tham số độ dài.
        /// </summary>
        public string Size { get; set; } = "";

        /// <summary>
        /// v9.6: tên tham số dạng số của block động không có Visibility (null nếu không có) - khớp block mẫu lưu từ bản cũ
        /// (chủng loại dạng "Distance1=47116.93"). Chỉ trong bộ nhớ.
        /// </summary>
        public ICollection<string> NumericParamNames { get; set; }

        /// <summary>v9.6: block động (ô ký hiệu chép hình từ block thật trên bản vẽ như block có chủng loại).</summary>
        public bool IsDynamic { get; set; }

        /// <summary>Đã được nhận diện/khớp từ thư viện thiết bị chưa (chấm xanh/đỏ).</summary>
        public bool IsMatchedByLibrary { get; set; }

        /// <summary>BlockTableRecord cho hiển thị ảnh & xuất bảng (Instance BTR ẩn danh nếu tách theo visibility).</summary>
        public ObjectId SourceBtrId { get; set; }

        /// <summary>BlockTableRecord gốc của definition (dùng để tính ShapeHash bất biến).</summary>
        public ObjectId DynamicBtrId { get; set; }

        /// <summary>Đường dẫn ObjectId các block cha đối với block lồng.</summary>
        public ObjectId[] ContainerPath { get; set; }

        /// <summary>Độ sâu lồng: 0 = Model Space trực tiếp, 1, 2, ...</summary>
        public int NestDepth { get; set; }

        /// <summary>Mã băm hình dạng 64-bit (dHash) tính từ thumbnail chuẩn hoá.</summary>
        public ulong ShapeHash { get; set; }

        /// <summary>Layer của instance đầu tiên trong nhóm.</summary>
        public string LayerName { get; set; }

        /// <summary>Toàn bộ các layer khác nhau của các block trong nhóm.</summary>
        public string[] AllLayers { get; set; }

        public bool IsMergedGroup { get; set; }

        /// <summary>Nếu là dòng đã gộp, lưu lại các BlockName gốc đã gộp vào đây.</summary>
        public List<string> MergedSourceNames { get; set; } = new List<string>();

        /// <summary>Căn lề ngang từng ô do user chọn (key = tên cột: colDisplayName, colCount...).
        /// Ô không có trong từ điển = căn giữa. Chỉ tồn tại trong bộ nhớ như các chỉnh sửa khác trên form.</summary>
        public Dictionary<string, CellHAlign> CellAlignments { get; set; } = new Dictionary<string, CellHAlign>();

        /// <summary>Từng block reference thuộc dòng này kèm vị trí WCS - để tìm block trùng. KHÔNG serialize.</summary>
        public List<BlockInstanceRef> Instances { get; set; } = new List<BlockInstanceRef>();

        /// <summary>Các vị trí có block của dòng này chồng lên block cùng tên (DuplicateFinder.Detect tính).
        /// 1 nhóm có thể dùng chung cho nhiều dòng (khác chủng loại).</summary>
        public List<DuplicateGroup> DuplicateGroups { get; set; } = new List<DuplicateGroup>();

        /// <summary>Số block thừa CỦA DÒNG NÀY do trùng (block giữ lại của nhóm có thể thuộc dòng khác).</summary>
        public int DuplicateExtra
        {
            get
            {
                int extra = 0;
                if (DuplicateGroups != null) foreach (var g in DuplicateGroups) extra += g.ExtraFor(this);
                return extra;
            }
        }

        /// <summary>true = Count hiện đang trừ DuplicateExtra ("Không đếm block trùng").</summary>
        public bool DuplicatesExcluded { get; set; }

        /// <summary>
        /// Giá trị cột thêm (Premium): "zone:&lt;khu vực&gt;" = SL trong khu vực, "attr:&lt;khoá&gt;" = giá trị thuộc tính.
        /// Form tính lại mỗi lần quét / đổi khu vực; bảng xuất, Excel đọc qua TableExporter.GetItemTextForColumn.
        /// </summary>
        public Dictionary<string, string> ExtraValues { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public CellHAlign GetAlignment(string columnKey) =>
            CellAlignments != null && CellAlignments.TryGetValue(columnKey, out var align) ? align : CellHAlign.Center;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

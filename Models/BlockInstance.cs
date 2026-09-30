using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace LHBBlockScheduler.Models
{
    /// <summary>Block nằm trong vỏ nào (v9.4) - hiện ở cột Nguồn, có thể kết hợp (ARRAY trong XREF...).</summary>
    [System.Flags]
    public enum RefVia
    {
        None = 0,
        /// <summary>ARRAY liên kết (lệnh ARRAY).</summary>
        Array = 1,
        /// <summary>Phần tử của MINSERT (hoặc block con nằm trong phần tử MINSERT).</summary>
        MInsert = 2,
        /// <summary>Block của bản vẽ XREF (chỉ khi bật "Đếm trong XREF").</summary>
        Xref = 4,
        /// <summary>Block ẩn danh *U khác (không phải ARRAY, không phải dynamic block).</summary>
        Anonymous = 8
    }

    /// <summary>
    /// 1 block reference cụ thể trên bản vẽ (kể cả block lồng) - dùng để tìm block trùng vị trí.
    /// Chỉ tồn tại trong bộ nhớ, KHÔNG serialize (có ObjectId).
    /// </summary>
    public class BlockInstanceRef
    {
        /// <summary>Đường dẫn ObjectId từ block ngoài cùng tới chính block này (Length = 1: nằm trực tiếp trong Model).</summary>
        public ObjectId[] Path { get; set; }

        /// <summary>Điểm chèn theo WCS.</summary>
        public Point3d Position { get; set; }

        /// <summary>Ma trận từ toạ độ chủ sở hữu của block này ra WCS (Identity nếu nằm trực tiếp trong Model).</summary>
        public Matrix3d OwnerTransform { get; set; }

        /// <summary>Khoá so trùng cũ: tên block + chủng loại (giữ để log).</summary>
        public string Key { get; set; }

        /// <summary>Tên block gốc (DynamicBlockTableRecord). Block cùng tên mới so trùng, khác chủng loại vẫn so.</summary>
        public string BlockName { get; set; }

        /// <summary>
        /// 4 góc khung bao của block theo WCS (khung bao trong toạ độ block, biến đổi theo góc xoay / tỉ lệ / block cha)
        /// -> khung xoay theo block, không phình to như khung bao trục X/Y. Null nếu không tính được extents.
        /// </summary>
        public Point2d[] Corners { get; set; }

        /// <summary>Dòng đang chứa block này. DuplicateFinder.Detect gán lại mỗi lần tìm trùng (gộp dòng làm đổi dòng).</summary>
        public BlockItem Item { get; set; }

        /// <summary>Khoá gom dòng lúc quét (tên + chủng loại + layer + thuộc tính tách dòng) - bảng tự cập nhật (Premium) dùng.</summary>
        public string GroupKey { get; set; }

        public string Layer { get; set; }

        /// <summary>Tỉ lệ X, Y theo WCS (tính cả block cha) - soát lỗi block lật / sai tỉ lệ (Premium).</summary>
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;

        /// <summary>Góc xoay theo WCS (radian).</summary>
        public double Rotation { get; set; }

        /// <summary>Thuộc tính (khoá "A:TAG") và tham số dynamic block (khoá "D:Tên") của block này.</summary>
        public Dictionary<string, string> Attributes { get; set; }

        /// <summary>v9.4: block nằm trong ARRAY / MINSERT / XREF.</summary>
        public RefVia Via { get; set; }

        /// <summary>
        /// v9.4: 1 phần tử của MINSERT. Mọi phần tử dùng chung 1 đối tượng (Path[0]) -> không xoá / thay / ghi thuộc tính
        /// riêng từng phần tử được (xoá 1 phần tử = xoá cả MINSERT).
        /// </summary>
        public bool IsMInsertElement { get; set; }

        /// <summary>Nằm trực tiếp trong Model và là 1 đối tượng riêng (xoá / thay / ghi thuộc tính được). Phần tử MINSERT: false.</summary>
        public bool IsTopLevel => Path != null && Path.Length == 1 && !IsMInsertElement;

        /// <summary>Nhóm trùng chứa block này (null = không trùng). DuplicateFinder.Detect gán lại mỗi lần tìm trùng.</summary>
        public DuplicateGroup Group { get; set; }

        /// <summary>Block này là bản thừa của 1 nhóm trùng đang bị trừ khỏi SL (không đếm vào khu vực / cập nhật).</summary>
        public bool IsExcludedDuplicate => Item != null && Item.DuplicatesExcluded && Group != null && Group.Keep != this;
    }

    /// <summary>
    /// Nhóm block cùng tên nằm chồng lên nhau: cùng điểm chèn (trong sai số) hoặc khung bao che lấp nhau.
    /// Có thể gồm block của nhiều dòng (khác chủng loại). Giữ lại 1 block (Keep), còn lại là bản thừa.
    /// </summary>
    public class DuplicateGroup
    {
        public string BlockName { get; set; }

        /// <summary>Tâm vùng trùng (tâm khung bao chung) theo WCS - để khoanh đỏ, zoom, kéo đường dẫn.</summary>
        public Point3d Position { get; set; }

        /// <summary>Nửa đường chéo khung bao chung - vòng khoanh đỏ phải bao hết các block chồng nhau.</summary>
        public double Radius { get; set; }

        public List<BlockInstanceRef> Instances { get; } = new List<BlockInstanceRef>();

        /// <summary>Block giữ lại khi xoá bản thừa (block lồng nếu có, không thì block tạo sớm nhất).</summary>
        public BlockInstanceRef Keep { get; set; }

        /// <summary>Có cặp nào trùng vì khung bao che lấp (không chỉ cùng điểm chèn).</summary>
        public bool HasOverlap { get; set; }

        /// <summary>Tỉ lệ che lấp lớn nhất trong nhóm (0..1, so với block nhỏ hơn).</summary>
        public double MaxOverlap { get; set; }

        /// <summary>Số block thừa (giữ lại 1).</summary>
        public int Extra => Instances.Count - 1;

        /// <summary>Số block thừa thuộc 1 dòng (block giữ lại không tính).</summary>
        public int ExtraFor(BlockItem item) => Instances.Count(i => i.Item == item && i != Keep);

        public IEnumerable<BlockItem> Items => Instances.Select(i => i.Item).Where(i => i != null).Distinct();
    }
}

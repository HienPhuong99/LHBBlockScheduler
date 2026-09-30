using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    public class ExtractionOptions
    {
        public int MaxDepth { get; set; } = 2;              // 1 = chỉ tầng ngoài, 2, 3, hoặc int.MaxValue
        public bool CountParentBlocks { get; set; } = false;// đếm cả block cha (cụm)
        public bool SplitByVisibility { get; set; } = true; // tách theo trạng thái visibility của dynamic block
        public bool SplitByLayer { get; set; } = false;     // tách nhóm theo layer (A.4)
        /// <summary>
        /// v9.6: tách dòng theo tham số độ dài của block động (Linear / Polar / XY: chiều dài đầu báo tia chiếu, rộng x cao
        /// tủ...), form hiện thêm cột Kích thước. Tắt = không tách (chủng loại không bao giờ chứa tham số số).
        /// </summary>
        public bool SplitBySize { get; set; }
        /// <summary>Tách dòng theo giá trị thuộc tính / tham số (khoá "A:TAG", "D:Tên") - Premium P5.</summary>
        public List<string> SplitAttributeKeys { get; set; } = new List<string>();

        /// <summary>
        /// v9.4: true = đếm cả block nằm trong XREF. Mặc định false = bỏ qua XREF và mọi thứ bên trong (XREF kiến trúc
        /// chứa cửa, nội thất... không phải thiết bị của bản vẽ này; trước v9.4 XREF bị đếm như 1 block thường).
        /// </summary>
        public bool CountXrefBlocks { get; set; }

        /// <summary>
        /// v9.4: bộ block mẫu đang lọc ("Chỉ quét block mẫu" bật và bộ có block), null = quét mọi block.
        /// Có bộ mẫu: đi sâu KHÔNG giới hạn tìm block mẫu; block mẫu là thiết bị (được đếm kể cả khi bên trong có
        /// block con, chỉ đi vào trong khi bật "Đếm cả block cha"); block khác chỉ là vỏ chứa, không ghi nhận.
        /// </summary>
        public TemplateLibrary TemplateFilter { get; set; }

        /// <summary>
        /// Chủng loại kiểu cũ (v9 - v9.5): block động không có Visibility ghép MỌI tham số đang hiện "Tên=Giá trị", kể cả
        /// độ dài / góc / toạ độ (vd "Distance1=12320.33"). Chỉ LHBCAPNHAT dùng cho bảng xuất từ bản cũ để khoá dòng
        /// lưu trong bảng vẫn khớp.
        /// </summary>
        internal bool LegacyVariant { get; set; }

        /// <summary>Số liệu lần quét gần nhất dùng options này (BlockExtractor điền sau khi quét).</summary>
        public ScanStats Stats { get; internal set; }

        public bool TemplateMode => TemplateFilter?.Entries != null && TemplateFilter.Entries.Count > 0;

        /// <summary>
        /// Tuỳ chọn quét theo settings (form lưu lại mỗi lần đổi): LHBSCAN, lệnh Premium chạy riêng, nhiều bản vẽ dùng
        /// chung 1 bộ tuỳ chọn với form (trước v9.4 LHBSCAN luôn dùng mặc định, bỏ qua cột thuộc tính tách dòng).
        /// </summary>
        public static ExtractionOptions FromSettings(AppSettings s, TemplateLibrary templateFilter = null) => new ExtractionOptions
        {
            MaxDepth = s.ScanDepth > 0 ? s.ScanDepth : 2,
            CountParentBlocks = s.CountParentBlocks,
            SplitByVisibility = s.SplitByVisibility,
            SplitByLayer = s.SplitByLayer,
            SplitBySize = s.SplitBySize,
            SplitAttributeKeys = (s.SplitAttributeKeys ?? new List<string>()).ToList(),
            CountXrefBlocks = s.CountXrefBlocks,
            TemplateFilter = templateFilter
        };

        public override string ToString() =>
            $"Depth={(MaxDepth == int.MaxValue ? "max" : MaxDepth.ToString())}, CountParents={CountParentBlocks}, SplitVis={SplitByVisibility}, " +
            $"SplitLay={SplitByLayer}, SplitSize={SplitBySize}, Xref={CountXrefBlocks}, BlockMau={(TemplateMode ? "'" + TemplateFilter.Name + "'" : "-")}" +
            (LegacyVariant ? ", ChungLoaiCu" : "");
    }

    /// <summary>
    /// Số liệu 1 lần quét (v9.4): ghi log + hiện dưới form để user thấy block trong ARRAY / MINSERT / XREF đã được
    /// xử lý thế nào và block nào bị cắt vì giới hạn độ sâu.
    /// </summary>
    public sealed class ScanStats
    {
        public int Roots { get; set; }
        public int Refs { get; set; }
        public int Arrays { get; set; }
        public int AnonymousBlocks { get; set; }
        public int ContainerRefs { get; set; }
        public int MInserts { get; set; }
        public int MInsertElements { get; set; }
        public int XrefsSkipped { get; set; }
        public int XrefsEntered { get; set; }
        public int XrefRefs { get; set; }
        public int Tables { get; set; }
        public int Hidden { get; set; }
        public int DepthLimited { get; set; }
        public int MaxDepth { get; set; }
        public bool TemplateMode { get; set; }
        public long ElapsedMs { get; set; }
        public HashSet<string> DepthLimitedNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SkippedXrefNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Block con nằm ngay dưới giới hạn độ sâu (không duyệt tới): tên -> số chỗ.</summary>
        public Dictionary<string, int> BeyondDepth { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Trong BeyondDepth, loại block ĐANG được đếm ở chỗ khác của vùng chọn -> gần như chắc chắn là thiết bị bị sót vì
        /// độ sâu (vd phòng mẫu -> tủ -> đèn). Chỉ cảnh báo loại này để không báo nhầm block con trang trí của thiết bị.
        /// </summary>
        public Dictionary<string, int> MissedByDepth { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cộng số liệu lần quét thêm vào lần quét trước (Quét thêm).</summary>
        public void Add(ScanStats o)
        {
            if (o == null) return;
            Roots += o.Roots; Refs += o.Refs; Arrays += o.Arrays; AnonymousBlocks += o.AnonymousBlocks; ContainerRefs += o.ContainerRefs;
            MInserts += o.MInserts; MInsertElements += o.MInsertElements; XrefsSkipped += o.XrefsSkipped; XrefsEntered += o.XrefsEntered;
            XrefRefs += o.XrefRefs; Tables += o.Tables; Hidden += o.Hidden; DepthLimited += o.DepthLimited; ElapsedMs += o.ElapsedMs;
            DepthLimitedNames.UnionWith(o.DepthLimitedNames);
            SkippedXrefNames.UnionWith(o.SkippedXrefNames);
            foreach (var kv in o.BeyondDepth) BeyondDepth[kv.Key] = (BeyondDepth.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            foreach (var kv in o.MissedByDepth) MissedByDepth[kv.Key] = (MissedByDepth.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
        }

        /// <summary>Câu tóm tắt 1 dòng cho thanh trạng thái form / dòng lệnh.</summary>
        public string Summary()
        {
            var parts = new List<string> { $"{Roots} đối tượng chọn", $"{Refs} block tìm thấy (gồm block cha)" };
            if (Arrays > 0) parts.Add($"{Arrays} ARRAY ({ContainerRefs} block bên trong)");
            else if (ContainerRefs > 0) parts.Add($"{ContainerRefs} block trong block ẩn danh");
            if (AnonymousBlocks > 0 && Arrays > 0) parts.Add($"{AnonymousBlocks} block ẩn danh khác");
            if (MInserts > 0) parts.Add($"{MInserts} MINSERT = {MInsertElements} phần tử");
            if (XrefsEntered > 0) parts.Add($"đếm trong {XrefsEntered} XREF ({XrefRefs} block)");
            if (XrefsSkipped > 0) parts.Add($"bỏ qua {XrefsSkipped} XREF");
            if (Tables > 0) parts.Add($"bỏ {Tables} bảng");
            if (Hidden > 0) parts.Add($"bỏ {Hidden} block ẩn (visibility)");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Cảnh báo chạm giới hạn độ sâu (null nếu không có): loại block đang được đếm nhưng còn nằm trong block khác sâu
        /// hơn độ sâu quét nên các chỗ đó chưa được đếm.
        /// </summary>
        public string DepthWarning()
        {
            if (MissedByDepth.Count == 0 || TemplateMode) return null;
            int total = MissedByDepth.Values.Sum();
            string names = string.Join(", ", MissedByDepth.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} x{kv.Value}")) +
                           (MissedByDepth.Count > 5 ? "..." : "");
            return $"{total} block ({names}) còn nằm trong block khác sâu hơn độ sâu quét {MaxDepth} nên CHƯA được đếm. " +
                   "Chọn 'Độ sâu quét' lớn hơn (3 / Không giới hạn) để đếm đủ.";
        }
    }

    public class ScannedRef
    {
        public ObjectId BrId { get; set; }
        public ObjectId DynamicBtrId { get; set; }
        public ObjectId InstanceBtrId { get; set; }
        public string BlockName { get; set; }
        public string BlockKind { get; set; }
        public string VisibilityState { get; set; }
        public string Layer { get; set; }
        public int Depth { get; set; }
        public ObjectId[] ContainerPath { get; set; }
        public Matrix3d Transform { get; set; }
        /// <summary>Ma trận toạ độ chủ sở hữu -> WCS (transform của các block cha).</summary>
        public Matrix3d OwnerTransform { get; set; }
        /// <summary>Điểm chèn theo WCS (tính cả block cha) - dùng tìm block trùng vị trí.</summary>
        public Point3d Position { get; set; }
        /// <summary>4 góc khung bao block theo WCS (null nếu không tính được) - dùng tìm block che lấp nhau.</summary>
        public Point2d[] Corners { get; set; }
        public bool HasChildren { get; set; }
        public string GroupKey { get; set; }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double Rotation { get; set; }
        /// <summary>Thuộc tính "A:TAG" + tham số dynamic "D:Tên" (null nếu block không có).</summary>
        public Dictionary<string, string> Attributes { get; set; }
        /// <summary>v9.4: block nằm trong ARRAY / MINSERT / XREF (có thể kết hợp).</summary>
        public RefVia Via { get; set; }
        /// <summary>v9.4: 1 phần tử của MINSERT (nhiều phần tử dùng chung 1 đối tượng).</summary>
        public bool IsMInsertElement { get; set; }
        /// <summary>v9.6: kích thước theo tham số độ dài của block động ("12320", "1200 x 600"), "" nếu không có.</summary>
        public string Size { get; set; } = "";
        /// <summary>
        /// v9.6: tên tham số dạng số của block động KHÔNG có Visibility (null nếu không có) - để khớp block mẫu lưu từ bản cũ
        /// có chủng loại dạng "Distance1=47116.93".
        /// </summary>
        public HashSet<string> NumericParamNames { get; set; }
        public bool IsDynamic { get; set; }
    }

    /// <summary>
    /// Tham số block động của 1 block reference, đọc 1 lần (v9.6).
    /// Chủng loại CHỈ lấy từ Visibility, không có Visibility thì từ tham số dạng chữ (Lookup, bảng thuộc tính...).
    /// Tham số dạng số (độ dài Linear / Polar / XY, góc Rotation, toạ độ Point, lật Flip...) không bao giờ là chủng loại:
    /// trước v9.6 đầu báo tia chiếu (chỉ có Linear "Distance1") ra chủng loại "Distance1=12320.3286822983", mỗi độ dài
    /// thành 1 dòng riêng (ảnh test 30/09/2026). Độ dài muốn tách thì dùng "Tách theo kích thước" (cột Kích thước).
    /// </summary>
    public sealed class DynamicInfo
    {
        public static readonly DynamicInfo Empty = new DynamicInfo();

        /// <summary>Chủng loại: trạng thái Visibility; không có thì các tham số dạng chữ "Tên=Giá trị" nối " - ".</summary>
        public string Variant { get; internal set; } = "";
        public bool HasVisibility { get; internal set; }
        /// <summary>Tham số độ dài (đơn vị Distance / Area, không phải toạ độ X / Y của Point) theo thứ tự trong block.</summary>
        public List<KeyValuePair<string, double>> Sizes { get; } = new List<KeyValuePair<string, double>>();
        /// <summary>Tên mọi tham số dạng số đang hiện (độ dài, góc, toạ độ, lật...).</summary>
        public HashSet<string> NumericNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tên tham số số chỉ dùng khi block KHÔNG có Visibility (chủng loại cũ chỉ ghép tham số lúc đó) - null nếu không có.</summary>
        public HashSet<string> LegacyNames => HasVisibility || NumericNames.Count == 0 ? null : NumericNames;

        /// <summary>Kích thước hiển thị / khoá tách dòng: "12320", "1200 x 600" (làm tròn theo DynamicParamText.FormatSize).</summary>
        public string SizeText => DynamicParamText.JoinSizes(Sizes.Select(kv => kv.Value));
    }

    public static class BlockExtractor
    {
        /// <summary>MINSERT lớn hơn mức này chỉ lấy chừng này phần tử (phòng bản vẽ lỗi hàng triệu phần tử treo CAD).</summary>
        private const int MaxMInsertElements = 100000;

        /// <summary>
        /// Cho user quét chọn trên bản vẽ, trích xuất và gom nhóm Block theo ExtractionOptions.
        /// options.TemplateFilter khác null (có block mẫu): chỉ block mẫu (hoặc block cha chứa block mẫu) được chọn.
        /// selectedIds = các đối tượng đã chọn (form giữ lại để quét lại khi đổi tuỳ chọn). Trả null nếu user huỷ.
        /// v9.4: bỏ biến static LastSelectedObjectIds dùng chung mọi bản vẽ -> mỗi form giữ vùng chọn của bản vẽ mình.
        /// </summary>
        public static List<BlockItem> ExtractFromSelection(Document doc, ExtractionOptions options, out List<ObjectId> selectedIds)
        {
            options ??= new ExtractionOptions();
            selectedIds = null;
            var ids = PromptSelection(doc, options, "\nQuét chọn vùng cần thống kê: ");
            if (ids == null) return null;
            selectedIds = ids.Distinct().ToList();
            return ExtractFromObjectIds(doc, selectedIds, options);
        }

        /// <summary>
        /// "Quét thêm": chọn thêm vùng, chỉ trích xuất đối tượng CHƯA có trong alreadySelected (chọn lại vùng cũ không
        /// đếm 2 lần). addedIds = đối tượng mới (form cộng vào vùng chọn của mình). Trả null nếu user huỷ.
        /// </summary>
        public static List<BlockItem> ExtractAdditionalSelection(Document doc, ExtractionOptions options, ICollection<ObjectId> alreadySelected,
                                                                 out List<ObjectId> addedIds, out int alreadyCount)
        {
            options ??= new ExtractionOptions();
            addedIds = new List<ObjectId>();
            alreadyCount = 0;
            var ids = PromptSelection(doc, options, "\nQuét chọn THÊM vùng cần thống kê: ");
            if (ids == null) return null;

            var known = alreadySelected as HashSet<ObjectId> ?? new HashSet<ObjectId>(alreadySelected ?? new List<ObjectId>());
            addedIds = ids.Where(id => !known.Contains(id)).Distinct().ToList();
            alreadyCount = ids.Length - addedIds.Count;
            Logger.Log($"BlockExtractor.ExtractAdditionalSelection: chọn {ids.Length} đối tượng, mới {addedIds.Count}, đã có từ lần trước {alreadyCount} (bỏ qua)");
            return ExtractFromObjectIds(doc, addedIds, options);
        }

        /// <summary>
        /// Nhắc chọn đối tượng. v9.4: chỉ cho chọn INSERT (block, MINSERT, ARRAY, XREF) -> chọn ALL không còn trả về
        /// hàng vạn nét / chữ. Có block mẫu thì gắn thêm bộ lọc vào SelectionAdded: block không phải block mẫu bị bỏ
        /// ngay khi quét, không sáng lên, không vào vùng chọn.
        /// </summary>
        private static ObjectId[] PromptSelection(Document doc, ExtractionOptions options, string message)
        {
            var ed = doc.Editor;
            TemplateSelectionFilter filter = null;
            var template = options.TemplateMode ? options.TemplateFilter : null;
            if (template != null)
            {
                filter = new TemplateSelectionFilter(doc.Database, template, options.CountXrefBlocks);
                ed.SelectionAdded += filter.OnSelectionAdded;
                ed.WriteMessage($"\n[LHB] Chỉ quét block mẫu (bộ '{template.Name}', {template.Entries.Count} block). " +
                                "Block khác, chữ, nét không được chọn. Tắt ô 'Chỉ quét block mẫu' trên form nếu muốn quét tất cả.");
                message = message.TrimEnd(':', ' ') + " (chỉ dính block mẫu): ";
            }

            try
            {
                var onlyInserts = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                var selRes = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = message }, onlyInserts);
                if (selRes.Status != PromptStatus.OK)
                {
                    Logger.Log("BlockExtractor: user huỷ chọn / không chọn được block (status=" + selRes.Status + ")");
                    return null;
                }
                return selRes.Value.GetObjectIds();
            }
            finally
            {
                if (filter != null)
                {
                    ed.SelectionAdded -= filter.OnSelectionAdded;
                    Logger.Log($"[TemplateSelectionFilter] bộ '{template.Name}': giữ {filter.Kept} block ({filter.KeptAsParent} là block cha / ARRAY chứa block mẫu), " +
                               $"bỏ {filter.Removed} block không phải block mẫu");
                }
            }
        }

        /// <summary>
        /// Trích xuất danh sách BlockItem từ tập ObjectId đã chọn trước đó (dùng khi user đổi option trên UI mà không cần pick lại).
        /// </summary>
        public static List<BlockItem> ExtractFromObjectIds(Document doc, IEnumerable<ObjectId> rootIds, ExtractionOptions options = null)
        {
            options ??= new ExtractionOptions();
            return GroupScannedRefs(doc, ScanRefs(doc.Database, rootIds, options), options);
        }

        /// <summary>
        /// Trích xuất từ database bất kỳ (bản vẽ khác đọc bằng ReadDwgFile - Premium "Nhiều bản vẽ"): không tạo ảnh.
        /// ObjectId trong kết quả chỉ hợp lệ khi database còn mở.
        /// </summary>
        public static List<BlockItem> ExtractFromDatabase(Database db, IEnumerable<ObjectId> rootIds, ExtractionOptions options)
        {
            options ??= new ExtractionOptions();
            return GroupScannedRefs(null, ScanRefs(db, rootIds, options), options);
        }

        /// <summary>Mọi block đặt trực tiếp trong Model Space (quét toàn bản vẽ).</summary>
        public static List<ObjectId> ModelSpaceBlockIds(Database db)
        {
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (id.ObjectClass.IsDerivedFrom(BlockRefClass)) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        private static Autodesk.AutoCAD.Runtime.RXClass _blockRefClass;

        /// <summary>RXClass của BlockReference: lọc ObjectId theo loại mà không phải mở đối tượng ra.</summary>
        internal static Autodesk.AutoCAD.Runtime.RXClass BlockRefClass =>
            _blockRefClass ??= Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));

        /// <summary>Loại đối tượng INSERT khi duyệt (v9.4).</summary>
        internal enum RefKind
        {
            /// <summary>Block thường / dynamic block: thiết bị hoặc block cha.</summary>
            Block,
            /// <summary>Bảng AutoCAD (Table kế thừa BlockReference): không đếm, không đi vào (ô ký hiệu của bảng thống kê).</summary>
            Table,
            /// <summary>XREF: bỏ qua, hoặc là vỏ trong suốt khi bật "Đếm trong XREF".</summary>
            Xref,
            /// <summary>ARRAY liên kết / block ẩn danh *U không phải dynamic: vỏ trong suốt, đếm block bên trong.</summary>
            Container
        }

        /// <summary>Phân loại 1 INSERT. def = định nghĩa block gốc (DynamicBlockTableRecord).</summary>
        internal static RefKind Classify(Transaction tr, BlockReference br, out BlockTableRecord def)
        {
            def = null;
            if (br is Table) return RefKind.Table;
            def = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
            if (def.IsFromExternalReference) return RefKind.Xref;
            if (def.IsLayout) return RefKind.Table; // không xảy ra với bản vẽ hợp lệ, phòng hờ: bỏ qua
            if (!br.IsDynamicBlock && (def.IsAnonymous || string.IsNullOrEmpty(def.Name) || def.Name.StartsWith("*")))
                return RefKind.Container;
            return RefKind.Block;
        }

        /// <summary>ARRAY liên kết (lệnh ARRAY mặc định) hay block ẩn danh khác - chỉ để ghi log / cột Nguồn.</summary>
        private static bool IsAssociativeArray(ObjectId id)
        {
            try { return AssocArray.IsAssociativeArray(id); }
            catch { return false; }
        }

        /// <summary>
        /// Bộ nhớ đệm trong 1 lần quét: khung bao theo BTR, danh sách block con theo BTR (định nghĩa block không đổi
        /// trong lúc quét -> 5000 đầu phun cùng định nghĩa chỉ duyệt nội dung định nghĩa 1 lần thay vì 5000 lần).
        /// </summary>
        private sealed class ScanCache
        {
            public readonly HashSet<ObjectId> Visiting = new HashSet<ObjectId>();
            public readonly Dictionary<ObjectId, Extents3d?> Extents = new Dictionary<ObjectId, Extents3d?>();
            public readonly Dictionary<ObjectId, List<ObjectId>> ChildRefs = new Dictionary<ObjectId, List<ObjectId>>();
            /// <summary>Tên các block con đang hiện của 1 BTR (kiểm tra khi chạm giới hạn độ sâu).</summary>
            public readonly Dictionary<ObjectId, List<string>> VisibleKidNames = new Dictionary<ObjectId, List<string>>();
            /// <summary>Chế độ block mẫu: BTR không chứa block mẫu nào ở mọi tầng -> không duyệt lại.</summary>
            public readonly HashSet<ObjectId> NoTemplateBtrs = new HashSet<ObjectId>();
            /// <summary>v9.6: block động có tham số số (không làm chủng loại) -> tên tham số + kích thước mẫu, để ghi log 1 lần / tên block.</summary>
            public readonly Dictionary<string, string> NumericParamLog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public ExtractionOptions Options;
            public TemplateLibrary Template;
            public HashSet<string> TemplateNames;
            public ScanStats Stats;
        }

        private static List<ScannedRef> ScanRefs(Database db, IEnumerable<ObjectId> rootIds, ExtractionOptions options)
        {
            var scannedRefs = new List<ScannedRef>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stats = new ScanStats { MaxDepth = options.MaxDepth, TemplateMode = options.TemplateMode };
            var cache = new ScanCache { Options = options, Stats = stats };
            if (options.TemplateMode)
            {
                cache.Template = options.TemplateFilter;
                cache.TemplateNames = new HashSet<string>(options.TemplateFilter.Entries.Select(e => e.BlockName), StringComparer.OrdinalIgnoreCase);
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in rootIds)
                {
                    // Lọc theo loại trước khi mở: vùng chọn có hàng vạn nét / chữ không phải block
                    if (id.IsNull || !id.IsValid || id.IsErased || !id.ObjectClass.IsDerivedFrom(BlockRefClass)) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    if (!br.Visible) { stats.Hidden++; continue; }
                    stats.Roots++;
                    Walk(tr, br, Matrix3d.Identity, 0, new List<ObjectId> { br.ObjectId }, RefVia.None, scannedRefs, cache);
                }
                tr.Commit();
            }

            stats.Refs = scannedRefs.Count;
            stats.ElapsedMs = sw.ElapsedMilliseconds;
            // Block con bị cắt vì độ sâu mà cùng loại với thiết bị đang đếm -> thiết bị bị sót
            if (stats.BeyondDepth.Count > 0)
            {
                var counted = new HashSet<string>(scannedRefs.Where(r => options.CountParentBlocks || !r.HasChildren).Select(r => r.BlockName),
                                                  StringComparer.OrdinalIgnoreCase);
                foreach (var kv in stats.BeyondDepth)
                    if (counted.Contains(kv.Key)) stats.MissedByDepth[kv.Key] = kv.Value;
                Logger.Log($"BlockExtractor: {stats.DepthLimited} block chạm giới hạn độ sâu {options.MaxDepth} " +
                           $"[{string.Join(", ", stats.DepthLimitedNames.Take(10))}], block con không duyệt tới: " +
                           $"[{string.Join(", ", stats.BeyondDepth.Take(20).Select(kv => kv.Key + " x" + kv.Value))}]");
            }
            options.Stats = stats;
            int noCorners = scannedRefs.Count(r => r.Corners == null);
            Logger.Log($"BlockExtractor: quét [{options}] -> {stats.Summary()}; {cache.Extents.Count} định nghĩa block khác nhau, {stats.ElapsedMs} ms" +
                       (noCorners > 0 ? $", {noCorners} block không tính được khung bao (chỉ so trùng theo điểm chèn)" : ""));
            if (stats.SkippedXrefNames.Count > 0)
                Logger.Log($"BlockExtractor: XREF bỏ qua (bật 'Đếm trong XREF' để đếm block bên trong): [{string.Join(", ", stats.SkippedXrefNames)}]");
            if (cache.NumericParamLog.Count > 0)
                Logger.Log($"BlockExtractor: {cache.NumericParamLog.Count} block động có tham số số (độ dài / góc / toạ độ / lật) KHÔNG tính làm chủng loại" +
                           (options.SplitBySize ? ", tách dòng theo kích thước" : " (bật 'Tách theo kích thước' để tách dòng)") + ": " +
                           string.Join("; ", cache.NumericParamLog.Take(20).Select(kv => kv.Key + " " + kv.Value)));
            string depthWarn = stats.DepthWarning();
            if (depthWarn != null) Logger.Warn("BlockExtractor: " + depthWarn);
            return scannedRefs;
        }

        /// <summary>
        /// Duyệt 1 INSERT ở tầng depth (0 = nằm trực tiếp trong vùng chọn), path = đường dẫn ObjectId tới chính nó.
        /// ARRAY / block ẩn danh / XREF (khi bật đếm) là vỏ trong suốt: không ghi nhận chính nó, block bên trong giữ
        /// nguyên tầng của vỏ (trước v9.4 ARRAY bị bỏ qua cả cụm, XREF bị đếm như 1 block). Trả true nếu có duyệt
        /// (không phải bảng / XREF bỏ qua) - để biết block cha có block con thật hay không.
        /// </summary>
        private static bool Walk(Transaction tr, BlockReference br, Matrix3d parentXform, int depth, List<ObjectId> path,
                                 RefVia via, List<ScannedRef> results, ScanCache cache)
        {
            try
            {
                switch (Classify(tr, br, out var def))
                {
                    case RefKind.Table:
                        cache.Stats.Tables++;
                        return false;

                    case RefKind.Xref:
                        if (!cache.Options.CountXrefBlocks)
                        {
                            cache.Stats.XrefsSkipped++;
                            cache.Stats.SkippedXrefNames.Add(def.Name);
                            return false;
                        }
                        cache.Stats.XrefsEntered++;
                        if (!def.IsResolved)
                            Logger.Warn($"BlockExtractor: XREF '{def.Name}' chưa nạp / không tìm thấy file (XrefStatus={def.XrefStatus}) -> không có block bên trong để đếm");
                        int beforeXref = results.Count;
                        WalkContainer(tr, br, parentXform, depth, path, via | RefVia.Xref, results, cache);
                        // Chỉ cộng ở XREF ngoài cùng (XREF lồng trong XREF không cộng 2 lần)
                        if ((via & RefVia.Xref) == 0) cache.Stats.XrefRefs += results.Count - beforeXref;
                        return true;

                    case RefKind.Container:
                        bool isArray = IsAssociativeArray(br.ObjectId);
                        if (isArray) cache.Stats.Arrays++;
                        else cache.Stats.AnonymousBlocks++;
                        int beforeArray = results.Count;
                        WalkContainer(tr, br, parentXform, depth, path, via | (isArray ? RefVia.Array : RefVia.Anonymous), results, cache);
                        if ((via & (RefVia.Array | RefVia.Anonymous)) == 0) cache.Stats.ContainerRefs += results.Count - beforeArray;
                        return true;

                    default:
                        WalkBlock(tr, br, def, parentXform, depth, path, via, results, cache);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"BlockExtractor.Walk Handle={br?.Handle}");
                return false;
            }
        }

        /// <summary>Vỏ trong suốt (ARRAY, block ẩn danh, XREF): duyệt block bên trong ở cùng tầng, không ghi nhận chính nó.</summary>
        private static void WalkContainer(Transaction tr, BlockReference br, Matrix3d parentXform, int depth, List<ObjectId> path,
                                          RefVia via, List<ScannedRef> results, ScanCache cache)
        {
            foreach (var off in ElementOffsets(br, cache.Stats))
            {
                var xform = parentXform * Matrix3d.Displacement(off) * br.BlockTransform;
                WalkChildren(tr, br.BlockTableRecord, xform, depth, path, via, results, cache);
            }
        }

        /// <summary>
        /// Block thật: ghi nhận (thiết bị / block cha) rồi đi vào block con theo độ sâu. MINSERT = Rows x Columns phần tử,
        /// mỗi phần tử ghi nhận riêng với vị trí riêng (trước v9.4 MINSERT 4x5 chỉ đếm 1).
        /// </summary>
        private static void WalkBlock(Transaction tr, BlockReference br, BlockTableRecord def, Matrix3d parentXform, int depth,
                                      List<ObjectId> path, RefVia via, List<ScannedRef> results, ScanCache cache)
        {
            string realName = def.Name;
            bool isDynamic = br.IsDynamicBlock;

            // Xác định BlockKind
            string kind;
            if (isDynamic) kind = "Động";
            else if (def.HasAttributeDefinitions) kind = "Có thuộc tính";
            else kind = "Tĩnh";

            bool templateMode = cache.Template != null;
            Dictionary<string, string> attributes = null;
            var dyn = DynamicInfo.Empty;
            bool isTemplate = false;
            // Chế độ block mẫu: block không trùng tên block mẫu nào thì không cần đọc thuộc tính / tham số dynamic
            if (!templateMode || cache.TemplateNames.Contains(realName) || cache.TemplateNames.Contains(TemplateLibraryManager.StripXrefPrefix(realName)))
            {
                attributes = ReadAttributes(tr, br);
                dyn = ReadDynamic(br, realName, ref attributes, cache.Options.LegacyVariant);
                isTemplate = templateMode && TemplateLibraryManager.Match(cache.Template, realName, dyn.Variant, dyn.LegacyNames) != null;
                if (dyn.NumericNames.Count > 0 && !cache.NumericParamLog.ContainsKey(realName))
                    cache.NumericParamLog[realName] = $"[{string.Join(", ", dyn.NumericNames)}]" +
                                                      (dyn.Sizes.Count > 0 ? $" kích thước '{dyn.SizeText}'" : "") +
                                                      (dyn.HasVisibility ? $", chủng loại theo Visibility '{dyn.Variant}'" : $", chủng loại '{dyn.Variant}'");
            }
            string visibility = dyn.Variant;
            // Chế độ block mẫu: chỉ block mẫu được ghi nhận, block khác là vỏ chứa; đi sâu không giới hạn,
            // chỉ đi vào trong block mẫu khi đếm cả block cha. Chế độ thường: theo độ sâu quét.
            bool record = !templateMode || isTemplate;
            bool descend = templateMode ? (!isTemplate || cache.Options.CountParentBlocks) : depth + 1 < cache.Options.MaxDepth;

            var offsets = ElementOffsets(br, cache.Stats);
            bool isMInsert = br is MInsertBlock && offsets.Count > 1;
            var childVia = isMInsert ? via | RefVia.MInsert : via;

            foreach (var off in offsets)
            {
                Matrix3d currentXform = parentXform * Matrix3d.Displacement(off) * br.BlockTransform;
                ScannedRef currentRef = null;
                if (record)
                {
                    var cs = currentXform.CoordinateSystem3d;
                    bool mirrored = cs.Xaxis.CrossProduct(cs.Yaxis).Z < 0;
                    currentRef = new ScannedRef
                    {
                        BrId = br.ObjectId,
                        DynamicBtrId = def.ObjectId,
                        InstanceBtrId = br.BlockTableRecord,
                        BlockName = realName,
                        BlockKind = kind,
                        VisibilityState = visibility,
                        Layer = br.Layer,
                        Depth = depth,
                        ContainerPath = path.ToArray(),
                        Transform = currentXform,
                        OwnerTransform = parentXform,
                        Position = (br.Position + off).TransformBy(parentXform),
                        Corners = ComputeCorners(tr, br.BlockTableRecord, currentXform, cache.Extents),
                        HasChildren = false,
                        ScaleX = cs.Xaxis.Length,
                        ScaleY = mirrored ? -cs.Yaxis.Length : cs.Yaxis.Length,
                        Rotation = Math.Atan2(cs.Xaxis.Y, cs.Xaxis.X),
                        Attributes = attributes,
                        Via = childVia,
                        IsMInsertElement = isMInsert,
                        Size = dyn.SizeText,
                        NumericParamNames = dyn.LegacyNames,
                        IsDynamic = isDynamic
                    };
                    results.Add(currentRef);
                }

                if (descend)
                {
                    bool hasChildren = WalkChildren(tr, br.BlockTableRecord, currentXform, depth + 1, path, childVia, results, cache);
                    // Chế độ block mẫu: block mẫu luôn được đếm (không coi là block cha bị loại)
                    if (currentRef != null && hasChildren && !isTemplate) currentRef.HasChildren = true;
                }
                else if (currentRef != null && !templateMode)
                {
                    // Chạm giới hạn độ sâu: block này có block con nhưng không duyệt -> đếm như block lá; ghi lại tên block con
                    // để cảnh báo nếu cùng loại với thiết bị đang đếm ở chỗ khác
                    var kids = VisibleChildNames(tr, br.BlockTableRecord, cache);
                    if (kids.Count > 0)
                    {
                        cache.Stats.DepthLimited++;
                        cache.Stats.DepthLimitedNames.Add(realName);
                        foreach (var k in kids)
                            cache.Stats.BeyondDepth[k] = (cache.Stats.BeyondDepth.TryGetValue(k, out int n) ? n : 0) + 1;
                    }
                }
            }
        }

        /// <summary>
        /// Duyệt các block con đang HIỆN trong 1 BTR (v9.4: bỏ block con bị ẩn theo trạng thái visibility của dynamic
        /// block cha, trước đây vẫn đếm). Trả true nếu có ít nhất 1 block con thật (không tính bảng / XREF bỏ qua).
        /// </summary>
        private static bool WalkChildren(Transaction tr, ObjectId btrId, Matrix3d xform, int childDepth, List<ObjectId> pathSoFar,
                                         RefVia via, List<ScannedRef> results, ScanCache cache)
        {
            bool templateMode = cache.Template != null;
            if (templateMode && cache.NoTemplateBtrs.Contains(btrId)) return true;
            if (!cache.Visiting.Add(btrId)) return false; // định nghĩa block tự chứa chính nó (bản vẽ lỗi)

            bool any = false;
            int before = results.Count;
            try
            {
                foreach (ObjectId childId in ChildBlockRefs(tr, btrId, cache))
                {
                    if (!(tr.GetObject(childId, OpenMode.ForRead) is BlockReference childBr)) continue;
                    if (!childBr.Visible)
                    {
                        cache.Stats.Hidden++;
                        continue;
                    }
                    var childPath = new List<ObjectId>(pathSoFar) { childId };
                    if (Walk(tr, childBr, xform, childDepth, childPath, via, results, cache)) any = true;
                }
            }
            finally
            {
                cache.Visiting.Remove(btrId);
            }
            // Nội dung định nghĩa block không đổi trong lúc quét: không có block mẫu thì các bản khác cũng không có
            if (templateMode && results.Count == before) cache.NoTemplateBtrs.Add(btrId);
            return any;
        }

        /// <summary>
        /// Tên các block con thật đang hiện trong 1 BTR (mỗi instance 1 tên, bỏ bảng, bỏ XREF, ARRAY tính theo tên block bên
        /// trong 1 tầng) - nhớ theo BTR. Dùng khi chạm giới hạn độ sâu.
        /// </summary>
        private static List<string> VisibleChildNames(Transaction tr, ObjectId btrId, ScanCache cache)
        {
            if (cache.VisibleKidNames.TryGetValue(btrId, out var names)) return names;
            names = new List<string>();
            cache.VisibleKidNames[btrId] = names; // chống vòng lặp
            foreach (ObjectId childId in ChildBlockRefs(tr, btrId, cache))
            {
                try
                {
                    if (!(tr.GetObject(childId, OpenMode.ForRead) is BlockReference c) || !c.Visible) continue;
                    switch (Classify(tr, c, out var def))
                    {
                        case RefKind.Block:
                            int n = c is MInsertBlock mi ? Math.Max(1, (int)mi.Rows) * Math.Max(1, (int)mi.Columns) : 1;
                            for (int i = 0; i < n; i++) names.Add(def.Name);
                            break;
                        case RefKind.Container:
                            names.AddRange(VisibleChildNames(tr, c.BlockTableRecord, cache));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"BlockExtractor: đọc block con {childId.Handle} lỗi {ex.Message}");
                }
            }
            return names;
        }

        /// <summary>
        /// Độ dời từng phần tử theo toạ độ chủ sở hữu: block thường = [0]; MINSERT = Rows x Columns độ dời
        /// (cột theo trục X, hàng theo trục Y của block đã xoay, khoảng cách tính bằng đơn vị bản vẽ, không nhân tỉ lệ).
        /// </summary>
        private static List<Vector3d> ElementOffsets(BlockReference br, ScanStats stats)
        {
            if (!(br is MInsertBlock mi)) return new List<Vector3d>(1) { new Vector3d(0, 0, 0) };
            int rows = Math.Max(1, (int)mi.Rows), cols = Math.Max(1, (int)mi.Columns);
            long total = (long)rows * cols;
            stats.MInserts++;
            if (total > MaxMInsertElements)
            {
                Logger.Warn($"BlockExtractor: MINSERT Handle={br.Handle} có {rows}x{cols} = {total} phần tử (quá lớn) -> chỉ lấy {MaxMInsertElements}");
                rows = Math.Max(1, MaxMInsertElements / cols);
                if (rows * (long)cols > MaxMInsertElements) { rows = 1; cols = MaxMInsertElements; }
            }
            var toOwner = Matrix3d.PlaneToWorld(mi.Normal);
            var list = new List<Vector3d>(rows * cols);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    list.Add(new Vector3d(c * mi.ColumnSpacing, r * mi.RowSpacing, 0).RotateBy(mi.Rotation, Vector3d.ZAxis).TransformBy(toOwner));
            stats.MInsertElements += list.Count;
            return list;
        }

        /// <summary>Block con (BlockReference) nằm trực tiếp trong 1 BTR, nhớ theo BTR.</summary>
        private static List<ObjectId> ChildBlockRefs(Transaction tr, ObjectId btrId, ScanCache cache)
        {
            if (cache.ChildRefs.TryGetValue(btrId, out var list)) return list;
            list = new List<ObjectId>();
            if (tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr && !btr.IsLayout)
            {
                foreach (ObjectId id in btr)
                    if (id.ObjectClass.IsDerivedFrom(BlockRefClass)) list.Add(id);
            }
            cache.ChildRefs[btrId] = list;
            return list;
        }

        /// <summary>Thuộc tính "A:TAG" của block (null nếu không có) - Premium P5.</summary>
        private static Dictionary<string, string> ReadAttributes(Transaction tr, BlockReference br)
        {
            Dictionary<string, string> d = null;
            try
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (!(tr.GetObject(attId, OpenMode.ForRead) is AttributeReference ar) || string.IsNullOrEmpty(ar.Tag)) continue;
                    d ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string text = ar.IsMTextAttribute ? VietnameseHelper.CleanMTextString(ar.TextString) : ar.TextString;
                    d["A:" + ar.Tag.ToUpperInvariant()] = (text ?? "").Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"BlockExtractor: không đọc được thuộc tính block Handle={br.Handle}: {ex.Message}");
            }
            return d;
        }

        /// <summary>
        /// Đọc tham số dynamic block MỘT lần (DynamicBlockReferencePropertyCollection tốn thời gian): chủng loại, kích thước,
        /// tên tham số số (DynamicInfo) và thêm tham số đang hiện trừ Visibility vào attrs (khoá "D:Tên") - Premium P5.
        /// legacyVariant = chủng loại kiểu v9 - v9.5 (ghép cả tham số số) cho bảng cũ khi LHBCAPNHAT.
        /// </summary>
        private static DynamicInfo ReadDynamic(BlockReference br, string realName, ref Dictionary<string, string> attrs, bool legacyVariant = false)
        {
            if (!br.IsDynamicBlock) return DynamicInfo.Empty;
            var info = new DynamicInfo();
            object visValue = null;
            var textProps = new List<string>();
            var legacyProps = new List<string>();
            try
            {
                foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                {
                    if (!p.Show) continue;
                    string pName = p.PropertyName ?? "";
                    object v = p.Value;
                    if (pName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Tham số Visibility đầu tiên là chủng loại; tham số đứng sau không ghép vào chủng loại nữa
                        if (!info.HasVisibility) { info.HasVisibility = true; visValue = v; }
                        continue;
                    }
                    if (!info.HasVisibility) legacyProps.Add($"{pName}={v}");

                    bool numeric = IsNumber(v);
                    if (numeric)
                    {
                        if (pName.Length > 0) info.NumericNames.Add(pName);
                        if (IsSizeProperty(p, pName))
                            info.Sizes.Add(new KeyValuePair<string, double>(pName, Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    else if (v != null && !(v is Point3d) && !(v is Point2d) && !(v is ObjectId))
                    {
                        string text = v.ToString().Trim();
                        if (text.Length > 0) textProps.Add($"{pName}={text}");
                    }

                    if (pName.Length == 0) continue;
                    string s;
                    if (v is double dv) s = dv.ToString("0.###");
                    else if (v is short || v is int || v is long || v is string) s = v.ToString();
                    else continue; // Point3d, ObjectId... không hiện được thành cột
                    attrs ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    attrs["D:" + pName] = s;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"BlockExtractor: không đọc được dynamic property cho '{realName}': {ex.Message}");
                return DynamicInfo.Empty;
            }
            if (info.HasVisibility && visValue != null) info.Variant = visValue.ToString();
            else if (legacyVariant) info.Variant = string.Join(" - ", legacyProps);
            else info.Variant = info.HasVisibility ? "" : string.Join(" - ", textProps);
            return info;
        }

        /// <summary>
        /// Tham số độ dài (Linear / Polar / XY: đơn vị Distance hoặc Area) đang có tác dụng ở chủng loại hiện tại. Toạ độ X / Y
        /// của Point (vị trí nhãn, đầu dây...) không phải kích thước thiết bị. Lỗi đọc -> không tính (không làm mất chủng loại).
        /// </summary>
        private static bool IsSizeProperty(DynamicBlockReferenceProperty p, string name)
        {
            try
            {
                var u = p.UnitsType;
                return (u == DynamicBlockReferencePropertyUnitsType.Distance || u == DynamicBlockReferencePropertyUnitsType.Area) &&
                       p.VisibleInCurrentVisibilityState && !DynamicParamText.IsPointCoordinate(name);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNumber(object v) =>
            v is double || v is float || v is short || v is int || v is long || v is byte || v is ushort || v is uint || v is decimal;

        /// <summary>Khoá thuộc tính -> tên cột dễ đọc: "A:MA_TB" -> "MA_TB", "D:Distance1" -> "Tham số Distance1".</summary>
        public static string AttributeKeyLabel(string key) =>
            key == null ? "" : key.StartsWith("A:") ? key.Substring(2) : key.StartsWith("D:") ? "Tham số " + key.Substring(2) : key;

        /// <summary>
        /// 4 góc khung bao của block theo WCS: khung bao trong toạ độ định nghĩa block (BTR thực của instance, với dynamic
        /// block là BTR ẩn danh đúng chủng loại) rồi biến đổi theo ma trận của block. Cache extents theo BTR vì
        /// hàng nghìn block cùng định nghĩa (sprinkler) chỉ cần tính 1 lần.
        /// </summary>
        private static Point2d[] ComputeCorners(Transaction tr, ObjectId btrId, Matrix3d xform, Dictionary<ObjectId, Extents3d?> extCache)
        {
            if (!extCache.TryGetValue(btrId, out var ext))
            {
                ext = null;
                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (ExtentsHelper.TryGetBtrExtents(tr, btr, out var e)) ext = e;
                    else Logger.Warn($"BlockExtractor: block '{btr.Name}' không tính được khung bao -> chỉ so trùng theo điểm chèn");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"BlockExtractor: lỗi tính khung bao BTR {btrId}: {ex.Message}");
                }
                extCache[btrId] = ext;
            }
            if (ext == null) return null;

            var mn = ext.Value.MinPoint;
            var mx = ext.Value.MaxPoint;
            double z = (mn.Z + mx.Z) / 2.0;
            Point2d Corner(double x, double y) { var w = new Point3d(x, y, z).TransformBy(xform); return new Point2d(w.X, w.Y); }
            return new[] { Corner(mn.X, mn.Y), Corner(mx.X, mn.Y), Corner(mx.X, mx.Y), Corner(mn.X, mx.Y) };
        }

        /// <summary>
        /// Chủng loại của block: giá trị tham số Visibility của dynamic block; dynamic block không có Visibility thì
        /// ghép các tham số dạng chữ đang hiện (v9.6: bỏ tham số số); block thường = "". Dùng chung cho quét thống kê và
        /// thư viện block mẫu.
        /// </summary>
        public static string ReadVisibility(BlockReference br, string realName) => ReadDynamicInfo(br, realName).Variant;

        /// <summary>Tham số block động (chủng loại, kích thước, tên tham số số) - cùng hàm đọc với lúc quét.</summary>
        public static DynamicInfo ReadDynamicInfo(BlockReference br, string realName)
        {
            // Cùng 1 hàm đọc với lúc quét -> lọc block mẫu và gom dòng luôn ra cùng chủng loại
            Dictionary<string, string> ignored = null;
            return ReadDynamic(br, realName, ref ignored);
        }

        /// <summary>
        /// Tham số block động của 1 block theo TÊN (lấy 1 block reference bất kỳ của block đó trong bản vẽ, kể cả bản đã
        /// sửa tham số nằm trong BTR ẩn danh *U). null = bản vẽ không có block này / không phải block động / chưa đặt lần nào.
        /// Dùng chuẩn hoá chủng loại cũ "Distance1=..." của block mẫu (v9.6).
        /// </summary>
        public static DynamicInfo FindDynamicInfo(Database db, string blockName)
        {
            if (db == null || string.IsNullOrEmpty(blockName)) return null;
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(blockName)) return null;
                    var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                    if (!btr.IsDynamicBlock) return null;
                    var btrIds = new List<ObjectId> { btr.ObjectId };
                    foreach (ObjectId anon in btr.GetAnonymousBlockIds()) btrIds.Add(anon);
                    foreach (var id in btrIds)
                    {
                        var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        foreach (ObjectId refId in b.GetBlockReferenceIds(true, false))
                        {
                            if (refId.IsErased || !(tr.GetObject(refId, OpenMode.ForRead) is BlockReference br) || br is Table) continue;
                            var info = ReadDynamicInfo(br, blockName);
                            tr.Commit();
                            return info;
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"BlockExtractor.FindDynamicInfo '{blockName}': {ex.Message}");
            }
            return null;
        }

        private static List<BlockItem> GroupScannedRefs(Document doc, List<ScannedRef> refs, ExtractionOptions options)
        {
            // Nếu không bật CountParentBlocks thì lọc bỏ các block cha có chứa block con
            var filtered = refs;
            if (!options.CountParentBlocks)
            {
                filtered = refs.Where(r => !r.HasChildren).ToList();
            }

            var groupDict = new Dictionary<string, List<ScannedRef>>();

            foreach (var r in filtered)
            {
                string key = r.BlockName;
                if (options.SplitByVisibility && !string.IsNullOrEmpty(r.VisibilityState))
                {
                    key += "||VIS:" + r.VisibilityState;
                }
                if (options.SplitByLayer && !string.IsNullOrEmpty(r.Layer))
                {
                    key += "||LAY:" + r.Layer;
                }
                // v9.6: tách theo kích thước (tham số độ dài block động), khoá đã làm tròn (FormatSize)
                if (options.SplitBySize && !string.IsNullOrEmpty(r.Size))
                {
                    key += "||SIZE:" + r.Size;
                }
                if (options.SplitAttributeKeys != null)
                {
                    foreach (var ak in options.SplitAttributeKeys)
                    {
                        string v = r.Attributes != null && r.Attributes.TryGetValue(ak, out var av) ? av : "";
                        key += "||" + ak + "=" + v;
                    }
                }
                r.GroupKey = key;

                if (!groupDict.TryGetValue(key, out var list))
                {
                    list = new List<ScannedRef>();
                    groupDict[key] = list;
                }
                list.Add(r);
            }

            // Chữ ký nội dung BTR dùng chung trong lần gom này (nhiều dòng cùng định nghĩa block gốc)
            var signatureMemo = new Dictionary<ObjectId, string>();
            var items = new List<BlockItem>();
            foreach (var kvp in groupDict)
            {
                var list = kvp.Value;
                var first = list[0];

                // Block trong XREF tên "XREF|TÊN": tên hiển thị bỏ tiền tố (tên block giữ nguyên để không lẫn với block cùng tên của bản vẽ)
                string displayName = TemplateLibraryManager.StripXrefPrefix(first.BlockName);
                if (Regex.IsMatch(displayName, @"^A\$[A-Za-z]") && !string.IsNullOrEmpty(first.VisibilityState))
                {
                    displayName = first.VisibilityState;
                }

                var item = new BlockItem
                {
                    BlockName = first.BlockName,
                    DisplayName = displayName,
                    VisibilityState = first.VisibilityState,
                    Size = DynamicParamText.GroupSize(list.Select(x => x.Size)),
                    NumericParamNames = first.NumericParamNames,
                    IsDynamic = first.IsDynamic,
                    BlockKind = first.BlockKind,
                    Unit = "Cái",
                    Note = "",
                    Count = list.Count,
                    NestDepth = list.Min(x => x.Depth),
                    ContainerPath = first.ContainerPath,
                    LayerName = first.Layer,
                    AllLayers = list.Select(x => x.Layer).Distinct().ToArray(),
                    SourceBtrId = options.SplitByVisibility ? first.InstanceBtrId : first.DynamicBtrId,
                    DynamicBtrId = first.DynamicBtrId,
                    ObjectIds = list.Select(x => x.BrId).ToList(),
                    Instances = list.Select(x => new BlockInstanceRef
                    {
                        Path = x.ContainerPath,
                        Position = x.Position,
                        OwnerTransform = x.OwnerTransform,
                        Key = x.BlockName + "||" + (x.VisibilityState ?? ""),
                        BlockName = x.BlockName,
                        Corners = x.Corners,
                        GroupKey = x.GroupKey,
                        Layer = x.Layer,
                        ScaleX = x.ScaleX,
                        ScaleY = x.ScaleY,
                        Rotation = x.Rotation,
                        Attributes = x.Attributes,
                        Via = x.Via,
                        IsMInsertElement = x.IsMInsertElement
                    }).ToList()
                };

                // Database khác (không có Document) -> không tạo ảnh
                if (doc == null)
                {
                    items.Add(item);
                    continue;
                }

                // 1. Ảnh hiển thị trên grid và ô ký hiệu: render từ SourceBtrId (InstanceBtrId khi tách theo chủng loại).
                // v9.4: tên file ảnh kèm chữ ký nội dung định nghĩa block -> block cùng tên khác hình ở bản vẽ khác không
                // dùng nhầm ảnh cũ (trước đây cache chỉ theo tên block).
                string cacheKey = (!string.IsNullOrEmpty(item.VisibilityState) && options.SplitByVisibility)
                    ? $"{item.BlockName}_{item.VisibilityState}"
                    : item.BlockName;

                string thumb = ThumbnailGenerator.GetOrCreateThumbnail(doc, item.SourceBtrId, cacheKey, signatureMemo);
                item.ThumbnailPath = thumb;

                // 2. ShapeHash: LUÔN tính từ DynamicBtrId (block definition gốc)
                if (item.DynamicBtrId.IsValid && !item.DynamicBtrId.IsNull)
                {
                    string defThumb = ThumbnailGenerator.GetOrCreateThumbnail(doc, item.DynamicBtrId, $"{item.BlockName}_defhash", signatureMemo);
                    if (!string.IsNullOrEmpty(defThumb))
                    {
                        item.ShapeHash = ThumbnailGenerator.GetHashFromPngFile(defThumb);
                    }
                }

                items.Add(item);
            }

            // Sắp xếp mặc định theo tên alphabet (tách theo kích thước: cùng tên xếp theo kích thước tăng dần)
            var sorted = items.OrderBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase)
                              .ThenBy(i => DynamicParamText.SizeSortKey(i.Size)).ToList();
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Order = i;

            Logger.Log($"BlockExtractor: Gom thành {sorted.Count} nhóm Block [{options}]");
            if (options.SplitBySize)
            {
                var sized = sorted.Where(i => !string.IsNullOrEmpty(i.Size)).ToList();
                if (sized.Count > 0)
                    Logger.Log($"BlockExtractor: tách theo kích thước -> {sized.Count} dòng có kích thước: " +
                               string.Join("; ", sized.Take(30).Select(i => $"{i.BlockName} [{i.Size}] x{i.Count}")) + (sized.Count > 30 ? "..." : ""));
            }
            return sorted;
        }
    }
}

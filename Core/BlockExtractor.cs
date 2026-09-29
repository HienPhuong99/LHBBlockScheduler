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
        /// <summary>Tách dòng theo giá trị thuộc tính / tham số (khoá "A:TAG", "D:Tên") - Premium P5.</summary>
        public List<string> SplitAttributeKeys { get; set; } = new List<string>();
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
    }

    public static class BlockExtractor
    {
        public static List<ObjectId> LastSelectedObjectIds { get; private set; } = new List<ObjectId>();

        /// <summary>
        /// Cho user quét chọn trên bản vẽ, trích xuất và gom nhóm Block theo ExtractionOptions.
        /// templateFilter khác null (có block mẫu): chỉ block mẫu (hoặc block cha chứa block mẫu) được chọn.
        /// </summary>
        public static List<BlockItem> ExtractFromSelection(Document doc, ExtractionOptions options = null, TemplateLibrary templateFilter = null,
                                                           bool remember = true)
        {
            options ??= new ExtractionOptions();
            var ids = PromptSelection(doc, options, templateFilter, "\nQuét chọn vùng cần thống kê: ");
            if (ids == null) return null;

            // Lệnh Premium chạy riêng (soát lỗi, đánh số...) không ghi đè vùng chọn của form thống kê đang mở
            if (!remember) return ExtractFromObjectIds(doc, ids, options);
            LastSelectedObjectIds = ids.ToList();
            return ExtractFromObjectIds(doc, LastSelectedObjectIds, options);
        }

        /// <summary>
        /// "Quét thêm": chọn thêm vùng, chỉ trích xuất đối tượng CHƯA có trong lần chọn trước (chọn lại vùng cũ không
        /// đếm 2 lần), rồi cộng vào LastSelectedObjectIds. Trả null nếu user huỷ.
        /// </summary>
        public static List<BlockItem> ExtractAdditionalSelection(Document doc, ExtractionOptions options, TemplateLibrary templateFilter,
                                                                 out int newObjects, out int alreadySelected)
        {
            newObjects = alreadySelected = 0;
            var ids = PromptSelection(doc, options, templateFilter, "\nQuét chọn THÊM vùng cần thống kê: ");
            if (ids == null) return null;

            var known = new HashSet<ObjectId>(LastSelectedObjectIds);
            var added = ids.Where(id => !known.Contains(id)).Distinct().ToList();
            alreadySelected = ids.Length - added.Count;
            newObjects = added.Count;
            LastSelectedObjectIds.AddRange(added);
            Logger.Log($"BlockExtractor.ExtractAdditionalSelection: chọn {ids.Length} đối tượng, mới {added.Count}, đã có từ lần trước {alreadySelected} (bỏ qua), " +
                       $"tổng đã chọn {LastSelectedObjectIds.Count}");
            return ExtractFromObjectIds(doc, added, options);
        }

        /// <summary>Nhắc chọn đối tượng. Có block mẫu thì gắn bộ lọc vào sự kiện SelectionAdded: đối tượng không phải
        /// block mẫu bị bỏ ngay khi quét, không sáng lên, không vào vùng chọn.</summary>
        private static ObjectId[] PromptSelection(Document doc, ExtractionOptions options, TemplateLibrary templateFilter, string message)
        {
            var ed = doc.Editor;
            TemplateSelectionFilter filter = null;
            if (templateFilter != null && templateFilter.Entries != null && templateFilter.Entries.Count > 0)
            {
                filter = new TemplateSelectionFilter(doc.Database, templateFilter, options.MaxDepth);
                ed.SelectionAdded += filter.OnSelectionAdded;
                ed.WriteMessage($"\n[LHB] Chỉ quét block mẫu (bộ '{templateFilter.Name}', {templateFilter.Entries.Count} block). " +
                                "Block khác, chữ, nét không được chọn. Tắt ô 'Chỉ quét block mẫu' trên form nếu muốn quét tất cả.");
                message = message.TrimEnd(':', ' ') + " (chỉ dính block mẫu): ";
            }

            try
            {
                var selRes = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = message });
                if (selRes.Status != PromptStatus.OK)
                {
                    Logger.Log("BlockExtractor: user huỷ chọn (status=" + selRes.Status + ")");
                    return null;
                }
                return selRes.Value.GetObjectIds();
            }
            finally
            {
                if (filter != null)
                {
                    ed.SelectionAdded -= filter.OnSelectionAdded;
                    Logger.Log($"[TemplateSelectionFilter] bộ '{templateFilter.Name}': giữ {filter.Kept} block ({filter.KeptAsParent} là block cha chứa block mẫu), " +
                               $"bỏ {filter.Removed} đối tượng không phải block mẫu");
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
                var rx = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference));
                foreach (ObjectId id in ms)
                    if (id.ObjectClass.IsDerivedFrom(rx)) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        private static List<ScannedRef> ScanRefs(Database db, IEnumerable<ObjectId> rootIds, ExtractionOptions options)
        {
            var scannedRefs = new List<ScannedRef>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var visiting = new HashSet<ObjectId>();
                var extCache = new Dictionary<ObjectId, Extents3d?>();

                foreach (var id in rootIds)
                {
                    if (id.IsNull || !id.IsValid || id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!(ent is BlockReference br)) continue;

                    var pathSoFar = new List<ObjectId> { br.ObjectId };
                    WalkBlockReference(tr, br, Matrix3d.Identity, 0, options.MaxDepth, visiting, pathSoFar, scannedRefs, extCache);
                }

                int noCorners = scannedRefs.Count(r => r.Corners == null);
                Logger.Log($"BlockExtractor: quét {scannedRefs.Count} block, {extCache.Count} định nghĩa block khác nhau" +
                           (noCorners > 0 ? $", {noCorners} block không tính được khung bao (chỉ so trùng theo điểm chèn)" : ""));
                tr.Commit();
            }
            return scannedRefs;
        }

        /// <summary>
        /// Thuộc tính (khoá "A:TAG") và tham số dynamic block đang hiện, trừ Visibility (khoá "D:Tên") - Premium P5.
        /// null nếu không có gì.
        /// </summary>
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
                if (br.IsDynamicBlock)
                {
                    foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                    {
                        if (!p.Show || string.IsNullOrEmpty(p.PropertyName)) continue;
                        if (p.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        object v = p.Value;
                        string s;
                        if (v is double dv) s = dv.ToString("0.###");
                        else if (v is short || v is int || v is long || v is string) s = v.ToString();
                        else continue; // Point3d, ObjectId... không hiện được thành cột
                        d ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        d["D:" + p.PropertyName] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"BlockExtractor: không đọc được thuộc tính block Handle={br.Handle}: {ex.Message}");
            }
            return d;
        }

        /// <summary>Khoá thuộc tính -> tên cột dễ đọc: "A:MA_TB" -> "MA_TB", "D:Distance1" -> "Tham số Distance1".</summary>
        public static string AttributeKeyLabel(string key) =>
            key == null ? "" : key.StartsWith("A:") ? key.Substring(2) : key.StartsWith("D:") ? "Tham số " + key.Substring(2) : key;

        /// <summary>
        /// 4 góc khung bao của block theo WCS: khung bao trong toạ độ định nghĩa block (BTR thực của instance, với dynamic
        /// block là BTR ẩn danh đúng chủng loại) rồi biến đổi theo ma trận của block. Cache extents theo BTR vì
        /// hàng nghìn block cùng định nghĩa (sprinkler) chỉ cần tính 1 lần.
        /// </summary>
        private static Point2d[] ComputeCorners(Transaction tr, BlockReference br, Matrix3d xform, Dictionary<ObjectId, Extents3d?> extCache)
        {
            ObjectId btrId = br.BlockTableRecord;
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
            return new[]
            {
                new Point3d(mn.X, mn.Y, z), new Point3d(mx.X, mn.Y, z),
                new Point3d(mx.X, mx.Y, z), new Point3d(mn.X, mx.Y, z)
            }.Select(p => { var w = p.TransformBy(xform); return new Point2d(w.X, w.Y); }).ToArray();
        }

        /// <summary>
        /// Chủng loại của block: giá trị tham số Visibility của dynamic block; dynamic block không có Visibility thì
        /// ghép các tham số đang hiện; block thường = "". Dùng chung cho quét thống kê và thư viện block mẫu.
        /// </summary>
        public static string ReadVisibility(BlockReference br, string realName)
        {
            if (!br.IsDynamicBlock) return "";
            try
            {
                DynamicBlockReferenceProperty primaryProp = null;
                var allProps = new List<string>();

                foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                {
                    if (!prop.Show) continue;
                    string pName = prop.PropertyName;
                    if (pName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        primaryProp = prop;
                        break;
                    }
                    allProps.Add($"{pName}={prop.Value}");
                }

                if (primaryProp != null && primaryProp.Value != null) return primaryProp.Value.ToString();
                if (allProps.Count > 0) return string.Join(" - ", allProps);
            }
            catch (Exception ex)
            {
                Logger.Warn($"BlockExtractor: không đọc được dynamic property cho '{realName}': {ex.Message}");
            }
            return "";
        }

        private static void WalkBlockReference(Transaction tr, BlockReference br, Matrix3d parentXform,
                                              int depth, int maxDepth, HashSet<ObjectId> visiting,
                                              List<ObjectId> pathSoFar, List<ScannedRef> results,
                                              Dictionary<ObjectId, Extents3d?> extCache)
        {
            try
            {
                ObjectId dynBtrId = br.DynamicBlockTableRecord;
                var btrDef = (BlockTableRecord)tr.GetObject(dynBtrId, OpenMode.ForRead);
                string realName = btrDef.Name;

                // Bỏ qua block ẩn danh hệ thống (*Model_Space, v.v.), nhưng GIỮ và đếm block A$C...
                if (string.IsNullOrEmpty(realName) || (realName.StartsWith("*") && !br.IsDynamicBlock))
                    return;

                // Xác định BlockKind
                string kind;
                if (br.IsDynamicBlock) kind = "Động";
                else if (btrDef.HasAttributeDefinitions) kind = "Có thuộc tính";
                else kind = "Tĩnh";

                string visibility = ReadVisibility(br, realName);

                Matrix3d currentXform = parentXform * br.BlockTransform;
                var cs = currentXform.CoordinateSystem3d;
                bool mirrored = cs.Xaxis.CrossProduct(cs.Yaxis).Z < 0;

                var currentRef = new ScannedRef
                {
                    BrId = br.ObjectId,
                    DynamicBtrId = dynBtrId,
                    InstanceBtrId = br.BlockTableRecord,
                    BlockName = realName,
                    BlockKind = kind,
                    VisibilityState = visibility,
                    Layer = br.Layer,
                    Depth = depth,
                    ContainerPath = pathSoFar.ToArray(),
                    Transform = currentXform,
                    OwnerTransform = parentXform,
                    Position = br.Position.TransformBy(parentXform),
                    Corners = ComputeCorners(tr, br, currentXform, extCache),
                    HasChildren = false,
                    ScaleX = cs.Xaxis.Length,
                    ScaleY = mirrored ? -cs.Yaxis.Length : cs.Yaxis.Length,
                    Rotation = Math.Atan2(cs.Xaxis.Y, cs.Xaxis.X),
                    Attributes = ReadAttributes(tr, br)
                };
                results.Add(currentRef);

                // Đệ quy quét block con nếu chưa vượt quá maxDepth
                if (depth + 1 < maxDepth)
                {
                    ObjectId instanceBtrId = br.BlockTableRecord;
                    if (!visiting.Contains(instanceBtrId))
                    {
                        visiting.Add(instanceBtrId);
                        var childBtr = tr.GetObject(instanceBtrId, OpenMode.ForRead) as BlockTableRecord;
                        if (childBtr != null && !childBtr.IsLayout)
                        {
                            bool foundChild = false;
                            foreach (ObjectId childId in childBtr)
                            {
                                var childEnt = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                                if (childEnt is BlockReference childBr)
                                {
                                    foundChild = true;
                                    var childPath = new List<ObjectId>(pathSoFar) { childBr.ObjectId };
                                    WalkBlockReference(tr, childBr, currentXform, depth + 1, maxDepth, visiting, childPath, results, extCache);
                                }
                            }
                            if (foundChild)
                            {
                                currentRef.HasChildren = true;
                            }
                        }
                        visiting.Remove(instanceBtrId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BlockExtractor.WalkBlockReference");
            }
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

            var items = new List<BlockItem>();
            foreach (var kvp in groupDict)
            {
                var list = kvp.Value;
                var first = list[0];

                string displayName = first.BlockName;
                if (Regex.IsMatch(first.BlockName, @"^A\$[A-Za-z]") && !string.IsNullOrEmpty(first.VisibilityState))
                {
                    displayName = first.VisibilityState;
                }

                var item = new BlockItem
                {
                    BlockName = first.BlockName,
                    DisplayName = displayName,
                    VisibilityState = first.VisibilityState,
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
                        Attributes = x.Attributes
                    }).ToList()
                };

                // Database khác (không có Document) -> không tạo ảnh
                if (doc == null)
                {
                    items.Add(item);
                    continue;
                }

                // 1. Ảnh hiển thị trên grid và ô ký hiệu: render từ SourceBtrId (InstanceBtrId khi tách theo chủng loại)
                string cacheKey = (!string.IsNullOrEmpty(item.VisibilityState) && options.SplitByVisibility)
                    ? $"{item.BlockName}_{item.VisibilityState}"
                    : item.BlockName;

                string thumb = ThumbnailGenerator.GetOrCreateThumbnail(doc, item.SourceBtrId, cacheKey);
                item.ThumbnailPath = thumb;

                // 2. ShapeHash: LUÔN tính từ DynamicBtrId (block definition gốc bất biến xuyên bản vẽ)
                if (item.DynamicBtrId.IsValid && !item.DynamicBtrId.IsNull)
                {
                    string defThumb = ThumbnailGenerator.GetOrCreateThumbnail(doc, item.DynamicBtrId, $"{item.BlockName}_defhash");
                    if (!string.IsNullOrEmpty(defThumb))
                    {
                        item.ShapeHash = ThumbnailGenerator.GetHashFromPngFile(defThumb);
                    }
                }

                items.Add(item);
            }

            // Sắp xếp mặc định theo tên alphabet
            var sorted = items.OrderBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Order = i;

            Logger.Log($"BlockExtractor: Gom thành {sorted.Count} nhóm Block (Depth={options.MaxDepth}, CountParents={options.CountParentBlocks}, SplitVis={options.SplitByVisibility}, SplitLay={options.SplitByLayer})");
            return sorted;
        }
    }
}

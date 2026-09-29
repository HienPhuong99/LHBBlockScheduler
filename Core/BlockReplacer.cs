using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Thay block hàng loạt (Premium P12): mọi block của các dòng đã chọn đổi sang block đích (block trong bản vẽ hoặc
    /// block mẫu), giữ điểm chèn, góc xoay, layer; tỉ lệ giữ nguyên hoặc co theo kích thước block cũ; chép thuộc tính
    /// cùng tag; đặt chủng loại (visibility) cho block đích dynamic. Block lồng trong block cha không thay (báo số lượng).
    /// Ctrl+Z trong AutoCAD hoàn tác được.
    /// </summary>
    public static class BlockReplacer
    {
        public class Options
        {
            public ObjectId TargetBtrId;
            public string TargetVisibility = "";
            public bool FitSize;
            public bool KeepRotation = true;
            public bool KeepLayer = true;
            public bool CopyAttributes = true;
        }

        public class Result
        {
            public int Replaced;
            public int SkippedNested;
            public List<ObjectId> OldIds = new List<ObjectId>();
            public List<ObjectId> NewIds = new List<ObjectId>();
        }

        /// <summary>Tên block thường trong bản vẽ (bỏ block ẩn danh, layout, xref) -> ObjectId.</summary>
        public static List<(string Name, ObjectId Id)> ListBlocks(Database db)
        {
            var list = new List<(string, ObjectId)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference || btr.IsDependent || btr.Name.StartsWith("*")) continue;
                    if (btr.Name.StartsWith("LHB_SYM_") || btr.Name.StartsWith("LHB_IMG_")) continue;
                    list.Add((btr.Name, id));
                }
                tr.Commit();
            }
            return list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Các chủng loại (visibility state) của 1 block dynamic: chèn tạm 1 block vào Model Space trong transaction
        /// rồi huỷ transaction (không để lại gì trên bản vẽ).
        /// </summary>
        public static List<string> VisibilityStates(Document doc, ObjectId btrId)
        {
            var result = new List<string>();
            var db = doc.Database;
            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (btr.IsDynamicBlock)
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var br = new BlockReference(Point3d.Origin, btrId);
                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                        foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                        {
                            if (p.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            foreach (var v in p.GetAllowedValues()) result.Add(v?.ToString());
                            break;
                        }
                    }
                    tr.Abort();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BlockReplacer] Đọc chủng loại block {btrId}: {ex.Message}");
            }
            return result.Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public static Result Replace(Document doc, List<BlockItem> items, Options o)
        {
            var res = new Result();
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var target = (BlockTableRecord)tr.GetObject(o.TargetBtrId, OpenMode.ForRead);
                double targetSize = 0;
                if (o.FitSize && ExtentsHelper.TryGetBtrExtents(tr, target, out var te))
                    targetSize = Math.Max(te.MaxPoint.X - te.MinPoint.X, te.MaxPoint.Y - te.MinPoint.Y);
                var attDefs = new List<AttributeDefinition>();
                foreach (ObjectId id in target)
                    if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant) attDefs.Add(ad);

                foreach (var item in items)
                {
                    foreach (var inst in item.Instances ?? new List<BlockInstanceRef>())
                    {
                        if (!inst.IsTopLevel) { res.SkippedNested++; continue; }
                        var oldId = inst.Path[0];
                        if (oldId.IsErased || res.OldIds.Contains(oldId)) continue;
                        try
                        {
                            var old = (BlockReference)tr.GetObject(oldId, OpenMode.ForWrite);
                            var owner = (BlockTableRecord)tr.GetObject(old.OwnerId, OpenMode.ForWrite);
                            var scale = old.ScaleFactors;
                            if (o.FitSize && targetSize > 0 && ExtentsHelper.TryGetExtents(old, out var oe))
                            {
                                double oldSize = Math.Max(oe.MaxPoint.X - oe.MinPoint.X, oe.MaxPoint.Y - oe.MinPoint.Y);
                                double k = oldSize / targetSize;
                                scale = new Scale3d(k * Math.Sign(scale.X == 0 ? 1 : scale.X), k * Math.Sign(scale.Y == 0 ? 1 : scale.Y), k);
                            }
                            var nb = new BlockReference(old.Position, o.TargetBtrId)
                            {
                                Normal = old.Normal,
                                Rotation = o.KeepRotation ? old.Rotation : 0,
                                ScaleFactors = scale
                            };
                            nb.SetDatabaseDefaults(db);
                            if (o.KeepLayer) nb.Layer = old.Layer;
                            owner.AppendEntity(nb);
                            tr.AddNewlyCreatedDBObject(nb, true);

                            // Thuộc tính: tạo theo định nghĩa đích, lấy giá trị cũ cùng tag
                            var oldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (ObjectId aid in old.AttributeCollection)
                                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference oar) oldValues[oar.Tag] = oar.TextString;
                            foreach (var ad in attDefs)
                            {
                                var ar = new AttributeReference();
                                ar.SetAttributeFromBlock(ad, nb.BlockTransform);
                                if (o.CopyAttributes && oldValues.TryGetValue(ad.Tag, out var v)) ar.TextString = v;
                                nb.AttributeCollection.AppendAttribute(ar);
                                tr.AddNewlyCreatedDBObject(ar, true);
                            }

                            if (!string.IsNullOrEmpty(o.TargetVisibility) && nb.IsDynamicBlock)
                                foreach (DynamicBlockReferenceProperty p in nb.DynamicBlockReferencePropertyCollection)
                                    if (p.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0 && !p.ReadOnly)
                                    {
                                        p.Value = o.TargetVisibility;
                                        break;
                                    }

                            old.Erase();
                            res.OldIds.Add(oldId);
                            res.NewIds.Add(nb.ObjectId);
                            res.Replaced++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"[BlockReplacer] Thay block {oldId.Handle}");
                        }
                    }
                }
                tr.Commit();
                Logger.Log($"[BlockReplacer] Thay {res.Replaced} block sang '{target.Name}'" +
                           (string.IsNullOrEmpty(o.TargetVisibility) ? "" : $" [{o.TargetVisibility}]") +
                           $", bỏ {res.SkippedNested} block lồng, co theo kích thước={o.FitSize}, giữ góc={o.KeepRotation}, giữ layer={o.KeepLayer}");
            }
            return res;
        }
    }
}

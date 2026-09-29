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
    /// Đánh số thiết bị tự động (Premium P7): SP-01, SP-02... cho từng loại thiết bị, theo thứ tự trái -> phải rồi
    /// trên -> dưới (hoặc trên -> dưới rồi trái -> phải, hoặc theo khu vực). Ghi số vào thuộc tính của block (nếu chọn
    /// tag) và / hoặc tạo chữ cạnh block trên layer LHB_DANHSO (in được). Block thừa do trùng không đánh số.
    /// </summary>
    public static class DeviceNumbering
    {
        public const string TextLayer = "LHB_DANHSO";

        public enum SortOrder { RowsLeftToRight, ColumnsTopToBottom, ZoneThenRows }

        public class TypeSpec
        {
            public BlockItem Item;
            public string Prefix;
            public int Start = 1;
        }

        public class Options
        {
            public int Digits = 2;
            public SortOrder Order = SortOrder.RowsLeftToRight;
            public bool CreateText = true;
            public double TextHeight;
            /// <summary>Tag thuộc tính để ghi số ("" = không ghi thuộc tính).</summary>
            public string AttributeTag = "";
            /// <summary>Đánh số lại từ đầu trong mỗi khu vực, tiền tố thêm tên khu vực.</summary>
            public bool RestartPerZone;
        }

        /// <summary>Tiền tố gợi ý: chữ cái đầu mỗi từ của tên thiết bị (bỏ dấu) + "-", vd "Đầu báo khói" -> "DBK-".</summary>
        public static string SuggestPrefix(BlockItem item)
        {
            var saved = SettingsManager.Current.NumberingPrefixes;
            if (saved != null && saved.TryGetValue(DrawingHelper.ItemKey(item), out var p) && !string.IsNullOrWhiteSpace(p)) return p;
            string name = VietnameseHelper.RemoveDiacritics(item.DisplayName ?? item.BlockName ?? "TB").ToUpperInvariant();
            var words = name.Split(new[] { ' ', '_', '-', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => char.IsLetterOrDigit(w[0])).ToList();
            string init = new string(words.Take(4).Select(w => w[0]).ToArray());
            return (init.Length > 0 ? init : "TB") + "-";
        }

        /// <summary>Đánh số. Trả số block đã đánh.</summary>
        public static int Apply(Document doc, List<TypeSpec> specs, Options o)
        {
            var db = doc.Database;
            var zones = o.Order == SortOrder.ZoneThenRows || o.RestartPerZone ? ZoneManager.Load(db) : new List<LoadedZone>();
            int total = 0, attrWritten = 0, texts = 0;
            double typical = CountChecker.TypicalSize(specs.Select(s => s.Item));
            double textH = o.TextHeight > 0 ? o.TextHeight : typical * 0.35;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var layerId = o.CreateText ? DrawingHelper.GetOrCreateLayer(db, tr, TextLayer, 3, true) : ObjectId.Null;

                foreach (var spec in specs)
                {
                    var insts = (spec.Item.Instances ?? new List<BlockInstanceRef>()).Where(i => !i.IsExcludedDuplicate).ToList();
                    if (insts.Count == 0) continue;
                    var groups = o.RestartPerZone
                        ? insts.GroupBy(i => ZoneManager.Find(zones, i.Position)?.Name ?? "").ToList()
                        : insts.GroupBy(i => "").ToList();

                    foreach (var g in groups)
                    {
                        var ordered = Sort(g.ToList(), o.Order, zones, typical);
                        int n = spec.Start;
                        string prefix = spec.Prefix ?? "";
                        if (o.RestartPerZone && g.Key.Length > 0) prefix = g.Key + "." + prefix;
                        foreach (var inst in ordered)
                        {
                            string label = prefix + n.ToString(new string('0', Math.Max(1, o.Digits)));
                            n++;
                            total++;

                            if (!string.IsNullOrEmpty(o.AttributeTag) && inst.IsTopLevel && WriteAttribute(tr, inst.Path[0], o.AttributeTag, label))
                                attrWritten++;

                            if (o.CreateText)
                            {
                                var pos = TextPosition(inst, textH);
                                var t = new DBText
                                {
                                    TextString = label,
                                    Height = textH,
                                    Position = pos
                                };
                                t.SetDatabaseDefaults(db);
                                t.LayerId = layerId;
                                ms.AppendEntity(t);
                                tr.AddNewlyCreatedDBObject(t, true);
                                texts++;
                            }
                        }
                    }
                    Logger.Log($"[DeviceNumbering] '{spec.Item.BlockName}' ({spec.Item.VisibilityState}): tiền tố '{spec.Prefix}', bắt đầu {spec.Start}, {insts.Count} block");
                }
                tr.Commit();
            }
            Logger.Log($"[DeviceNumbering] Đánh số {total} block: {texts} chữ trên layer {TextLayer}, {attrWritten} thuộc tính '{o.AttributeTag}', thứ tự {o.Order}");
            return total;
        }

        /// <summary>Xoá mọi số đã đánh dạng chữ (layer LHB_DANHSO).</summary>
        public static int ClearTexts(Document doc) => DrawingHelper.EraseOnLayer(doc, TextLayer);

        private static List<BlockInstanceRef> Sort(List<BlockInstanceRef> insts, SortOrder order, List<LoadedZone> zones, double typical)
        {
            double band = typical * 0.6; // các block lệch nhau dưới ~nửa kích thước block coi như cùng hàng / cột
            IEnumerable<BlockInstanceRef> RowsLR(IEnumerable<BlockInstanceRef> s) =>
                s.OrderByDescending(i => Math.Round(i.Position.Y / band)).ThenBy(i => i.Position.X);
            switch (order)
            {
                case SortOrder.ColumnsTopToBottom:
                    return insts.OrderBy(i => Math.Round(i.Position.X / band)).ThenByDescending(i => i.Position.Y).ToList();
                case SortOrder.ZoneThenRows:
                    var zoneIndex = zones.Select((z, k) => (z, k)).ToDictionary(x => x.z, x => x.k);
                    return insts.GroupBy(i => ZoneManager.Find(zones, i.Position))
                                .OrderBy(g => g.Key == null ? int.MaxValue : zoneIndex[g.Key])
                                .SelectMany(g => RowsLR(g)).ToList();
                default:
                    return RowsLR(insts).ToList();
            }
        }

        /// <summary>Chữ đặt ở góc trên-phải khung bao block, lệch ra ngoài 1 chút.</summary>
        private static Point3d TextPosition(BlockInstanceRef inst, double textH)
        {
            if (inst.Corners != null && inst.Corners.Length == 4)
            {
                double maxX = inst.Corners.Max(c => c.X), maxY = inst.Corners.Max(c => c.Y);
                return new Point3d(maxX + textH * 0.3, maxY + textH * 0.3, inst.Position.Z);
            }
            return inst.Position + new Vector3d(textH, textH, 0);
        }

        private static bool WriteAttribute(Transaction tr, ObjectId brId, string tag, string value)
        {
            try
            {
                if (!(tr.GetObject(brId, OpenMode.ForRead) is BlockReference br)) return false;
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var ar = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    if (!string.Equals(ar.Tag, tag, StringComparison.OrdinalIgnoreCase)) continue;
                    ar.UpgradeOpen();
                    ar.TextString = value;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DeviceNumbering] Ghi thuộc tính '{tag}' block {brId.Handle}: {ex.Message}");
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Vẽ vùng bảo vệ PCCC (Premium P8): vòng tròn bán kính bảo vệ quanh từng đầu báo / đầu phun... (bán kính do user
    /// nhập theo tiêu chuẩn áp dụng, add-in KHÔNG tự tra tiêu chuẩn), tuỳ chọn tô mờ. Soát khoảng hở: 2 thiết bị cùng
    /// loại gần nhau nhất mà cách nhau quá 2 x bán kính (2 vòng không chạm) -> vẽ đoạn nối đỏ + khoảng cách.
    /// Tất cả trên layer LHB_VUNGBAOVE (không in), xoá bằng nút "Xoá vùng bảo vệ".
    /// </summary>
    public static class CoverageDrawer
    {
        public const string LayerName = "LHB_VUNGBAOVE";

        public class Spec
        {
            public BlockItem Item;
            /// <summary>Bán kính bảo vệ (m).</summary>
            public double RadiusM;
            public short ColorIndex = 4;
        }

        public static (int Circles, int Gaps) Draw(Document doc, List<Spec> specs, bool fill, bool checkGaps)
        {
            var db = doc.Database;
            double unitsPerM = 1000.0 / DrawingHelper.MmPerUnit(db);
            int circles = 0, gaps = 0;
            DrawingHelper.EraseOnLayer(doc, LayerName);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var layerId = DrawingHelper.GetOrCreateLayer(db, tr, LayerName, 4, false);
                foreach (var s in specs.Where(x => x.RadiusM > 0))
                {
                    double r = s.RadiusM * unitsPerM;
                    var pts = (s.Item.Instances ?? new List<BlockInstanceRef>()).Where(i => !i.IsExcludedDuplicate).Select(i => i.Position).ToList();
                    var color = Color.FromColorIndex(ColorMethod.ByAci, s.ColorIndex > 0 ? s.ColorIndex : (short)4);
                    foreach (var p in pts)
                    {
                        var c = new Circle(new Point3d(p.X, p.Y, p.Z), Vector3d.ZAxis, r);
                        Add(tr, ms, db, layerId, c, color);
                        circles++;
                        if (fill) AddFill(tr, ms, db, layerId, c, color);
                    }

                    if (checkGaps && pts.Count > 1)
                    {
                        var drawn = new HashSet<(int, int)>();
                        for (int i = 0; i < pts.Count; i++)
                        {
                            int best = -1;
                            double bd = double.MaxValue;
                            for (int j = 0; j < pts.Count; j++)
                            {
                                if (i == j) continue;
                                double d = pts[i].DistanceTo(pts[j]);
                                if (d < bd) { bd = d; best = j; }
                            }
                            if (best < 0 || bd <= 2 * r) continue;
                            var key = (Math.Min(i, best), Math.Max(i, best));
                            if (!drawn.Add(key)) continue;
                            var red = Color.FromColorIndex(ColorMethod.ByAci, 1);
                            Add(tr, ms, db, layerId, new Line(pts[i], pts[best]), red);
                            var mid = pts[i] + (pts[best] - pts[i]) / 2;
                            Add(tr, ms, db, layerId, new DBText
                            {
                                Position = mid,
                                Height = Math.Max(r * 0.12, 1e-3),
                                TextString = $"{bd / unitsPerM:0.##}m > 2R={2 * s.RadiusM:0.##}m"
                            }, red);
                            gaps++;
                        }
                    }
                    Logger.Log($"[CoverageDrawer] '{s.Item.BlockName}' ({s.Item.VisibilityState}): R={s.RadiusM} m = {r:0.#} đơn vị, {pts.Count} vòng");
                }
                tr.Commit();
            }
            Logger.Log($"[CoverageDrawer] Vẽ {circles} vòng bảo vệ, {gaps} khoảng hở (1 m = {unitsPerM:0.###} đơn vị bản vẽ)");
            return (circles, gaps);
        }

        public static int Clear(Document doc) => DrawingHelper.EraseOnLayer(doc, LayerName);

        private static void Add(Transaction tr, BlockTableRecord ms, Database db, ObjectId layerId, Entity e, Color color)
        {
            e.SetDatabaseDefaults(db);
            e.LayerId = layerId;
            e.Color = color;
            ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }

        private static void AddFill(Transaction tr, BlockTableRecord ms, Database db, ObjectId layerId, Circle boundary, Color color)
        {
            try
            {
                var h = new Hatch();
                h.SetDatabaseDefaults(db);
                h.LayerId = layerId;
                h.Color = color;
                h.Transparency = new Transparency((byte)64); // alpha 64/255 ~ trong suốt 75%
                ms.AppendEntity(h);
                tr.AddNewlyCreatedDBObject(h, true);
                h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                h.Associative = false;
                h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { boundary.ObjectId });
                h.EvaluateHatch(true);
                h.DrawOrder(tr, ms);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[CoverageDrawer] Tô mờ vòng bảo vệ lỗi: {ex.Message}");
            }
        }

        /// <summary>Đưa hatch xuống dưới cùng để không che thiết bị.</summary>
        private static void DrawOrder(this Hatch h, Transaction tr, BlockTableRecord ms)
        {
            try
            {
                var dot = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
                dot.MoveToBottom(new ObjectIdCollection { h.ObjectId });
            }
            catch { }
        }
    }
}

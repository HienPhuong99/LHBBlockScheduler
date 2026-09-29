using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Tìm block trùng (user lỡ copy đè 2 block lên nhau -> SL đếm dư).
    /// Trùng = CÙNG TÊN BLOCK (khác chủng loại vẫn tính, yêu cầu 29/09/2026) và:
    ///  - điểm chèn cách nhau không quá sai số, HOẶC
    ///  - khung bao (xoay theo block) che lấp nhau từ DuplicateOverlapPercent % diện tích block nhỏ hơn trở lên
    ///    (2 block khác tỉ lệ / lệch điểm chèn nhưng đè lên nhau, ảnh test 29/09/2026).
    /// Đánh dấu chỗ trùng bằng vòng tròn + đường dẫn từ bảng xuất, vẽ nét thật trên layer riêng không in.
    /// </summary>
    public static class DuplicateFinder
    {
        public const string MarkerLayerName = "LHB_BLOCK_TRUNG";
        private const int MaxDetailLogs = 30;

        // ============================== TÌM TRÙNG ==============================

        /// <summary>Tính lại DuplicateGroups cho mọi dòng. Không đổi Count (xem SetExclusion).</summary>
        public static void Detect(IEnumerable<BlockItem> items, double tolerance, double overlapPercent)
        {
            double tol = tolerance > 0 ? tolerance : 1.0;
            double minOverlap = (overlapPercent > 0 ? overlapPercent : 10.0) / 100.0;
            var itemList = items.ToList();

            // Gom mọi block của mọi dòng, gắn lại dòng đang chứa (gộp dòng làm block đổi dòng)
            var all = new List<BlockInstanceRef>();
            foreach (var item in itemList)
            {
                item.DuplicateGroups = new List<DuplicateGroup>();
                if (item.Instances == null) continue;
                foreach (var inst in item.Instances)
                {
                    inst.Item = item;
                    inst.Group = null;
                    all.Add(inst);
                }
            }

            var groups = new List<DuplicateGroup>();
            int pairsChecked = 0;
            foreach (var byName in all.GroupBy(i => i.BlockName ?? i.Key ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var list = byName.ToList();
                if (list.Count < 2) continue;
                groups.AddRange(FindGroups(byName.Key, list, tol, minOverlap, ref pairsChecked));
            }

            int extra = 0, nested = 0, overlapGroups = 0, logged = 0;
            foreach (var g in groups)
            {
                foreach (var it in g.Items) it.DuplicateGroups.Add(g);
                extra += g.Extra;
                nested += g.Instances.Count(i => !i.IsTopLevel);
                if (g.HasOverlap) overlapGroups++;
                // Form tính lại trùng sau mỗi thao tác -> chỉ log chi tiết 30 vị trí đầu, không làm phình log
                if (++logged > MaxDetailLogs) continue;
                Logger.Log($"[DuplicateFinder] '{g.BlockName}': {g.Instances.Count} block trùng tại ({g.Position.X:0.##}, {g.Position.Y:0.##}), " +
                           (g.HasOverlap ? $"che lấp tới {g.MaxOverlap:P0}" : "cùng điểm chèn") +
                           $", chủng loại [{string.Join(" / ", g.Items.Select(i => i.VisibilityState ?? "").Distinct())}]" +
                           $", handle [{string.Join(", ", g.Instances.Select(i => i.Path.Last().Handle))}], giữ {g.Keep.Path.Last().Handle}");
            }
            if (logged > MaxDetailLogs) Logger.Log($"[DuplicateFinder] ... và {logged - MaxDetailLogs} vị trí trùng khác (không ghi chi tiết)");

            Logger.Log($"[DuplicateFinder.Detect] sai số={tol}, che lấp≥{minOverlap:P0}: {all.Count} block, {pairsChecked} cặp cần so, " +
                       $"{groups.Count} vị trí trùng ({overlapGroups} do che lấp), thừa {extra} block ({nested} block nằm trong block cha)");
        }

        /// <summary>
        /// Các block cùng tên: quét theo trục X trên khung bao trục (nới thêm sai số) để chỉ so các cặp nằm gần nhau,
        /// cặp nào trùng thì nối vào cùng nhóm (union-find) -> 3 block chồng nhau thành 1 nhóm, thừa 2.
        /// </summary>
        private static List<DuplicateGroup> FindGroups(string blockName, List<BlockInstanceRef> list, double tol, double minOverlap, ref int pairsChecked)
        {
            int n = list.Count;
            var boxes = new (double MinX, double MinY, double MaxX, double MaxY)[n];
            var polys = new List<Point2d>[n];
            var areas = new double[n];
            for (int i = 0; i < n; i++)
            {
                var p = list[i].Position;
                double mnX = p.X, mnY = p.Y, mxX = p.X, mxY = p.Y;
                if (list[i].Corners != null && list[i].Corners.Length == 4)
                {
                    polys[i] = ToCcw(list[i].Corners);
                    areas[i] = Math.Abs(PolygonArea(polys[i]));
                    foreach (var c in list[i].Corners)
                    {
                        mnX = Math.Min(mnX, c.X); mnY = Math.Min(mnY, c.Y);
                        mxX = Math.Max(mxX, c.X); mxY = Math.Max(mxY, c.Y);
                    }
                }
                boxes[i] = (mnX - tol / 2, mnY - tol / 2, mxX + tol / 2, mxY + tol / 2);
            }

            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var overlapOf = new Dictionary<int, double>();
            var hasOverlap = new HashSet<int>();

            var order = Enumerable.Range(0, n).OrderBy(i => boxes[i].MinX).ToArray();
            for (int a = 0; a < n; a++)
            {
                int i = order[a];
                for (int b = a + 1; b < n; b++)
                {
                    int j = order[b];
                    if (boxes[j].MinX > boxes[i].MaxX) break;
                    if (boxes[j].MinY > boxes[i].MaxY || boxes[j].MaxY < boxes[i].MinY) continue;
                    pairsChecked++;

                    bool samePoint = Distance2d(list[i].Position, list[j].Position) <= tol;
                    double ratio = 0;
                    if (polys[i] != null && polys[j] != null)
                    {
                        double minArea = Math.Min(areas[i], areas[j]);
                        if (minArea > 1e-12)
                            ratio = Math.Abs(PolygonArea(ClipConvex(polys[i], polys[j]))) / minArea;
                    }
                    bool overlap = ratio >= minOverlap;
                    if (!samePoint && !overlap) continue;

                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) parent[ri] = rj;
                    int root = Find(i);
                    // Lưu tỉ lệ che lấp theo gốc mới, gộp cả giá trị của gốc cũ
                    double prev = Math.Max(overlapOf.TryGetValue(ri, out var o1) ? o1 : 0, overlapOf.TryGetValue(rj, out var o2) ? o2 : 0);
                    overlapOf[root] = Math.Max(prev, ratio);
                    if (hasOverlap.Contains(ri) || hasOverlap.Contains(rj) || !samePoint) hasOverlap.Add(root);
                }
            }

            var result = new List<DuplicateGroup>();
            foreach (var cluster in Enumerable.Range(0, n).GroupBy(Find).Where(c => c.Count() > 1))
            {
                var members = cluster.ToList();
                var g = new DuplicateGroup
                {
                    BlockName = blockName,
                    HasOverlap = hasOverlap.Contains(cluster.Key),
                    MaxOverlap = overlapOf.TryGetValue(cluster.Key, out var ov) ? ov : 0
                };
                foreach (int k in members)
                {
                    g.Instances.Add(list[k]);
                    list[k].Group = g;
                }

                // Tâm + bán kính theo khung bao chung (bỏ phần nới sai số)
                double mnX = members.Min(k => boxes[k].MinX) + tol / 2, mxX = members.Max(k => boxes[k].MaxX) - tol / 2;
                double mnY = members.Min(k => boxes[k].MinY) + tol / 2, mxY = members.Max(k => boxes[k].MaxY) - tol / 2;
                g.Position = new Point3d((mnX + mxX) / 2, (mnY + mxY) / 2, list[members[0]].Position.Z);
                g.Radius = Math.Sqrt((mxX - mnX) * (mxX - mnX) + (mxY - mnY) * (mxY - mnY)) / 2;

                // Giữ block lồng (không xoá được) nếu có, không thì block tạo sớm nhất (handle nhỏ nhất)
                g.Keep = g.Instances.FirstOrDefault(i => !i.IsTopLevel)
                         ?? g.Instances.OrderBy(i => i.Path[0].Handle.Value).First();
                result.Add(g);
            }
            return result;
        }

        // ---------- Hình học: diện tích giao 2 tứ giác lồi (khung bao xoay theo block) ----------

        private static double PolygonArea(IList<Point2d> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].X * q.Y - q.X * p[i].Y;
            }
            return a / 2.0;
        }

        /// <summary>Đưa về chiều ngược kim đồng hồ (block mirror có định thức âm -> đảo chiều).</summary>
        private static List<Point2d> ToCcw(Point2d[] pts)
        {
            var list = pts.ToList();
            if (PolygonArea(list) < 0) list.Reverse();
            return list;
        }

        private static double Cross(Point2d a, Point2d b, Point2d p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

        /// <summary>Sutherland–Hodgman: cắt đa giác lồi subject theo đa giác lồi clip (cả 2 ngược kim đồng hồ).</summary>
        private static List<Point2d> ClipConvex(List<Point2d> subject, List<Point2d> clip)
        {
            var output = new List<Point2d>(subject);
            for (int e = 0; e < clip.Count && output.Count > 0; e++)
            {
                var a = clip[e];
                var b = clip[(e + 1) % clip.Count];
                var input = output;
                output = new List<Point2d>();
                var prev = input[input.Count - 1];
                double cPrev = Cross(a, b, prev);
                foreach (var cur in input)
                {
                    double cCur = Cross(a, b, cur);
                    if (cCur >= 0)
                    {
                        if (cPrev < 0) output.Add(Intersect(prev, cur, cPrev, cCur));
                        output.Add(cur);
                    }
                    else if (cPrev >= 0)
                    {
                        output.Add(Intersect(prev, cur, cPrev, cCur));
                    }
                    prev = cur;
                    cPrev = cCur;
                }
            }
            return output;
        }

        private static Point2d Intersect(Point2d p, Point2d q, double cp, double cq)
        {
            double t = cp / (cp - cq);
            return new Point2d(p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y));
        }

        /// <summary>
        /// Bật/tắt "Không đếm block trùng": trừ / cộng lại DuplicateExtra vào Count. Có cờ DuplicatesExcluded
        /// nên gọi nhiều lần không trừ 2 lần. Trước khi đổi Instances/Detect lại phải gọi SetExclusion(false).
        /// </summary>
        public static void SetExclusion(IEnumerable<BlockItem> items, bool exclude)
        {
            foreach (var item in items)
            {
                if (item.DuplicatesExcluded == exclude) continue;
                int extra = item.DuplicateExtra;
                item.Count += exclude ? -extra : extra;
                item.DuplicatesExcluded = exclude;
            }
        }

        private static double Distance2d(Point3d a, Point3d b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>Mọi nhóm trùng, mỗi nhóm 1 lần (nhóm dùng chung giữa các dòng khác chủng loại).</summary>
        public static List<DuplicateGroup> AllGroups(IEnumerable<BlockItem> items) =>
            items.SelectMany(i => i.DuplicateGroups ?? new List<DuplicateGroup>()).Distinct().ToList();

        // ============================== XOÁ BẢN THỪA ==============================

        /// <summary>
        /// Xoá block thừa, mỗi vị trí giữ lại 1. Chỉ xoá block nằm trực tiếp trong Model: block lồng nằm trong
        /// định nghĩa block cha dùng chung, xoá sẽ mất ở mọi bản copy của block cha -> bỏ qua, báo user xoá tay.
        /// Trả về danh sách (dòng, block) đã xoá.
        /// </summary>
        public static List<(BlockItem Item, BlockInstanceRef Instance)> DeleteExtras(Document doc, IEnumerable<BlockItem> items, out int skippedNested)
        {
            var deleted = new List<(BlockItem, BlockInstanceRef)>();
            skippedNested = 0;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // 1 nhóm có thể nằm ở nhiều dòng (khác chủng loại) -> duyệt mỗi nhóm 1 lần
                foreach (var g in AllGroups(items))
                {
                    var keep = g.Keep;
                    foreach (var inst in g.Instances)
                    {
                        if (inst == keep) continue;
                        if (!inst.IsTopLevel)
                        {
                            skippedNested++;
                            continue;
                        }
                        var id = inst.Path[0];
                        if (id.IsNull || !id.IsValid || id.IsErased) continue;
                        tr.GetObject(id, OpenMode.ForWrite).Erase();
                        deleted.Add((inst.Item, inst));
                        Logger.Log($"[DuplicateFinder.DeleteExtras] Xoá '{inst.BlockName}' (dòng '{inst.Item?.DisplayName}', vis='{inst.Item?.VisibilityState}') " +
                                   $"Handle={id.Handle} tại ({inst.Position.X:0.##}, {inst.Position.Y:0.##}), giữ Handle={keep.Path.Last().Handle}" +
                                   (g.HasOverlap ? $" (che lấp {g.MaxOverlap:P0})" : ""));
                    }
                }
                tr.Commit();
            }

            Logger.Log($"[DuplicateFinder.DeleteExtras] Đã xoá {deleted.Count} block thừa, bỏ qua {skippedNested} block nằm trong block cha");
            return deleted;
        }

        // ============================== ZOOM ==============================

        /// <summary>Zoom tới 1 vị trí trùng và highlight các block trùng ở đó.</summary>
        public static void ZoomToGroup(Document doc, DuplicateGroup group, double minViewSize)
        {
            var db = doc.Database;
            Extents3d? ext = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var inst in group.Instances)
                {
                    var id = inst.Path.Last();
                    if (id.IsErased || !id.IsValid) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || !ExtentsHelper.TryGetExtents(ent, out var e)) continue;
                    // Block lồng: extents theo toạ độ block cha -> đổi ra WCS
                    e.TransformBy(inst.OwnerTransform);
                    if (ext == null) ext = e; else { var u = ext.Value; u.AddExtents(e); ext = u; }
                }
                tr.Commit();
            }

            // Khung nhìn tối thiểu để còn thấy xung quanh (block nhỏ zoom sát quá thì không biết nằm đâu)
            var c = group.Position;
            double half = minViewSize / 2.0;
            var view = new Extents3d(new Point3d(c.X - half, c.Y - half, 0), new Point3d(c.X + half, c.Y + half, 0));
            if (ext != null) view.AddExtents(ext.Value);

            ScheduleManager.ZoomToExtents(doc, view);
            ScheduleManager.HighlightPaths(doc, group.Instances.Select(i => i.Path).ToList());
            Logger.Log($"[DuplicateFinder.ZoomToGroup] '{group.BlockName}' tại ({c.X:0.##}, {c.Y:0.##}), {group.Instances.Count} block" +
                       (group.HasOverlap ? $", che lấp {group.MaxOverlap:P0}" : ""));
        }

        // ============================== ĐÁNH DẤU TRÊN BẢN VẼ ==============================

        /// <summary>Khoanh đỏ mọi chỗ trùng (chưa có bảng). Xoá dấu cũ trước.</summary>
        public static int DrawCircles(Document doc, IEnumerable<BlockItem> items, double radius, double textHeight)
        {
            var db = doc.Database;
            int count = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ObjectId layerId = GetOrCreateMarkerLayer(db, tr);
                ClearMarkers(tr, ms, layerId);
                foreach (var g in AllGroups(items))
                {
                    DrawCircle(tr, ms, layerId, g, radius, textHeight);
                    count++;
                }
                tr.Commit();
            }
            Logger.Log($"[DuplicateFinder.DrawCircles] Khoanh {count} vị trí trùng, bán kính={radius:0.#}");
            return count;
        }

        /// <summary>
        /// Vẽ đường dẫn từ mép phải dòng thiết bị trong bảng xuất tới từng chỗ trùng của thiết bị đó.
        /// Gọi trong transaction xuất bảng. Luôn xoá dấu cũ trước (xuất lại bảng = vẽ lại).
        /// </summary>
        public static void DrawTableLeaders(Transaction tr, Database db, BlockTableRecord space,
                                            List<(BlockItem Item, Point3d Anchor)> rows, double radius, double textHeight)
        {
            ObjectId layerId = GetOrCreateMarkerLayer(db, tr);
            ClearMarkers(tr, space, layerId);

            int leaders = 0;
            var circled = new HashSet<DuplicateGroup>();
            foreach (var (item, anchor) in rows)
            {
                if (item.DuplicateGroups == null || item.DuplicateGroups.Count == 0) continue;
                // Đoạn ngang ngắn ra khỏi bảng rồi mới toả đi các hướng -> không cắt qua chữ trong bảng
                var stub = anchor + new Vector3d(radius * 1.5, 0, 0);
                foreach (var g in item.DuplicateGroups)
                {
                    // Nhóm dùng chung giữa 2 dòng (khác chủng loại): khoanh 1 lần, mỗi dòng vẫn có đường dẫn riêng
                    double r = CircleRadius(g, radius);
                    if (circled.Add(g)) DrawCircle(tr, space, layerId, g, radius, textHeight);

                    var target = new Point3d(g.Position.X, g.Position.Y, anchor.Z);
                    var dir = stub - target;
                    var end = dir.Length > r ? target + dir.GetNormal() * r : target;
                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(anchor.X, anchor.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(stub.X, stub.Y), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(end.X, end.Y), 0, 0, 0);
                    pl.Elevation = anchor.Z;
                    AppendMarker(tr, space, layerId, pl);
                    leaders++;
                }
            }
            Logger.Log($"[DuplicateFinder.DrawTableLeaders] Vẽ {leaders} đường dẫn từ bảng tới chỗ trùng (layer {MarkerLayerName})");
        }

        /// <summary>Xoá mọi dấu trùng (lệnh LHBDUPCLEAR, nút Tắt khoanh đỏ).</summary>
        public static int ClearMarkers(Document doc)
        {
            var db = doc.Database;
            int erased;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(MarkerLayerName)) return 0;
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                erased = ClearMarkers(tr, ms, lt[MarkerLayerName]);
                tr.Commit();
            }
            return erased;
        }

        private static int ClearMarkers(Transaction tr, BlockTableRecord space, ObjectId layerId)
        {
            // Gom id trước rồi mới xoá, không xoá khi đang duyệt BlockTableRecord
            var toErase = new List<ObjectId>();
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && ent.LayerId == layerId) toErase.Add(id);
            }
            foreach (var id in toErase) tr.GetObject(id, OpenMode.ForWrite).Erase();
            int erased = toErase.Count;
            if (erased > 0) Logger.Log($"[DuplicateFinder.ClearMarkers] Xoá {erased} nét đánh dấu trùng cũ");
            return erased;
        }

        /// <summary>Vòng khoanh tối thiểu = bán kính chuẩn, to hơn nếu các block chồng nhau chiếm vùng rộng hơn.</summary>
        private static double CircleRadius(DuplicateGroup g, double radius) => Math.Max(radius, g.Radius * 1.15);

        private static void DrawCircle(Transaction tr, BlockTableRecord space, ObjectId layerId, DuplicateGroup g, double radius, double textHeight)
        {
            var center = new Point3d(g.Position.X, g.Position.Y, g.Position.Z);
            double r = CircleRadius(g, radius);
            AppendMarker(tr, space, layerId, new Circle(center, Vector3d.ZAxis, r));
            AppendMarker(tr, space, layerId, new DBText
            {
                Position = center + new Vector3d(r * 0.75, r * 0.75, 0),
                Height = textHeight,
                TextString = "x" + g.Instances.Count
            });
        }

        private static void AppendMarker(Transaction tr, BlockTableRecord space, ObjectId layerId, Entity ent)
        {
            ent.SetDatabaseDefaults(space.Database);
            ent.LayerId = layerId;
            ent.ColorIndex = 256; // ByLayer -> đỏ theo layer
            space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        /// <summary>Layer đỏ, không in: dấu trùng chỉ để nhìn, không lẫn vào bản in.</summary>
        private static ObjectId GetOrCreateMarkerLayer(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(MarkerLayerName)) return lt[MarkerLayerName];

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = MarkerLayerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, 1),
                IsPlottable = false
            };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            Logger.Log($"[DuplicateFinder] Tạo layer '{MarkerLayerName}' (đỏ, không in)");
            return id;
        }
    }
}

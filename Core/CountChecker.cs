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
    /// <summary>1 lỗi / cảnh báo có thể làm sai số lượng.</summary>
    public class CheckIssue
    {
        public string Kind { get; set; }
        public string BlockName { get; set; }
        public string Detail { get; set; }
        public Point3d Position { get; set; }
        /// <summary>Kích thước vùng cần zoom (đơn vị bản vẽ).</summary>
        public double Size { get; set; }
        /// <summary>Đường dẫn block (highlight được block lồng).</summary>
        public ObjectId[] Path { get; set; }
        /// <summary>Nét rời khớp hình block (lỗi block bị explode).</summary>
        public List<ObjectId> LooseIds { get; set; }
    }

    /// <summary>
    /// Soát lỗi đếm (Premium P6), giống báo lỗi của lệnh COUNT (AutoCAD 2022+):
    ///  1. Block trên layer tắt / đóng băng (không thấy trên bản vẽ nhưng vẫn đếm).
    ///  2. Block bị lật (mirror).
    ///  3. Tỉ lệ khác thường so với đa số block cùng loại (x2 trở lên).
    ///  4. 2 tên block khác nhau nhưng hình giống nhau (copy rồi đổi tên / A$C...).
    ///  5. Block trùng / che lấp (đã có ở "Tìm trùng", liệt kê lại cho đủ).
    ///  6. Block nằm ngoài mọi khu vực (khi bản vẽ có khu vực).
    ///  7. Block bị explode thành nét rời (tuỳ chọn, chậm hơn): tìm cụm nét rời trong Model Space khớp hình của block.
    /// </summary>
    public static class CountChecker
    {
        public const string MarkerLayer = "LHB_SOATLOI";

        public static List<CheckIssue> Run(Document doc, List<BlockItem> items, bool findExploded, Action<string> progress = null)
        {
            var db = doc.Database;
            var issues = new List<CheckIssue>();
            double typical = TypicalSize(items);

            // 1. Layer tắt / đóng băng
            progress?.Invoke("Kiểm tra layer...");
            var layerState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (l.IsFrozen || l.IsOff) layerState[l.Name] = l.IsFrozen ? "đóng băng" : "tắt";
                }
                tr.Commit();
            }
            foreach (var it in items)
                foreach (var inst in it.Instances ?? new List<BlockInstanceRef>())
                    if (inst.Layer != null && layerState.TryGetValue(inst.Layer, out var st))
                        issues.Add(Issue("Layer " + st, it, inst, $"Block trên layer '{inst.Layer}' đang {st}: không thấy trên bản vẽ nhưng vẫn được đếm", typical));

            // 2 + 3. Lật / tỉ lệ khác thường
            foreach (var it in items)
            {
                var insts = it.Instances ?? new List<BlockInstanceRef>();
                foreach (var inst in insts.Where(i => i.ScaleX * i.ScaleY < 0))
                    issues.Add(Issue("Block bị lật", it, inst, "Block bị lật (mirror): SL đúng, nhưng ký hiệu có thể ngược chiều", typical));
                if (insts.Count >= 3)
                {
                    var scales = insts.Select(i => Math.Abs(i.ScaleX)).OrderBy(x => x).ToList();
                    double median = scales[scales.Count / 2];
                    if (median > 0)
                        foreach (var inst in insts)
                        {
                            double ratio = Math.Abs(inst.ScaleX) / median;
                            if (ratio >= 2 || ratio <= 0.5)
                                issues.Add(Issue("Tỉ lệ khác thường", it, inst,
                                    $"Tỉ lệ {Math.Abs(inst.ScaleX):0.###} trong khi đa số block cùng loại là {median:0.###} (có thể chèn nhầm tỉ lệ)", typical));
                        }
                }
            }

            // 4. Khác tên, giống hình
            int threshold = SettingsManager.Current.HashThreshold;
            var hashed = items.Where(i => i.ShapeHash != 0 && !string.IsNullOrEmpty(i.BlockName)).ToList();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int a = 0; a < hashed.Count; a++)
                for (int b = a + 1; b < hashed.Count; b++)
                {
                    if (string.Equals(hashed[a].BlockName, hashed[b].BlockName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ThumbnailGenerator.HammingDistance(hashed[a].ShapeHash, hashed[b].ShapeHash) > threshold) continue;
                    string pair = string.Join("|", new[] { hashed[a].BlockName, hashed[b].BlockName }.OrderBy(x => x));
                    if (!reported.Add(pair)) continue;
                    var inst = hashed[b].Instances?.FirstOrDefault();
                    if (inst == null) continue;
                    issues.Add(Issue("Khác tên, giống hình", hashed[b], inst,
                        $"'{hashed[b].BlockName}' ({hashed[b].Count} cái) giống hình '{hashed[a].BlockName}' ({hashed[a].Count} cái): có thể copy rồi đổi tên, đang đếm thành 2 dòng", typical));
                }

            // 5. Trùng
            foreach (var g in DuplicateFinder.AllGroups(items))
                issues.Add(new CheckIssue
                {
                    Kind = "Block trùng / che lấp",
                    BlockName = g.BlockName,
                    Detail = $"{g.Instances.Count} block chồng nhau" + (g.HasOverlap ? $", che lấp {g.MaxOverlap:P0}" : ", cùng điểm chèn"),
                    Position = g.Position,
                    Size = Math.Max(g.Radius * 4, typical * 4),
                    Path = g.Instances.First().Path
                });

            // 6. Ngoài khu vực
            var zones = ZoneManager.Load(db);
            if (zones.Count > 0)
                foreach (var it in items)
                    foreach (var inst in (it.Instances ?? new List<BlockInstanceRef>()).Where(i => ZoneManager.Find(zones, i.Position) == null))
                        issues.Add(Issue("Ngoài khu vực", it, inst, "Block không nằm trong khu vực / tầng nào", typical));

            // 7. Block bị explode
            if (findExploded)
            {
                progress?.Invoke("Tìm block bị explode...");
                try
                {
                    issues.AddRange(FindExploded(db, items, typical));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[CountChecker] tìm block bị explode");
                }
            }

            Logger.Log($"[CountChecker] {issues.Count} lỗi: " + string.Join(", ", issues.GroupBy(i => i.Kind).Select(g => $"{g.Key}={g.Count()}")));
            return issues;
        }

        private static CheckIssue Issue(string kind, BlockItem it, BlockInstanceRef inst, string detail, double typical) => new CheckIssue
        {
            Kind = kind,
            BlockName = it.DisplayName ?? it.BlockName,
            Detail = detail,
            Position = inst.Position,
            Size = typical * 6,
            Path = inst.Path
        };

        /// <summary>Kích thước block điển hình (trung vị cạnh khung bao) - để zoom / dung sai.</summary>
        public static double TypicalSize(IEnumerable<BlockItem> items)
        {
            var sizes = new List<double>();
            foreach (var it in items)
                foreach (var inst in (it.Instances ?? new List<BlockInstanceRef>()).Take(50))
                    if (inst.Corners != null && inst.Corners.Length == 4)
                        sizes.Add(Math.Max(inst.Corners[0].GetDistanceTo(inst.Corners[1]), inst.Corners[1].GetDistanceTo(inst.Corners[2])));
            if (sizes.Count == 0) return 1000;
            sizes.Sort();
            return Math.Max(sizes[sizes.Count / 2], 1e-3);
        }

        // ============================== BLOCK BỊ EXPLODE ==============================

        private class Sig
        {
            public string Type;
            public Point3d P;
            public double Size;
            public ObjectId Id;
        }

        private static Sig ToSig(Entity e)
        {
            switch (e)
            {
                case Line l: return new Sig { Type = "L", P = l.StartPoint + (l.EndPoint - l.StartPoint) / 2, Size = l.Length };
                case Circle c: return new Sig { Type = "C", P = c.Center, Size = c.Radius };
                case Arc a: return new Sig { Type = "A", P = a.Center, Size = a.Radius };
                case Polyline p when p.NumberOfVertices > 1:
                    {
                        double x = 0, y = 0;
                        for (int i = 0; i < p.NumberOfVertices; i++) { var v = p.GetPoint2dAt(i); x += v.X; y += v.Y; }
                        return new Sig { Type = "P", P = new Point3d(x / p.NumberOfVertices, y / p.NumberOfVertices, p.Elevation), Size = p.Length };
                    }
                default: return null;
            }
        }

        /// <summary>
        /// Với mỗi loại block: lấy chữ ký hình (đường, tròn, cung, polyline) trong định nghĩa, dò các nét rời trong Model
        /// Space cùng loại + cùng kích thước với nét lớn nhất (nét neo), thử 4 góc xoay 0/90/180/270 và tỉ lệ thường dùng,
        /// khớp từ 70% số nét (tối thiểu 3 nét) -> báo "có thể block bị explode".
        /// </summary>
        private static List<CheckIssue> FindExploded(Database db, List<BlockItem> items, double typical)
        {
            var issues = new List<CheckIssue>();
            var loose = new List<Sig>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e is BlockReference) continue;
                    var s = ToSig(e);
                    if (s == null) continue;
                    s.Id = id;
                    loose.Add(s);
                }

                double cell = Math.Max(typical, 1e-3);
                var grid = new Dictionary<(long, long, string), List<Sig>>();
                foreach (var s in loose)
                {
                    var k = ((long)Math.Floor(s.P.X / cell), (long)Math.Floor(s.P.Y / cell), s.Type);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Sig>();
                    l.Add(s);
                }
                var used = new HashSet<ObjectId>();

                foreach (var it in items)
                {
                    if (it.SourceBtrId.IsNull || !it.SourceBtrId.IsValid) continue;
                    var btr = tr.GetObject(it.SourceBtrId, OpenMode.ForRead) as BlockTableRecord;
                    if (btr == null) continue;
                    var sigs = new List<Sig>();
                    foreach (ObjectId id in btr)
                    {
                        if (sigs.Count >= 60) break;
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity e && e.Visible)
                        {
                            var s = ToSig(e);
                            if (s != null && s.Size > 1e-9) sigs.Add(s);
                        }
                    }
                    if (sigs.Count < 3) continue;

                    var scales = (it.Instances ?? new List<BlockInstanceRef>()).Select(i => Math.Abs(i.ScaleX)).Where(x => x > 0).OrderBy(x => x).ToList();
                    double scale = scales.Count > 0 ? scales[scales.Count / 2] : 1;
                    var anchor = sigs.OrderByDescending(s => s.Size).First();
                    double tol = Math.Max(sigs.Max(s => s.P.DistanceTo(anchor.P)) * scale * 0.02, 1e-6);
                    int found = 0;

                    foreach (var cand in loose.Where(l => l.Type == anchor.Type && !used.Contains(l.Id) &&
                                                          Math.Abs(l.Size - anchor.Size * scale) <= Math.Max(anchor.Size * scale * 0.02, 1e-6)))
                    {
                        foreach (double ang in new[] { 0, Math.PI / 2, Math.PI, 1.5 * Math.PI })
                        {
                            double cos = Math.Cos(ang), sin = Math.Sin(ang);
                            var matched = new List<ObjectId>();
                            Point3d Map(Point3d p)
                            {
                                double dx = (p.X - anchor.P.X) * scale, dy = (p.Y - anchor.P.Y) * scale;
                                return new Point3d(cand.P.X + dx * cos - dy * sin, cand.P.Y + dx * sin + dy * cos, cand.P.Z);
                            }
                            foreach (var s in sigs)
                            {
                                var w = Map(s.P);
                                var k = ((long)Math.Floor(w.X / cell), (long)Math.Floor(w.Y / cell), s.Type);
                                Sig hit = null;
                                for (long dx = -1; dx <= 1 && hit == null; dx++)
                                    for (long dy = -1; dy <= 1 && hit == null; dy++)
                                        if (grid.TryGetValue((k.Item1 + dx, k.Item2 + dy, s.Type), out var list))
                                            hit = list.FirstOrDefault(l => !used.Contains(l.Id) && !matched.Contains(l.Id) &&
                                                                           l.P.DistanceTo(w) <= tol &&
                                                                           Math.Abs(l.Size - s.Size * scale) <= Math.Max(s.Size * scale * 0.02, tol));
                                if (hit != null) matched.Add(hit.Id);
                            }
                            if (matched.Count >= 3 && matched.Count >= sigs.Count * 0.7)
                            {
                                foreach (var m in matched) used.Add(m);
                                var center = Map(new Point3d(sigs.Average(s => s.P.X), sigs.Average(s => s.P.Y), 0));
                                issues.Add(new CheckIssue
                                {
                                    Kind = "Block bị explode",
                                    BlockName = it.DisplayName ?? it.BlockName,
                                    Detail = $"{matched.Count}/{sigs.Count} nét rời khớp hình block '{it.BlockName}': có thể block bị explode, KHÔNG được đếm",
                                    Position = center,
                                    Size = typical * 6,
                                    LooseIds = matched
                                });
                                found++;
                                break;
                            }
                        }
                        if (found >= 500) break;
                    }
                    if (found > 0) Logger.Log($"[CountChecker] '{it.BlockName}': {found} cụm nét rời khớp hình (block bị explode?)");
                }
                tr.Commit();
            }
            return issues;
        }

        /// <summary>Khoanh vị trí các lỗi trên layer LHB_SOATLOI (không in).</summary>
        public static int DrawMarkers(Document doc, List<CheckIssue> issues)
        {
            var db = doc.Database;
            DrawingHelper.EraseOnLayer(doc, MarkerLayer);
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var layerId = DrawingHelper.GetOrCreateLayer(db, tr, MarkerLayer, 6, false);
                foreach (var i in issues)
                {
                    var c = new Circle(i.Position, Vector3d.ZAxis, Math.Max(i.Size / 8, 1e-3));
                    c.SetDatabaseDefaults(db);
                    c.LayerId = layerId;
                    ms.AppendEntity(c);
                    tr.AddNewlyCreatedDBObject(c, true);
                }
                tr.Commit();
            }
            Logger.Log($"[CountChecker] Khoanh {issues.Count} vị trí lỗi trên layer {MarkerLayer}");
            return issues.Count;
        }
    }
}

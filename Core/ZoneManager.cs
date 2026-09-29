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
    /// <summary>Khu vực (tầng / phòng / zone) = 1 đường bao kín trên bản vẽ + tên. Lưu trong bản vẽ bằng handle.</summary>
    public class ZoneDef
    {
        public string Name { get; set; }
        public string Handle { get; set; }
    }

    /// <summary>Khu vực đã nạp đường bao (toạ độ WCS) để tính điểm nằm trong.</summary>
    public class LoadedZone
    {
        public string Name;
        public string Handle;
        public ObjectId Id;
        public List<Point2d> Polygon;
        public double Area;
        public Extents2d Box;

        public bool Contains(Point3d p)
        {
            if (p.X < Box.MinPoint.X || p.X > Box.MaxPoint.X || p.Y < Box.MinPoint.Y || p.Y > Box.MaxPoint.Y) return false;
            return DrawingHelper.PointInPolygon(Polygon, new Point2d(p.X, p.Y));
        }
    }

    /// <summary>
    /// Thống kê theo tầng / khu vực (Premium P1). Danh sách khu vực lưu trong bản vẽ (Named Objects Dictionary
    /// LHB_PREMIUM / ZONES, JSON tên + handle đường bao) -> mở lại bản vẽ vẫn còn, gửi bản vẽ cho người khác cũng còn.
    /// SL theo khu vực: mỗi block tính vào khu vực chứa điểm chèn (khu vực lồng nhau: khu nhỏ nhất). Block thừa do
    /// trùng không tính (giống cột SL). Không nằm trong khu nào -> cột "Ngoài khu vực".
    /// </summary>
    public static class ZoneManager
    {
        public const string ColumnPrefix = "zone:";
        public const string OutsideName = "Ngoài khu vực";
        private const string NodKey = "ZONES";

        public class ZoneList
        {
            public List<ZoneDef> Zones { get; set; } = new List<ZoneDef>();
        }

        public static List<ZoneDef> LoadDefs(Database db)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    string json = DrawingHelper.ReadNodString(db, tr, NodKey);
                    tr.Commit();
                    if (string.IsNullOrEmpty(json)) return new List<ZoneDef>();
                    var list = JsonHelper.Deserialize<ZoneList>(json);
                    return (list?.Zones ?? new List<ZoneDef>()).Where(z => z != null && !string.IsNullOrWhiteSpace(z.Name)).ToList();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ZoneManager.LoadDefs]");
                return new List<ZoneDef>();
            }
        }

        public static void SaveDefs(Document doc, List<ZoneDef> zones)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                DrawingHelper.WriteNodString(doc.Database, tr, NodKey, JsonHelper.Serialize(new ZoneList { Zones = zones }));
                tr.Commit();
            }
            Logger.Log($"[ZoneManager] Lưu {zones.Count} khu vực vào bản vẽ: [{string.Join(", ", zones.Select(z => z.Name + "@" + z.Handle))}]");
        }

        /// <summary>Nạp đường bao các khu vực. Đường bao đã bị xoá thì bỏ qua (có log).</summary>
        public static List<LoadedZone> Load(Database db, List<ZoneDef> defs = null)
        {
            defs ??= LoadDefs(db);
            var result = new List<LoadedZone>();
            if (defs.Count == 0) return result;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var d in defs)
                {
                    var id = DrawingHelper.FromHandle(db, d.Handle);
                    if (id.IsNull || !(tr.GetObject(id, OpenMode.ForRead) is Curve c))
                    {
                        Logger.Warn($"[ZoneManager] Khu vực '{d.Name}': đường bao handle {d.Handle} không còn trong bản vẽ -> bỏ qua");
                        continue;
                    }
                    var poly = DrawingHelper.CurveToPolygon(c);
                    if (poly == null || poly.Count < 3) continue;
                    result.Add(new LoadedZone
                    {
                        Name = d.Name,
                        Handle = d.Handle,
                        Id = id,
                        Polygon = poly,
                        Area = DrawingHelper.PolygonArea(poly),
                        Box = new Extents2d(poly.Min(p => p.X), poly.Min(p => p.Y), poly.Max(p => p.X), poly.Max(p => p.Y))
                    });
                }
                tr.Commit();
            }
            return result;
        }

        /// <summary>Khu vực chứa điểm (khu nhỏ nhất nếu lồng nhau), null = ngoài mọi khu.</summary>
        public static LoadedZone Find(List<LoadedZone> zones, Point3d p)
        {
            LoadedZone best = null;
            foreach (var z in zones)
                if (z.Contains(p) && (best == null || z.Area < best.Area)) best = z;
            return best;
        }

        /// <summary>
        /// Ghi SL theo khu vực vào item.ExtraValues["zone:&lt;tên&gt;"] (bỏ block thừa do trùng). Trả danh sách khoá cột
        /// theo thứ tự khu vực + "zone:Ngoài khu vực" nếu có block ngoài.
        /// </summary>
        public static List<string> ComputeCounts(IEnumerable<BlockItem> items, List<LoadedZone> zones)
        {
            var keys = zones.Select(z => ColumnPrefix + z.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool anyOutside = false;
            foreach (var item in items)
            {
                if (item.ExtraValues == null) item.ExtraValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in item.ExtraValues.Keys.Where(k => k.StartsWith(ColumnPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
                    item.ExtraValues.Remove(k);
                if (zones.Count == 0 || item.Instances == null || item.Instances.Count == 0) continue;

                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var inst in item.Instances)
                {
                    if (inst.IsExcludedDuplicate) continue;
                    var z = Find(zones, inst.Position);
                    string key = ColumnPrefix + (z?.Name ?? OutsideName);
                    if (z == null) anyOutside = true;
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                foreach (var kv in counts) item.ExtraValues[kv.Key] = kv.Value.ToString();
            }
            if (anyOutside) keys.Add(ColumnPrefix + OutsideName);
            return keys;
        }

        /// <summary>Tên gợi ý cho đường bao: chữ (Text / MText) cao nhất nằm trong đường bao, không có thì null.</summary>
        public static string SuggestName(Database db, List<Point2d> polygon)
        {
            try
            {
                string best = null;
                double bestH = 0;
                var box = new Extents2d(polygon.Min(p => p.X), polygon.Min(p => p.Y), polygon.Max(p => p.X), polygon.Max(p => p.Y));
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    var rxText = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(DBText));
                    var rxMText = Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MText));
                    foreach (ObjectId id in ms)
                    {
                        if (!id.ObjectClass.IsDerivedFrom(rxText) && !id.ObjectClass.IsDerivedFrom(rxMText)) continue;
                        Point3d p;
                        double h;
                        string s;
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        if (ent is DBText t) { p = t.Position; h = t.Height; s = t.TextString; }
                        else if (ent is MText m) { p = m.Location; h = m.TextHeight; s = m.Text; }
                        else continue;
                        if (p.X < box.MinPoint.X || p.X > box.MaxPoint.X || p.Y < box.MinPoint.Y || p.Y > box.MaxPoint.Y) continue;
                        if (string.IsNullOrWhiteSpace(s) || s.Length > 60 || h <= bestH) continue;
                        if (!DrawingHelper.PointInPolygon(polygon, new Point2d(p.X, p.Y))) continue;
                        best = s.Trim();
                        bestH = h;
                    }
                    tr.Commit();
                }
                return best;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ZoneManager.SuggestName] {ex.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>1 dòng thống kê chiều dài: 1 layer (và 1 khu vực nếu tách theo khu vực).</summary>
    public class LengthRow
    {
        public string Layer { get; set; }
        public string DisplayName { get; set; }
        public int Segments { get; set; }
        /// <summary>Tổng chiều dài theo đơn vị bản vẽ.</summary>
        public double DrawingLength { get; set; }
        /// <summary>Chiều dài theo khu vực (đơn vị bản vẽ), key = tên khu vực.</summary>
        public Dictionary<string, double> ZoneLengths { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<ObjectId> Ids { get; } = new List<ObjectId>();
    }

    /// <summary>
    /// Thống kê chiều dài ống / dây theo layer (Premium P4): Line, Polyline, Arc, Spline, Ellipse, Polyline2d/3d, Mline.
    /// Đổi ra mét theo đơn vị bản vẽ (DrawingHelper.MmPerUnit). Tách theo khu vực: mỗi đoạn thẳng / cung tính vào khu
    /// vực chứa trung điểm của đoạn (polyline chia theo từng đoạn, đường cong chia 1 m một đoạn).
    /// </summary>
    public static class LengthCounter
    {
        /// <summary>Bộ lọc chọn đối tượng (DXF code 0).</summary>
        public const string DxfFilter = "LINE,LWPOLYLINE,POLYLINE,ARC,SPLINE,ELLIPSE,MLINE";

        /// <summary>Mọi đường trong Model Space (quét toàn bản vẽ).</summary>
        public static List<ObjectId> AllCurves(Database db)
        {
            var ids = new List<ObjectId>();
            var classes = new[] { typeof(Curve), typeof(Mline) }.Select(t => Autodesk.AutoCAD.Runtime.RXObject.GetClass(t)).ToList();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (classes.Any(c => id.ObjectClass.IsDerivedFrom(c))) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        public static List<LengthRow> Count(Database db, IEnumerable<ObjectId> ids, List<LoadedZone> zones)
        {
            var rows = new Dictionary<string, LengthRow>(StringComparer.OrdinalIgnoreCase);
            var names = SettingsManager.Current.LayerDisplayNames ?? new Dictionary<string, string>();
            int skipped = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    try
                    {
                        if (id.IsNull || id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent is Xline || ent is Ray) { skipped++; continue; }
                        var segs = Segments(ent);
                        if (segs == null) { skipped++; continue; }

                        if (!rows.TryGetValue(ent.Layer, out var row))
                        {
                            row = new LengthRow
                            {
                                Layer = ent.Layer,
                                DisplayName = names.TryGetValue(ent.Layer, out var dn) && !string.IsNullOrWhiteSpace(dn) ? dn : ent.Layer
                            };
                            rows[ent.Layer] = row;
                        }
                        row.Segments++;
                        row.Ids.Add(id);
                        foreach (var (len, mid) in segs)
                        {
                            row.DrawingLength += len;
                            if (zones != null && zones.Count > 0)
                            {
                                string z = ZoneManager.Find(zones, mid)?.Name ?? ZoneManager.OutsideName;
                                row.ZoneLengths[z] = (row.ZoneLengths.TryGetValue(z, out double v) ? v : 0) + len;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        Logger.Warn($"[LengthCounter] Bỏ đối tượng {id.Handle}: {ex.Message}");
                    }
                }
                tr.Commit();
            }
            var list = rows.Values.OrderBy(r => r.Layer, StringComparer.OrdinalIgnoreCase).ToList();
            Logger.Log($"[LengthCounter] {list.Count} layer, {list.Sum(r => r.Segments)} đối tượng, bỏ {skipped}; " +
                       string.Join(", ", list.Select(r => $"{r.Layer}={r.DrawingLength:0.#}")));
            return list;
        }

        /// <summary>Các đoạn (chiều dài, trung điểm) của 1 đường. Null nếu không phải đường đo được.</summary>
        private static List<(double Len, Point3d Mid)> Segments(Entity ent)
        {
            var list = new List<(double, Point3d)>();
            if (ent is Mline ml)
            {
                for (int i = 0; i < ml.NumberOfVertices - 1; i++)
                {
                    var a = ml.VertexAt(i);
                    var b = ml.VertexAt(i + 1);
                    list.Add((a.DistanceTo(b), a + (b - a) / 2));
                }
                if (ml.IsClosed && ml.NumberOfVertices > 2)
                {
                    var a = ml.VertexAt(ml.NumberOfVertices - 1);
                    var b = ml.VertexAt(0);
                    list.Add((a.DistanceTo(b), a + (b - a) / 2));
                }
                return list;
            }
            if (!(ent is Curve c)) return null;

            if (c is Line ln)
            {
                list.Add((ln.Length, ln.StartPoint + (ln.EndPoint - ln.StartPoint) / 2));
                return list;
            }
            if (c is Polyline pl)
            {
                int n = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
                for (int i = 0; i < n; i++)
                {
                    double d0 = pl.GetDistanceAtParameter(i);
                    double d1 = pl.GetDistanceAtParameter(i + 1);
                    list.Add((d1 - d0, pl.GetPointAtParameter(i + 0.5)));
                }
                return list;
            }

            // Đường cong khác: chia khúc ~ 1/20 chiều dài để gán khu vực
            double total = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
            if (total <= 0) return list;
            int k = 20;
            for (int i = 0; i < k; i++)
                list.Add((total / k, c.GetPointAtDist(total * (i + 0.5) / k)));
            return list;
        }
    }
}

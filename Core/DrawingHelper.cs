using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>Hàm dùng chung cho các tính năng Premium: layer, đơn vị, đa giác, handle, zoom.</summary>
    public static class DrawingHelper
    {
        /// <summary>Layer có sẵn thì trả về, chưa có thì tạo (màu ACI, in / không in).</summary>
        public static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex, bool plottable)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                IsPlottable = plottable
            };
            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            Logger.Log($"[DrawingHelper] Tạo layer '{name}' (màu {colorIndex}, {(plottable ? "in" : "không in")})");
            return id;
        }

        /// <summary>Xoá mọi entity trên 1 layer trong Model Space. Trả số entity đã xoá.</summary>
        public static int EraseOnLayer(Document doc, string layerName)
        {
            var db = doc.Database;
            int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName)) return 0;
                var layerId = lt[layerName];
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && e.LayerId == layerId) ids.Add(id);
                foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase();
                n = ids.Count;
                tr.Commit();
            }
            Logger.Log($"[DrawingHelper] Xoá {n} entity trên layer '{layerName}'");
            return n;
        }

        /// <summary>
        /// 1 đơn vị bản vẽ = ? mm. Settings MmPerDrawingUnit > 0 thì dùng, không thì theo INSUNITS
        /// (không đặt đơn vị -> coi là mm, thói quen vẽ mặt bằng PCCC ở VN).
        /// </summary>
        public static double MmPerUnit(Database db)
        {
            double s = SettingsManager.Current.MmPerDrawingUnit;
            if (s > 0) return s;
            switch (db.Insunits)
            {
                case UnitsValue.Meters: return 1000;
                case UnitsValue.Centimeters: return 10;
                case UnitsValue.Decimeters: return 100;
                case UnitsValue.Inches: return 25.4;
                case UnitsValue.Feet: return 304.8;
                default: return 1;
            }
        }

        public static string UnitLabel(Database db)
        {
            double mm = MmPerUnit(db);
            string src = SettingsManager.Current.MmPerDrawingUnit > 0 ? "cài đặt" : $"INSUNITS={db.Insunits}";
            return $"1 đơn vị bản vẽ = {mm.ToString("0.###", CultureInfo.InvariantCulture)} mm ({src})";
        }

        /// <summary>Điểm nằm trong đa giác (ray casting, XY).</summary>
        public static bool PointInPolygon(IList<Point2d> poly, Point2d p)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) &&
                    p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        public static double PolygonArea(IList<Point2d> poly)
        {
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += (poly[j].X + poly[i].X) * (poly[j].Y - poly[i].Y);
            return Math.Abs(a / 2.0);
        }

        /// <summary>
        /// Đổi đường cong kín (Polyline có cung, Circle, Ellipse, Spline, Polyline2d...) thành đa giác XY theo WCS.
        /// Cung chia nhỏ theo góc ~5°. Null nếu không phải đường cong.
        /// </summary>
        public static List<Point2d> CurveToPolygon(Curve c)
        {
            try
            {
                var pts = new List<Point2d>();
                if (c is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint3dAt(i);
                        pts.Add(new Point2d(p.X, p.Y));
                        double bulge = pl.GetBulgeAt(i);
                        if (Math.Abs(bulge) > 1e-9 && (i < pl.NumberOfVertices - 1 || pl.Closed))
                        {
                            double sp = pl.GetParameterAtPoint(p);
                            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(4 * Math.Atan(bulge)) / (Math.PI / 36)));
                            for (int k = 1; k < steps; k++)
                            {
                                var q = pl.GetPointAtParameter(sp + (double)k / steps);
                                pts.Add(new Point2d(q.X, q.Y));
                            }
                        }
                    }
                    return pts;
                }

                double start = c.StartParam, end = c.EndParam;
                double len = c.GetDistanceAtParameter(end) - c.GetDistanceAtParameter(start);
                int n = c is Line ? 1 : 144;
                for (int k = 0; k <= n; k++)
                {
                    var q = c.GetPointAtDist(len * k / n);
                    pts.Add(new Point2d(q.X, q.Y));
                }
                return pts;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DrawingHelper.CurveToPolygon] {c?.GetType().Name} Handle={c?.Handle}: {ex.Message}");
                return null;
            }
        }

        public static string HandleString(ObjectId id) => id.IsNull ? "" : id.Handle.Value.ToString("X");

        /// <summary>Handle dạng hex -> ObjectId (Null nếu không còn trong bản vẽ / đã xoá).</summary>
        public static ObjectId FromHandle(Database db, string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return ObjectId.Null;
            try
            {
                if (!long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)) return ObjectId.Null;
                if (!db.TryGetObjectId(new Handle(v), out var id) || id.IsErased) return ObjectId.Null;
                return id;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        /// <summary>Zoom tới 1 điểm với khung nhìn cạnh size (đơn vị bản vẽ).</summary>
        public static void ZoomToPoint(Document doc, Point3d p, double size)
        {
            double h = Math.Max(size, 1) / 2;
            ScheduleManager.ZoomToExtents(doc, new Extents3d(new Point3d(p.X - h, p.Y - h, 0), new Point3d(p.X + h, p.Y + h, 0)));
        }

        /// <summary>Khoá loại thiết bị dùng lưu cài đặt theo loại (bán kính bảo vệ, tiền tố đánh số).</summary>
        public static string ItemKey(Models.BlockItem item) => (item.BlockName ?? "") + "||" + (item.VisibilityState ?? "");

        /// <summary>Ghi chuỗi dài vào Xrecord (chia khúc 250 ký tự) / đọc lại.</summary>
        public static ResultBuffer ToChunks(string s)
        {
            var rb = new ResultBuffer();
            s ??= "";
            for (int i = 0; i < s.Length; i += 250)
                rb.Add(new TypedValue((int)DxfCode.Text, s.Substring(i, Math.Min(250, s.Length - i))));
            if (s.Length == 0) rb.Add(new TypedValue((int)DxfCode.Text, ""));
            return rb;
        }

        public static string FromChunks(ResultBuffer rb)
        {
            if (rb == null) return null;
            return string.Concat(rb.AsArray().Where(tv => tv.TypeCode == (int)DxfCode.Text).Select(tv => tv.Value as string));
        }

        /// <summary>Đọc / ghi Xrecord chuỗi trong Named Objects Dictionary (dữ liệu Premium lưu trong bản vẽ).</summary>
        public const string NodDictName = "LHB_PREMIUM";

        public static string ReadNodString(Database db, Transaction tr, string key)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NodDictName)) return null;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(NodDictName), OpenMode.ForRead);
            if (!dict.Contains(key)) return null;
            return tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is Xrecord xr ? FromChunks(xr.Data) : null;
        }

        public static void WriteNodString(Database db, Transaction tr, string key, string value)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary dict;
            if (nod.Contains(NodDictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(NodDictName), OpenMode.ForWrite);
            else
            {
                nod.UpgradeOpen();
                dict = new DBDictionary();
                nod.SetAt(NodDictName, dict);
                tr.AddNewlyCreatedDBObject(dict, true);
            }
            if (dict.Contains(key))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                xr.Data = ToChunks(value);
            }
            else
            {
                var xr = new Xrecord { Data = ToChunks(value) };
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        /// <summary>Đọc / ghi chuỗi trong Extension Dictionary của 1 đối tượng (bảng xuất nhớ vùng quét).</summary>
        public static string ReadExtString(Transaction tr, DBObject obj, string key)
        {
            if (obj.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(key)) return null;
            return tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is Xrecord xr ? FromChunks(xr.Data) : null;
        }

        public static void WriteExtString(Transaction tr, DBObject obj, string key, string value)
        {
            if (obj.ExtensionDictionary.IsNull)
            {
                if (!obj.IsWriteEnabled) obj.UpgradeOpen();
                obj.CreateExtensionDictionary();
            }
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            if (dict.Contains(key))
            {
                ((Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite)).Data = ToChunks(value);
            }
            else
            {
                var xr = new Xrecord { Data = ToChunks(value) };
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Tính extents "bền": nếu entity.GeometricExtents ném eInvalidExtents (đã gặp với block
    /// ĐÈN EXIT THOÁT NẠN, LHBDIAG 28/09/2026) thì explode BlockReference ra từng entity con
    /// và cộng dồn extents của các entity con hợp lệ, bỏ qua entity lỗi.
    /// </summary>
    public static class ExtentsHelper
    {
        private const int MaxDepth = 6;
        // Chỉ log mỗi loại entity lỗi 1 lần / phiên để không làm phình log
        private static readonly HashSet<string> _loggedFailTypes = new HashSet<string>();

        public static bool TryGetExtents(Entity ent, out Extents3d ext)
        {
            ext = new Extents3d();
            bool has = false;
            Accumulate(ent, ref ext, ref has, 0);
            return has;
        }

        /// <summary>
        /// Chỉ dùng GeometricExtents của chính entity, KHÔNG explode. Đây cũng là extents AutoCAD dùng khi
        /// AutoFit block trong ô Table -> entity trả false ở đây sẽ làm AutoFit to nhỏ sai.
        /// </summary>
        public static bool TryGetOwnExtents(Entity ent, out Extents3d ext)
        {
            ext = new Extents3d();
            try
            {
                ext = ent.GeometricExtents;
                return IsValid(ext);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Extents của toàn bộ entity trong 1 BlockTableRecord (toạ độ nội bộ block).</summary>
        public static bool TryGetBtrExtents(Transaction tr, BlockTableRecord btr, out Extents3d ext)
        {
            ext = new Extents3d();
            bool has = false;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && ent.Visible)
                    Accumulate(ent, ref ext, ref has, 0);
            }
            return has;
        }

        private static void Accumulate(Entity ent, ref Extents3d ext, ref bool has, int depth)
        {
            // AttributeDefinition trong block không vẽ ra ở instance (trừ Constant) -> bỏ qua
            if (ent is AttributeDefinition ad && !ad.Constant) return;

            try
            {
                var e = ent.GeometricExtents;
                if (IsValid(e))
                {
                    if (has) ext.AddExtents(e); else { ext = e; has = true; }
                    return;
                }
            }
            catch (Exception ex)
            {
                string type = ent.GetType().Name;
                lock (_loggedFailTypes)
                {
                    if (_loggedFailTypes.Add(type))
                        Logger.Log($"[ExtentsHelper] {type} (Handle={SafeHandle(ent)}) không có GeometricExtents ({ex.Message}) -> thử explode");
                }
            }

            if (!(ent is BlockReference br) || depth >= MaxDepth) return;

            var parts = new DBObjectCollection();
            try
            {
                br.Explode(parts);
                foreach (DBObject o in parts)
                {
                    if (o is Entity sub && sub.Visible) Accumulate(sub, ref ext, ref has, depth + 1);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ExtentsHelper] Explode block '{br.Name}' thất bại: {ex.Message}");
            }
            finally
            {
                foreach (DBObject o in parts) o.Dispose();
            }
        }

        private static bool IsValid(Extents3d e)
        {
            double w = e.MaxPoint.X - e.MinPoint.X, h = e.MaxPoint.Y - e.MinPoint.Y;
            return !double.IsNaN(w) && !double.IsNaN(h) && !double.IsInfinity(w) && !double.IsInfinity(h)
                   && w >= 0 && h >= 0 && w < 1e12 && h < 1e12;
        }

        private static string SafeHandle(Entity ent)
        {
            try { return ent.IsNewObject ? "(tạm)" : ent.Handle.ToString(); } catch { return "?"; }
        }
    }
}

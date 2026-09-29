using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Zoom / highlight block trên bản vẽ theo dòng thống kê (gộp / sắp xếp dòng nằm ở form, chỉ thao tác bộ nhớ).
    /// </summary>
    public static class ScheduleManager
    {
        // Lưu lại các ObjectId đang được highlight để Unhighlight trước khi chọn dòng mới
        private static List<ObjectId> _currentlyHighlighted = new List<ObjectId>();

        /// <summary>
        /// Zoom AutoCAD tới vùng bao (extents) của các Block thuộc dòng được chọn, và highlight chúng.
        /// Tự động unhighlight dòng được chọn trước đó.
        /// </summary>
        public static void ZoomAndHighlight(Document doc, List<ObjectId> objectIds)
        {
            if (objectIds == null || objectIds.Count == 0) return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                UnhighlightPrevious(db);

                Extents3d? ext = null;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in objectIds)
                    {
                        if (id.IsErased || !id.IsValid) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        // Extents bền: ĐÈN EXIT ném eInvalidExtents khi gọi GeometricExtents trực tiếp
                        if (ExtentsHelper.TryGetExtents(ent, out var e))
                            ext = ext == null ? e : CombineExtents(ext.Value, e);
                        else
                            Logger.Warn($"ZoomAndHighlight: object Handle={ent.Handle} ({ent.GetType().Name}) không tính được extents kể cả sau khi explode");
                    }
                    tr.Commit();
                }

                if (ext == null)
                {
                    Logger.Warn("ZoomAndHighlight: không tính được extents (có thể toàn bộ object đã bị xoá)");
                    return;
                }

                ZoomToExtents(doc, ext.Value);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in objectIds)
                    {
                        if (id.IsErased || !id.IsValid) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        ent?.Highlight();
                    }
                    tr.Commit();
                }
                _currentlyHighlighted = objectIds.Where(id => id.IsValid && !id.IsErased).ToList();

                Logger.Log($"ZoomAndHighlight: đã zoom + highlight {objectIds.Count} object");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ZoomAndHighlight");
            }
        }

        /// <summary>
        /// Highlight các BlockItem được chọn (KHÔNG zoom). Hỗ trợ cả block lồng bằng FullSubentityPath.
        /// </summary>
        public static void HighlightItems(Document doc, List<BlockItem> items)
        {
            if (items == null || items.Count == 0) return;
            var db = doc.Database;

            try
            {
                UnhighlightPrevious(db);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var item in items)
                    {
                        if (item.ObjectIds == null) continue;
                        foreach (var id in item.ObjectIds)
                        {
                            if (id.IsNull || !id.IsValid || id.IsErased) continue;
                            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            if (item.ContainerPath != null && item.ContainerPath.Length > 1)
                            {
                                try
                                {
                                    var subId = new SubentityId(SubentityType.Null, IntPtr.Zero);
                                    var fullPath = new FullSubentityPath(item.ContainerPath, subId);
                                    ent.Highlight(fullPath, true);
                                }
                                catch
                                {
                                    ent.Highlight();
                                }
                            }
                            else
                            {
                                ent.Highlight();
                            }
                            _currentlyHighlighted.Add(id);
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ScheduleManager.HighlightItems");
            }
        }

        // Block lồng được highlight theo đường dẫn (FullSubentityPath) -> phải unhighlight đúng đường dẫn đó
        private static readonly List<ObjectId[]> _highlightedPaths = new List<ObjectId[]>();

        /// <summary>
        /// Highlight từng block theo đường dẫn [block ngoài cùng, ..., block cần highlight].
        /// Block lồng phải highlight qua block ngoài cùng, gọi Highlight() trên block con không hiện gì.
        /// </summary>
        public static void HighlightPaths(Document doc, List<ObjectId[]> paths)
        {
            if (paths == null || paths.Count == 0) return;
            var db = doc.Database;
            try
            {
                UnhighlightPrevious(db);
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var path in paths)
                    {
                        if (path == null || path.Length == 0 || path.Any(id => id.IsNull || !id.IsValid || id.IsErased)) continue;
                        if (!(tr.GetObject(path[0], OpenMode.ForRead) is Entity top)) continue;
                        if (path.Length == 1)
                        {
                            top.Highlight();
                            _currentlyHighlighted.Add(path[0]);
                        }
                        else
                        {
                            top.Highlight(new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero)), true);
                            _highlightedPaths.Add(path);
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.UpdateScreen();
                Logger.Log($"HighlightPaths: highlight {paths.Count} block ({_highlightedPaths.Count} block lồng)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ScheduleManager.HighlightPaths");
            }
        }

        public static void UnhighlightPrevious(Database db)
        {
            if (_currentlyHighlighted.Count == 0 && _highlightedPaths.Count == 0) return;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in _currentlyHighlighted)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        ent?.Unhighlight();
                    }
                    foreach (var path in _highlightedPaths)
                    {
                        if (path.Any(id => !id.IsValid || id.IsErased)) continue;
                        var top = tr.GetObject(path[0], OpenMode.ForRead) as Entity;
                        top?.Unhighlight(new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero)), true);
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "UnhighlightPrevious");
            }
            finally
            {
                _currentlyHighlighted.Clear();
                _highlightedPaths.Clear();
            }
        }

        private static Extents3d CombineExtents(Extents3d a, Extents3d b)
        {
            a.AddExtents(b);
            return a;
        }

        /// <summary>
        /// Zoom to extents đơn giản, giả định view đang ở góc nhìn Top (trường hợp
        /// gần như 100% với bản vẽ mặt bằng 2D chứa block thống kê). Nếu sau này cần
        /// hỗ trợ isometric/3D view, đây là chỗ cần nâng cấp thêm phép biến đổi WCS->DCS.
        /// </summary>
        public static void ZoomToExtents(Document doc, Extents3d ext)
        {
            var ed = doc.Editor;
            using (doc.LockDocument())
            {
                var view = ed.GetCurrentView();

                double width = ext.MaxPoint.X - ext.MinPoint.X;
                double height = ext.MaxPoint.Y - ext.MinPoint.Y;
                if (width <= 0) width = 1;
                if (height <= 0) height = 1;

                Point2d center = new Point2d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0);

                view.Width = width * 1.3;   // chừa lề 30% quanh block để dễ nhìn
                view.Height = height * 1.3;
                view.CenterPoint = center;

                ed.SetCurrentView(view);
            }
        }
    }
}

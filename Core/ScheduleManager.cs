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
        /// v9.4: zoom tới vùng bao các block của 1 dòng theo toạ độ WCS đã tính lúc quét (khung bao xoay / điểm chèn) rồi
        /// highlight từng block theo đường dẫn. Trước đây zoom theo extents của ObjectId: block lồng / trong ARRAY có extents
        /// theo toạ độ block cha -> zoom lệch chỗ.
        /// </summary>
        public static void ZoomAndHighlightItem(Document doc, BlockItem item)
        {
            if (item?.Instances == null || item.Instances.Count == 0) return;
            try
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var inst in item.Instances)
                {
                    if (inst.Corners != null)
                    {
                        foreach (var c in inst.Corners)
                        {
                            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
                            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
                        }
                    }
                    else
                    {
                        minX = Math.Min(minX, inst.Position.X); minY = Math.Min(minY, inst.Position.Y);
                        maxX = Math.Max(maxX, inst.Position.X); maxY = Math.Max(maxY, inst.Position.Y);
                    }
                }
                if (minX > maxX) return;
                ZoomToExtents(doc, new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0)));
                HighlightPaths(doc, item.Instances.Select(i => i.Path).ToList());
                Logger.Log($"ZoomAndHighlightItem: '{item.DisplayName ?? item.BlockName}' {item.Instances.Count} block, " +
                           $"vùng ({minX:0.##}, {minY:0.##}) - ({maxX:0.##}, {maxY:0.##})");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ScheduleManager.ZoomAndHighlightItem");
            }
        }

        /// <summary>
        /// Highlight các BlockItem được chọn (KHÔNG zoom). Hỗ trợ cả block lồng / trong ARRAY bằng FullSubentityPath.
        /// v9.4: highlight theo đường dẫn của TỪNG block (trước đây mọi block của dòng dùng đường dẫn của block đầu tiên
        /// -> block lồng trong block cha khác không sáng).
        /// </summary>
        public static void HighlightItems(Document doc, List<BlockItem> items)
        {
            if (items == null || items.Count == 0) return;
            var paths = items.Where(i => i.Instances != null).SelectMany(i => i.Instances.Select(x => x.Path)).Where(p => p != null).ToList();
            HighlightPaths(doc, paths);
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
                int failed = 0;
                var seenTop = new HashSet<ObjectId>();
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var path in paths)
                    {
                        if (path == null || path.Length == 0 || path.Any(id => id.IsNull || !id.IsValid || id.IsErased || id.Database != db)) continue;
                        if (!(tr.GetObject(path[0], OpenMode.ForRead) is Entity top)) continue;
                        try
                        {
                            if (path.Length == 1)
                            {
                                // Phần tử MINSERT dùng chung 1 đối tượng -> chỉ highlight 1 lần
                                if (!seenTop.Add(path[0])) continue;
                                top.Highlight();
                                _currentlyHighlighted.Add(path[0]);
                            }
                            else
                            {
                                top.Highlight(new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero)), true);
                                _highlightedPaths.Add(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 1 đường dẫn lỗi (MINSERT lồng...) không làm dừng cả lượt highlight
                            if (failed++ == 0) Logger.Warn($"HighlightPaths: highlight Handle={path.Last().Handle} lỗi: {ex.Message}");
                        }
                    }
                    tr.Commit();
                }
                if (failed > 1) Logger.Warn($"HighlightPaths: {failed} block không highlight được");
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
                        // v9.4: danh sách highlight dùng chung mọi bản vẽ -> bỏ ObjectId của bản vẽ khác
                        if (!id.IsValid || id.IsErased || id.Database != db) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        ent?.Unhighlight();
                    }
                    foreach (var path in _highlightedPaths)
                    {
                        if (path.Any(id => !id.IsValid || id.IsErased || id.Database != db)) continue;
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

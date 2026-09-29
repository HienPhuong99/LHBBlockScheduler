using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsSystem;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Module tạo ảnh thu nhỏ (Thumbnail) cho Block bằng GraphicsSystem off-screen device:
    /// Render trực tiếp trong RAM (không tạo file tạm DWG/WMF, không chuyển Document active).
    /// Chuẩn hoá ảnh: nền trắng, ZoomExtents(0.85), kích thước cố định 96x96 px.
    /// Tự động xử lý màu 7 (trắng trên nền trắng) bằng cách render nền đen rồi đảo RGB.
    /// </summary>
    public static class ThumbnailGenerator
    {
        public static string ThumbCacheFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler", "Thumbs");

        private static bool _folderReady;

        // ShapeHash theo file PNG: mỗi lần quét lại (đổi tuỳ chọn trên form) không phải đọc + giải mã lại PNG.
        // File PNG chỉ đổi khi GenerateThumbnail ghi lại / ClearCache xoá -> xoá mục tương ứng ở đó.
        private static readonly Dictionary<string, ulong> _hashCache = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Xoá toàn bộ file PNG trong bộ nhớ đệm cache thumbnail.
        /// </summary>
        public static void ClearCache()
        {
            _hashCache.Clear();
            try
            {
                if (Directory.Exists(ThumbCacheFolder))
                {
                    foreach (var file in Directory.GetFiles(ThumbCacheFolder, "*.png"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    Logger.Log("ThumbnailGenerator: đã xoá sạch cache thumbnail.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ThumbnailGenerator.ClearCache");
            }
        }

        /// <summary>
        /// Lấy hoặc tạo thumbnail theo BlockTableRecord ObjectId (hỗ trợ cả dynamic block anonymous record).
        /// </summary>
        public static string GetOrCreateThumbnail(Document sourceDoc, ObjectId btrId, string cacheKey)
        {
            if (btrId.IsNull || sourceDoc == null) return null;

            if (!_folderReady)
            {
                Directory.CreateDirectory(ThumbCacheFolder);
                _folderReady = true;
            }
            string safeFileName = MakeSafeFileName(cacheKey);
            string pngPath = Path.Combine(ThumbCacheFolder, safeFileName + ".png");

            if (File.Exists(pngPath))
            {
                return pngPath;
            }

            _hashCache.Remove(pngPath);
            return GenerateThumbnail(sourceDoc, btrId, cacheKey, pngPath);
        }

        private static string GenerateThumbnail(Document doc, ObjectId btrId, string blockName, string pngPath)
        {
            Bitmap bmp = null;
            int w = 96, h = 96;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null)
                {
                    Logger.Warn($"ThumbnailGenerator: không mở được BTR {btrId} của block '{blockName}'");
                    tr.Commit();
                    return null;
                }

                // Chuỗi fallback: 1. PreviewIcon nếu có
                if (btr.PreviewIcon != null)
                {
                    try
                    {
                        bmp = new Bitmap(btr.PreviewIcon, new Size(w, h));
                        Logger.Log($"ThumbnailGenerator: dùng PreviewIcon có sẵn của block '{blockName}'");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"ThumbnailGenerator: PreviewIcon lỗi ({ex.Message}), chuyển sang GS render");
                    }
                }
                tr.Commit();
            }

            // Chuỗi fallback: 2. Render bằng GraphicsSystem off-screen device
            if (bmp == null)
            {
                bmp = RenderBtrToBitmap(doc, btrId, w, h, Color.White);
            }

            if (bmp == null)
            {
                Logger.Warn($"ThumbnailGenerator: Không thể render thumbnail cho block '{blockName}' (không có hình học hoặc lỗi GS)");
                return null;
            }

            // Xử lý màu 7: nếu >98% gần-trắng thì render lại với Color.Black rồi đảo màu RGB toàn ảnh
            if (IsAlmostAllWhite(bmp, 0.98))
            {
                bmp.Dispose();
                var blackBmp = RenderBtrToBitmap(doc, btrId, w, h, Color.Black);
                if (blackBmp != null)
                {
                    bmp = InvertBitmapRgb(blackBmp);
                    blackBmp.Dispose();
                    Logger.Log($"ThumbnailGenerator: block '{blockName}' phát hiện màu 7 (>98% trắng), đã render lại nền đen và đảo RGB.");
                }
                else
                {
                    Logger.Warn($"ThumbnailGenerator: render nền đen thất bại cho block '{blockName}'.");
                }
            }
            else
            {
                Logger.Log($"ThumbnailGenerator: render chuẩn nền trắng cho '{blockName}'.");
            }

            if (bmp == null) return null;

            try
            {
                bmp.Save(pngPath, ImageFormat.Png);
                return pngPath;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ThumbnailGenerator: không lưu được PNG '{pngPath}'");
                return null;
            }
            finally
            {
                bmp.Dispose();
            }
        }

        /// <summary>Render BlockTableRecord ra Bitmap bằng GS off-screen device.
        /// BẮT BUỘC gọi trên main thread của AutoCAD.</summary>
        public static Bitmap RenderBtrToBitmap(Document doc, ObjectId btrId, int w, int h, Color bg)
        {
            Bitmap bmp = null;
            var gsm = doc.GraphicsManager;

            using (var kernelDesc = new KernelDescriptor())
            {
                kernelDesc.addRequirement(KernelDescriptor.Drawing3D);
                using (var kernel = Manager.AcquireGraphicsKernel(kernelDesc))
                {
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                        using (var view = new Autodesk.AutoCAD.GraphicsSystem.View())
                        using (var dev = gsm.CreateAutoCADOffScreenDevice(kernel))
                        {
                            dev.OnSize(new Size(w, h));
                            dev.BackgroundColor = bg;
                            dev.Add(view);

                            using (var model = gsm.CreateAutoCADModel(kernel))
                            {
                                foreach (ObjectId id in btr)
                                {
                                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                    if (ent == null) continue;
                                    view.Add(ent, model);
                                }

                                // Extents bền: block lồng (vd block nhanh A$C chứa ĐÈN EXIT) ném eInvalidExtents
                                // -> trước đây trả null, thư viện lưu ShapeHash=0 và không có ảnh
                                if (!ExtentsHelper.TryGetBtrExtents(tr, btr, out var ext))
                                {
                                    Logger.Warn($"ThumbnailGenerator.RenderBtrToBitmap: BTR '{btr.Name}' không tính được extents (kể cả sau khi explode) -> không render");
                                    view.EraseAll(); dev.Erase(view); tr.Commit(); return null;
                                }

                                view.ZoomExtents(ext.MinPoint, ext.MaxPoint);
                                view.Zoom(0.85);                    // chừa lề, tránh cắt sát nét
                                bmp = view.GetSnapshot(new Rectangle(0, 0, w, h));

                                view.EraseAll();
                                dev.Erase(view);
                            }
                        }
                        tr.Commit();
                    }
                }
            }
            return bmp;
        }

        /// <summary>Đọc toàn bộ điểm ảnh dạng BGRA (LockBits: nhanh hơn GetPixel từng điểm hàng chục lần).</summary>
        private static byte[] ReadPixels(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[bmp.Width * bmp.Height * 4];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, y * bmp.Width * 4, bmp.Width * 4);
                return buf;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static bool IsAlmostAllWhite(Bitmap bmp, double threshold)
        {
            var px = ReadPixels(bmp);
            int nearWhiteCount = 0;
            int total = bmp.Width * bmp.Height;
            for (int i = 0; i < px.Length; i += 4)
            {
                // BGRA
                if (px[i + 3] < 20 || (px[i + 2] >= 240 && px[i + 1] >= 240 && px[i] >= 240))
                    nearWhiteCount++;
            }
            return (double)nearWhiteCount / total >= threshold;
        }

        private static Bitmap InvertBitmapRgb(Bitmap src)
        {
            var px = ReadPixels(src);
            // Đảo ngược RGB: nền đen (0,0,0) -> trắng (255,255,255), nét trắng -> đen (0,0,0), giữ kênh A
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = (byte)(255 - px[i]);
                px[i + 1] = (byte)(255 - px[i + 1]);
                px[i + 2] = (byte)(255 - px[i + 2]);
            }
            var res = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            var data = res.LockBits(new Rectangle(0, 0, res.Width, res.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < res.Height; y++)
                    Marshal.Copy(px, y * res.Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), res.Width * 4);
            }
            finally
            {
                res.UnlockBits(data);
            }
            return res;
        }

        public static string CustomImagesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler", "CustomImages");

        /// <summary>
        /// Chụp một vùng cửa sổ trên bản vẽ ModelSpace và lưu ra Bitmap.
        /// </summary>
        public static Bitmap RenderWindowToBitmap(Document doc, Point3d p1, Point3d p2, int w, int h)
        {
            var gsm = doc.GraphicsManager;
            double minX = Math.Min(p1.X, p2.X);
            double maxX = Math.Max(p1.X, p2.X);
            double minY = Math.Min(p1.Y, p2.Y);
            double maxY = Math.Max(p1.Y, p2.Y);

            using (var kernelDesc = new KernelDescriptor())
            {
                kernelDesc.addRequirement(KernelDescriptor.Drawing3D);
                using (var kernel = Manager.AcquireGraphicsKernel(kernelDesc))
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    using (var view = new Autodesk.AutoCAD.GraphicsSystem.View())
                    using (var dev = gsm.CreateAutoCADOffScreenDevice(kernel))
                    {
                        dev.OnSize(new Size(w, h));
                        dev.BackgroundColor = Color.White;
                        dev.Add(view);

                        using (var model = gsm.CreateAutoCADModel(kernel))
                        {
                            foreach (ObjectId id in ms)
                            {
                                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                if (ent == null) continue;
                                try
                                {
                                    var ext = ent.GeometricExtents;
                                    if (ext.MaxPoint.X >= minX && ext.MinPoint.X <= maxX &&
                                        ext.MaxPoint.Y >= minY && ext.MinPoint.Y <= maxY)
                                    {
                                        view.Add(ent, model);
                                    }
                                }
                                catch { }
                            }

                            view.ZoomWindow(new Point2d(minX, minY), new Point2d(maxX, maxY));
                            var bmp = view.GetSnapshot(new Rectangle(0, 0, w, h));

                            view.EraseAll();
                            dev.Erase(view);
                            tr.Commit();
                            return bmp;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tính dHash 64-bit từ Bitmap chuẩn hoá (9x8 grayscale, so sánh pixel liền kề).
        /// </summary>
        public static ulong ComputeDHash64(Bitmap bmp)
        {
            if (bmp == null) return 0;

            using (var resized = new Bitmap(9, 8, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(bmp, 0, 0, 9, 8);
                }

                int[,] gray = new int[9, 8];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 9; x++)
                    {
                        Color c = resized.GetPixel(x, y);
                        gray[x, y] = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
                    }
                }

                ulong hash = 0;
                int bit = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (gray[x, y] < gray[x + 1, y])
                        {
                            hash |= (1UL << bit);
                        }
                        bit++;
                    }
                }
                return hash;
            }
        }

        /// <summary>
        /// Tính khoảng cách Hamming (số lượng bit khác nhau) giữa 2 hash 64-bit.
        /// </summary>
        public static int HammingDistance(ulong h1, ulong h2)
        {
            ulong diff = h1 ^ h2;
            int dist = 0;
            while (diff != 0)
            {
                diff &= diff - 1; // xoá bit 1 thấp nhất
                dist++;
            }
            return dist;
        }

        /// <summary>
        /// Lấy hash từ file ảnh PNG đã cache hoặc tính mới.
        /// </summary>
        public static ulong GetHashFromPngFile(string pngPath)
        {
            if (string.IsNullOrEmpty(pngPath)) return 0;
            if (_hashCache.TryGetValue(pngPath, out ulong cached)) return cached;
            if (!File.Exists(pngPath)) return 0;
            try
            {
                using (var fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read))
                using (var img = System.Drawing.Image.FromStream(fs))
                using (var bmp = new Bitmap(img))
                {
                    ulong hash = ComputeDHash64(bmp);
                    _hashCache[pngPath] = hash;
                    return hash;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetHashFromPngFile: {pngPath}");
                return 0;
            }
        }

        /// <summary>Bỏ hash đã nhớ của 1 file PNG (file bị xoá để render lại).</summary>
        public static void ForgetHash(string pngPath)
        {
            if (!string.IsNullOrEmpty(pngPath)) _hashCache.Remove(pngPath);
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}

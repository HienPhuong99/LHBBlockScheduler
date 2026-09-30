using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using LHBBlockScheduler.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>1 nút lệnh trên Ribbon / palette.</summary>
    internal class LhbButton
    {
        public string Text, Command, Glyph, Tip;
        public Color Color;
        public LhbButton(string text, string command, string glyph, Color color, string tip)
        {
            Text = text; Command = command; Glyph = glyph; Color = color; Tip = tip;
        }
    }

    /// <summary>
    /// Tab Ribbon "LHB Premium" (P11). AdWindows.dll (Ribbon của AutoCAD) không có trong libs\ nên gọi qua reflection:
    /// lúc chạy lấy assembly AdWindows đã nạp trong acad.exe. Ribbon chưa sẵn sàng lúc NETLOAD -> chờ Application.Idle.
    /// Đổi workspace (WSCURRENT) làm mất tab -> tạo lại. Tắt tab: settings DisableRibbon = true (lệnh LHBRIBBON).
    /// </summary>
    internal static class RibbonBuilder
    {
        private const string TabId = "LHB_PREMIUM_TAB";
        private static int _tries;
        private static bool _waiting;

        public static readonly (string Panel, LhbButton[] Buttons)[] Layout =
        {
            ("Thống kê", new[]
            {
                new LhbButton("Thống kê\nblock", "LHBSCAN", "TK", Color.FromArgb(41, 128, 185), "Quét chọn block, mở bảng thống kê"),
                new LhbButton("Block\nmẫu", "LHBMAU", "BM", Color.FromArgb(39, 174, 96), "Thư viện block mẫu"),
                new LhbButton("Cập nhật\nbảng", "LHBCAPNHAT", "CN", Color.FromArgb(142, 68, 173), "Đếm lại bảng đã xuất sau khi sửa bản vẽ"),
                new LhbButton("Khớp\ncột bảng", "LHBKHOPCOT", "KC", Color.FromArgb(41, 128, 185), "Khớp độ rộng cột bảng đã xuất theo chữ (như Excel)"),
                new LhbButton("Nhiều\nbản vẽ", "LHBNHIEUBV", "NB", Color.FromArgb(52, 73, 94), "Thống kê nhiều file DWG cùng lúc"),
            }),
            ("Khối lượng", new[]
            {
                new LhbButton("Khu vực\n/ tầng", "LHBKHUVUC", "KV", Color.FromArgb(22, 160, 133), "Khai báo tầng / khu vực để thống kê theo khu"),
                new LhbButton("Chiều dài\nống / dây", "LHBCHIEUDAI", "CD", Color.FromArgb(211, 84, 0), "Thống kê chiều dài ống, dây theo layer"),
                new LhbButton("Mẫu\nbảng", "LHBMAUBANG", "MB", Color.FromArgb(127, 140, 141), "Tiêu đề, dòng tổng, font, màu bảng xuất"),
            }),
            ("Kiểm tra PCCC", new[]
            {
                new LhbButton("Soát\nlỗi đếm", "LHBSOATLOI", "SL", Color.FromArgb(192, 57, 43), "Block explode, đổi tên, layer tắt, trùng..."),
                new LhbButton("Đánh số\nthiết bị", "LHBDANHSO", "ĐS", Color.FromArgb(243, 156, 18), "Đánh số SP-01, SP-02... tự động"),
                new LhbButton("Vùng\nbảo vệ", "LHBVUNGBV", "VB", Color.FromArgb(26, 188, 156), "Vẽ bán kính bảo vệ đầu báo, đầu phun"),
                new LhbButton("Thay\nblock", "LHBTHAYBLOCK", "TB", Color.FromArgb(155, 89, 182), "Thay hàng loạt block sang block khác"),
            }),
            ("Tiện ích", new[]
            {
                new LhbButton("Danh sách\nlệnh", "LHBLENH", "L", Color.FromArgb(44, 62, 80), "Danh sách lệnh, đổi phím tắt"),
                new LhbButton("Palette", "LHBPALETTE", "P", Color.FromArgb(52, 152, 219), "Bảng công cụ LHB dock cạnh màn hình"),
                new LhbButton("Bản\nquyền", "LHBBANQUYEN", "BQ", Color.FromArgb(230, 126, 34), "Mã máy, kích hoạt Premium"),
            }),
        };

        public static void Init()
        {
            if (SettingsManager.Current.DisableRibbon) return;
            WaitForRibbon();
            AcApp.SystemVariableChanged += (s, e) =>
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase) && !SettingsManager.Current.DisableRibbon)
                    WaitForRibbon();
            };
        }

        public static void WaitForRibbon()
        {
            if (_waiting) return;
            _waiting = true;
            _tries = 0;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            try
            {
                var ribbon = GetRibbon();
                if (ribbon == null)
                {
                    if (++_tries > 300) { AcApp.Idle -= OnIdle; _waiting = false; Logger.Warn("[Ribbon] Không thấy Ribbon (AutoCAD chạy giao diện cổ điển?)"); }
                    return;
                }
                AcApp.Idle -= OnIdle;
                _waiting = false;
                Create(ribbon);
            }
            catch (Exception ex)
            {
                AcApp.Idle -= OnIdle;
                _waiting = false;
                Logger.Error(ex, "[Ribbon] Tạo tab LHB Premium");
            }
        }

        private static Assembly AdWindows =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "AdWindows", StringComparison.OrdinalIgnoreCase));

        private static object GetRibbon() =>
            AdWindows?.GetType("Autodesk.Windows.ComponentManager")?.GetProperty("Ribbon")?.GetValue(null);

        public static void Remove()
        {
            var ribbon = GetRibbon();
            if (ribbon == null) return;
            var tabs = (IList)Get(ribbon, "Tabs");
            foreach (var t in tabs.Cast<object>().ToList())
                if ((Get(t, "Id") as string) == TabId) tabs.Remove(t);
        }

        private static void Create(object ribbon)
        {
            var asm = AdWindows;
            var tabs = (IList)Get(ribbon, "Tabs");
            foreach (var t in tabs)
                if ((Get(t, "Id") as string) == TabId) return;

            var tab = Activator.CreateInstance(asm.GetType("Autodesk.Windows.RibbonTab"));
            Set(tab, "Title", "LHB Premium");
            Set(tab, "Id", TabId);
            tabs.Add(tab);

            var handler = new RibbonCommand();
            int buttons = 0;
            foreach (var (panelTitle, items) in Layout)
            {
                var src = Activator.CreateInstance(asm.GetType("Autodesk.Windows.RibbonPanelSource"));
                Set(src, "Title", panelTitle);
                var panel = Activator.CreateInstance(asm.GetType("Autodesk.Windows.RibbonPanel"));
                Set(panel, "Source", src);
                ((IList)Get(tab, "Panels")).Add(panel);

                foreach (var b in items)
                {
                    var btn = Activator.CreateInstance(asm.GetType("Autodesk.Windows.RibbonButton"));
                    Set(btn, "Text", b.Text);
                    Set(btn, "Id", "LHB_" + b.Command);
                    Set(btn, "ShowText", true);
                    Set(btn, "ShowImage", true);
                    Set(btn, "LargeImage", ToImageSource(Icon(b, 32)));
                    Set(btn, "Image", ToImageSource(Icon(b, 16)));
                    SetEnum(btn, "Size", "Large");
                    SetEnum(btn, "Orientation", "Vertical");
                    Set(btn, "CommandParameter", b.Command);
                    Set(btn, "CommandHandler", handler);
                    Set(btn, "ToolTip", $"{b.Tip}\nLệnh: {b.Command}");
                    ((IList)Get(src, "Items")).Add(btn);
                    buttons++;
                }
            }
            Logger.Log($"[Ribbon] Đã tạo tab 'LHB Premium': {Layout.Length} panel, {buttons} nút");
        }

        private static object Get(object o, string prop) => o.GetType().GetProperty(prop)?.GetValue(o);

        private static void Set(object o, string prop, object value)
        {
            try { o.GetType().GetProperty(prop)?.SetValue(o, value); }
            catch (Exception ex) { Logger.Warn($"[Ribbon] {o.GetType().Name}.{prop}: {ex.Message}"); }
        }

        private static void SetEnum(object o, string prop, string value)
        {
            var p = o.GetType().GetProperty(prop);
            if (p == null || !p.PropertyType.IsEnum) return;
            try { p.SetValue(o, Enum.Parse(p.PropertyType, value)); }
            catch (Exception ex) { Logger.Warn($"[Ribbon] {prop}={value}: {ex.Message}"); }
        }

        /// <summary>Icon vẽ bằng code: ô vuông bo góc màu + chữ viết tắt trắng.</summary>
        public static Bitmap Icon(LhbButton b, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int r = size / 5;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(size - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(size - r * 2 - 1, size - r * 2 - 1, r * 2, r * 2, 0, 90);
                    path.AddArc(0, size - r * 2 - 1, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    using (var br = new SolidBrush(b.Color)) g.FillPath(br, path);
                }
                float fs = b.Glyph.Length <= 1 ? size * 0.55f : size * 0.38f;
                using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(b.Glyph, f, Brushes.White, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        private static System.Windows.Media.ImageSource ToImageSource(Bitmap bmp)
        {
            using (bmp)
            {
                var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
    }

    /// <summary>Bấm nút Ribbon -> gửi lệnh vào dòng lệnh (huỷ lệnh đang chạy trước).</summary>
    internal class RibbonCommand : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            string cmd = parameter as string ?? parameter?.GetType().GetProperty("CommandParameter")?.GetValue(parameter) as string;
            if (string.IsNullOrEmpty(cmd)) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Logger.Log($"[Ribbon] Bấm nút {cmd}");
            doc.SendStringToExecute("\x03\x03" + cmd + " ", true, false, true);
        }
    }
}

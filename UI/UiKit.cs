using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Core;

namespace LHBBlockScheduler.UI
{
    /// <summary>Thành phần giao diện dùng chung cho các hộp thoại Premium.</summary>
    internal static class UiKit
    {
        public static readonly Color Blue = Color.FromArgb(41, 128, 185);
        public static readonly Color Gold = Color.FromArgb(243, 156, 18);
        public static readonly Color Red = Color.FromArgb(192, 57, 43);

        public static Button Btn(string text, EventHandler click, int width = 100, bool primary = false)
        {
            var b = new Button { Text = text, Width = width, Height = 30 };
            if (primary) { b.BackColor = Blue; b.ForeColor = Color.White; }
            b.Click += click;
            return b;
        }

        public static Label Header(string text) => new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Blue,
            ForeColor = Color.White,
            Padding = new Padding(8, 5, 0, 0),
            Text = text,
            AutoEllipsis = true
        };

        public static FlowLayoutPanel BottomBar(int height = 44) =>
            new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = height, Padding = new Padding(6), WrapContents = false };

        public static DataGridView Grid() => DoubleBuffer(new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            BackgroundColor = SystemColors.Window,
            AutoGenerateColumns = false
        });

        /// <summary>
        /// Bật vẽ đệm đôi cho lưới (thuộc tính DoubleBuffered là protected): cuộn / kéo cột lưới có ảnh ký hiệu không
        /// còn nháy.
        /// </summary>
        public static T DoubleBuffer<T>(T control) where T : Control
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                               ?.SetValue(control, true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[UiKit] Không bật được DoubleBuffered cho {control.GetType().Name}: {ex.Message}");
            }
            return control;
        }

        /// <summary>Hộp nhập 1 dòng chữ (WinForms không có InputBox sẵn). Trả null nếu bấm Huỷ.</summary>
        public static string PromptText(IWin32Window owner, string title, string label, string defaultValue)
        {
            using (var dlg = new Form
            {
                Text = title,
                Font = new Font("Segoe UI", 9F),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(360, 110)
            })
            {
                var lbl = new Label { Text = label, Left = 10, Top = 10, Width = 340, AutoEllipsis = true };
                var txt = new TextBox { Text = defaultValue, Left = 10, Top = 34, Width = 340 };
                var ok = new Button { Text = "OK", Left = 190, Top = 70, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Huỷ", Left = 275, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
            }
        }

        public static void InitForm(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Font = new Font("Segoe UI", 9F);
            f.Width = w;
            f.Height = h;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ShowInTaskbar = false;
        }

        /// <summary>
        /// Chọn điểm trên bản vẽ từ hộp thoại modeless: ẩn hộp thoại (và form cha), khoá bản vẽ, hỏi điểm, hiện lại.
        /// Trả điểm theo WCS.
        /// </summary>
        public static bool PickPoint(Form f, Document doc, string message, out Point3d wcs)
        {
            wcs = Point3d.Origin;
            var owner = f.Owner;
            f.Hide();
            owner?.Hide();
            try
            {
                using (doc.LockDocument())
                {
                    var r = doc.Editor.GetPoint(message);
                    if (r.Status != PromptStatus.OK) return false;
                    wcs = r.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                    return true;
                }
            }
            finally
            {
                owner?.Show();
                f.Show();
            }
        }

        /// <summary>Hộp thoại lưu file Excel, trả đường dẫn hoặc null.</summary>
        public static string AskExcelPath(IWin32Window owner, string defaultName)
        {
            using (var sfd = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = MakeFileName(defaultName) + ".xlsx" })
                return sfd.ShowDialog(owner) == DialogResult.OK ? sfd.FileName : null;
        }

        private static string MakeFileName(string s)
        {
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s;
        }

        /// <summary>Ô nhập số có nhãn, trả NumericUpDown.</summary>
        public static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals = 0, int width = 80) => new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Value = Math.Max(min, Math.Min(max, value)),
            Width = width,
            Increment = decimals > 0 ? 0.1m : 1
        };

        public static Label Lbl(string text) => new Label { Text = text, AutoSize = true, Padding = new Padding(4, 7, 0, 0) };

        /// <summary>Kiểm tra bản quyền Premium trước khi mở tính năng (dùng thử 30 ngày).</summary>
        public static bool Premium(string feature) => LicenseManager.EnsurePremium(feature);
    }
}

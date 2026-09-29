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

        public static DataGridView Grid() => new DataGridView
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
        };

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

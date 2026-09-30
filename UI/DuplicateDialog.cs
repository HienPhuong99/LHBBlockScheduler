using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Hộp thoại "Block trùng vị trí" (modeless, con của BlockScheduleForm): liệt kê chỗ trùng, zoom tới,
    /// khoanh đỏ, xoá bản thừa, đổi sai số rồi tìm lại.
    /// </summary>
    public class DuplicateDialog : Form
    {
        private readonly Document _doc;
        private readonly BlockScheduleForm _owner;
        private DataGridView _grid;
        private NumericUpDown _numTol, _numOverlap;
        private Label _lblSummary;
        private List<DuplicateGroup> _groups = new List<DuplicateGroup>();

        public DuplicateDialog(Document doc, BlockScheduleForm owner)
        {
            _doc = doc;
            _owner = owner;
            BuildUi();
            RefreshList();
            // v9.4: ẩn khi đổi bản vẽ, tự đóng khi bản vẽ đóng
            DocumentBinding.Bind(this, doc);
        }

        private void BuildUi()
        {
            UiKit.KeepFrameworkFont(this);
            Text = "Block trùng / che lấp nhau";
            Width = 820;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 6, 2), WrapContents = false };
            top.Controls.Add(new Label { Text = "Sai số vị trí:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            _numTol = new NumericUpDown
            {
                Width = 80,
                DecimalPlaces = 2,
                Minimum = 0.01m,
                Maximum = 100000m,
                Value = (decimal)Math.Min(100000, Math.Max(0.01, SettingsManager.Current.DuplicateTolerance))
            };
            top.Controls.Add(_numTol);
            // Block cùng tên che lấp nhau từ mức này (% diện tích block nhỏ hơn) = trùng, dù điểm chèn khác nhau
            top.Controls.Add(new Label { Text = "  Che lấp từ (%):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            _numOverlap = new NumericUpDown
            {
                Width = 60,
                DecimalPlaces = 0,
                Minimum = 1m,
                Maximum = 100m,
                Value = (decimal)Math.Min(100, Math.Max(1, SettingsManager.Current.DuplicateOverlapPercent))
            };
            top.Controls.Add(_numOverlap);
            top.Controls.Add(MakeButton("Tìm lại", (s, e) => Action_Redetect(), 70));
            _lblSummary = new Label { AutoSize = true, Padding = new Padding(10, 5, 0, 0) };
            top.Controls.Add(_lblSummary);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            UiKit.DoubleBuffer(_grid);
            _grid.Columns.Add("colName", "Tên thiết bị");
            _grid.Columns.Add("colVis", "Chủng loại");
            _grid.Columns.Add("colX", "X");
            _grid.Columns.Add("colY", "Y");
            _grid.Columns.Add("colCopies", "Số bản");
            _grid.Columns.Add("colKind", "Kiểu trùng");
            _grid.Columns.Add("colNote", "Ghi chú");
            _grid.Columns["colName"].FillWeight = 160;
            _grid.Columns["colCopies"].FillWeight = 50;
            _grid.Columns["colKind"].FillWeight = 90;
            _grid.Columns["colNote"].FillWeight = 150;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Action_Zoom(); };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), WrapContents = false };
            bottom.Controls.Add(MakeButton("Zoom tới", (s, e) => Action_Zoom(), 80));
            bottom.Controls.Add(MakeButton("Khoanh đỏ tất cả", (s, e) => Action_DrawCircles(), 115));
            bottom.Controls.Add(MakeButton("Tắt khoanh đỏ", (s, e) => Action_ClearMarkers(), 100));
            var btnDelete = MakeButton("Xoá bản thừa", (s, e) => Action_DeleteExtras(), 100);
            btnDelete.ForeColor = Color.FromArgb(192, 57, 43);
            bottom.Controls.Add(btnDelete);
            bottom.Controls.Add(MakeButton("Đóng", (s, e) => Close(), 70));

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);
        }

        private static Button MakeButton(string text, EventHandler onClick, int width)
        {
            var btn = new Button { Text = text, Width = width, Height = 30 };
            btn.Click += onClick;
            return btn;
        }

        /// <summary>Đọc lại chỗ trùng từ các dòng của form (gọi sau mỗi lần form tính lại trùng).</summary>
        public void RefreshList()
        {
            // 1 nhóm có thể gồm block của nhiều dòng (khác chủng loại) -> mỗi nhóm 1 dòng
            _groups = DuplicateFinder.AllGroups(_owner.AllItems)
                .OrderBy(g => g.Items.Select(i => i.Order).DefaultIfEmpty(0).Min())
                .ThenBy(g => g.Position.X).ThenBy(g => g.Position.Y)
                .ToList();

            _grid.Rows.Clear();
            foreach (var g in _groups)
            {
                var items = g.Items.ToList();
                int nested = g.Instances.Count(i => !i.IsTopLevel);
                string note = nested > 0 ? $"{nested} block nằm trong block cha / ARRAY / MINSERT (xoá tay)" : "";
                string names = string.Join(" / ", items.Select(i => i.DisplayName ?? i.BlockName).Distinct());
                string vis = string.Join(" / ", items.Select(i => i.VisibilityState ?? "").Where(v => v.Length > 0).Distinct());
                string kind = g.HasOverlap ? $"Che lấp {g.MaxOverlap:P0}" : "Cùng điểm chèn";
                _grid.Rows.Add(names, vis, g.Position.X.ToString("0.##"), g.Position.Y.ToString("0.##"), g.Instances.Count, kind, note);
            }

            int extra = _groups.Sum(g => g.Extra);
            _lblSummary.Text = _groups.Count == 0
                ? "Không có block trùng."
                : $"{_groups.Count} vị trí trùng, thừa {extra} block.";
            _lblSummary.ForeColor = _groups.Count == 0 ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 57, 43);
        }

        private DuplicateGroup SelectedGroup =>
            _grid.CurrentRow != null && _grid.CurrentRow.Index < _groups.Count ? _groups[_grid.CurrentRow.Index] : null;

        private void Action_Redetect()
        {
            double tol = (double)_numTol.Value;
            double overlap = (double)_numOverlap.Value;
            SettingsManager.Current.DuplicateTolerance = tol;
            SettingsManager.Current.DuplicateOverlapPercent = overlap;
            SettingsManager.SaveSettings();
            Logger.Log($"[DuplicateDialog] Tìm lại với sai số {tol}, che lấp từ {overlap}%");
            _owner.RecomputeDuplicates();
            RedrawCirclesIfAny();
        }

        private void Action_Zoom()
        {
            var g = SelectedGroup;
            if (g == null) return;
            try
            {
                DuplicateFinder.ZoomToGroup(_doc, g, _owner.MarkerRadius * 8);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DuplicateDialog.Action_Zoom");
            }
        }

        private void Action_DrawCircles()
        {
            if (_groups.Count == 0) return;
            RedrawCirclesIfAny();
        }

        /// <summary>Khoanh đỏ lại theo danh sách hiện tại (hoặc xoá dấu cũ nếu hết trùng).</summary>
        public void RedrawCirclesIfAny()
        {
            try
            {
                if (_groups.Count == 0)
                    DuplicateFinder.ClearMarkers(_doc);
                else
                    DuplicateFinder.DrawCircles(_doc, _owner.AllItems, _owner.MarkerRadius, _owner.MarkerTextHeight);
                _doc.Editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DuplicateDialog.RedrawCirclesIfAny");
            }
        }

        private void Action_ClearMarkers()
        {
            try
            {
                int n = DuplicateFinder.ClearMarkers(_doc);
                ScheduleManager.UnhighlightPrevious(_doc.Database);
                _doc.Editor.UpdateScreen();
                Logger.Log($"[DuplicateDialog] Tắt khoanh đỏ: xoá {n} nét");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DuplicateDialog.Action_ClearMarkers");
            }
        }

        private void Action_DeleteExtras()
        {
            int extra = _groups.Sum(g => g.Extra);
            if (extra == 0) return;

            var answer = MessageBox.Show(this,
                $"Xoá {extra} block thừa, mỗi vị trí giữ lại 1 block?\n\n" +
                "Block giữ lại là block vẽ trước (cũ nhất). Chỗ 'Che lấp' 2 block có thể khác cỡ / chủng loại:\n" +
                "nên Zoom tới xem trước, xoá sai thì Ctrl+Z.\n" +
                "Block nằm trong block cha / ARRAY / phần tử MINSERT sẽ không bị xoá (cần xoá tay).\n" +
                "Hoàn tác được bằng Ctrl+Z (lệnh U) trong AutoCAD.",
                "Xoá bản thừa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                ScheduleManager.UnhighlightPrevious(_doc.Database);
                var deleted = DuplicateFinder.DeleteExtras(_doc, _owner.AllItems, out int skipped);
                _owner.OnDuplicatesDeleted(deleted);
                RedrawCirclesIfAny();

                MessageBox.Show(this,
                    $"Đã xoá {deleted.Count} block thừa." + (skipped > 0 ? $"\nCòn {skipped} block nằm trong block cha / ARRAY / MINSERT, cần xoá tay." : ""),
                    "Xoá bản thừa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "DuplicateDialog.Action_DeleteExtras");
                MessageBox.Show(this, $"Xoá thất bại: {ex.Message}\nXem chi tiết tại: %APPDATA%\\LHBBlockScheduler\\log.txt", "Lỗi");
            }
        }
    }
}

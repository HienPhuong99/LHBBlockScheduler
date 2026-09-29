using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>Vùng bảo vệ PCCC (Premium P8, lệnh LHBVUNGBV): bán kính (m) + màu từng loại thiết bị, vẽ vòng / tô mờ / soát khoảng hở.</summary>
    public class CoverageDialog : Form
    {
        private readonly Document _doc;
        private readonly List<BlockItem> _items;
        private readonly DataGridView _grid;
        private readonly CheckBox _chkFill, _chkGaps;

        public CoverageDialog(Document doc, List<BlockItem> items)
        {
            _doc = doc;
            _items = items.Where(i => i.Instances != null && i.Instances.Count > 0).ToList();
            UiKit.InitForm(this, "Vùng bảo vệ PCCC - LHB Premium", 720, 500);
            StartPosition = FormStartPosition.CenterParent;
            _grid = UiKit.Grid();
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Thiết bị", Width = 300, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCount", HeaderText = "SL", Width = 50, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRadius", HeaderText = "Bán kính (m), 0 = không vẽ", Width = 170 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColor", HeaderText = "Màu (1-255)", Width = 90 });
            var radii = SettingsManager.Current.CoverageRadii;
            short color = 1;
            foreach (var it in _items)
            {
                radii.TryGetValue(DrawingHelper.ItemKey(it), out double r);
                color = (short)(color % 6 + 1);
                _grid.Rows.Add((it.DisplayName ?? it.BlockName) + (string.IsNullOrEmpty(it.VisibilityState) ? "" : $" [{it.VisibilityState}]"),
                               it.Count, r > 0 ? r.ToString("0.##", CultureInfo.InvariantCulture) : "0", color);
            }

            _chkFill = new CheckBox { Text = "Tô mờ vùng bảo vệ", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
            _chkGaps = new CheckBox { Text = "Soát khoảng hở (2 thiết bị gần nhất cách > 2R)", AutoSize = true, Checked = true, Padding = new Padding(6, 6, 0, 0) };
            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Vẽ vùng bảo vệ", (s, e) => Run(), 120, true));
            bar.Controls.Add(_chkFill);
            bar.Controls.Add(_chkGaps);
            bar.Controls.Add(UiKit.Btn("Xoá vùng bảo vệ", (s, e) =>
            {
                int n = CoverageDrawer.Clear(_doc);
                MessageBox.Show(this, $"Đã xoá {n} đối tượng trên layer {CoverageDrawer.LayerName}.", "Vùng bảo vệ");
            }, 120));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 60));

            Controls.Add(_grid);
            Controls.Add(UiKit.Header($"Nhập bán kính bảo vệ theo tiêu chuẩn áp dụng (TCVN / NFPA...). {DrawingHelper.UnitLabel(doc.Database)}"));
            Controls.Add(bar);
        }

        private void Run()
        {
            _grid.EndEdit();
            var specs = new List<CoverageDrawer.Spec>();
            var radii = SettingsManager.Current.CoverageRadii;
            for (int i = 0; i < _items.Count; i++)
            {
                string rs = (_grid.Rows[i].Cells["colRadius"].Value?.ToString() ?? "0").Replace(',', '.');
                double.TryParse(rs, NumberStyles.Any, CultureInfo.InvariantCulture, out double r);
                short.TryParse(_grid.Rows[i].Cells["colColor"].Value?.ToString(), out short c);
                radii[DrawingHelper.ItemKey(_items[i])] = r;
                if (r > 0) specs.Add(new CoverageDrawer.Spec { Item = _items[i], RadiusM = r, ColorIndex = c });
            }
            SettingsManager.SaveSettings();
            if (specs.Count == 0)
            {
                MessageBox.Show(this, "Nhập bán kính (m) cho ít nhất 1 loại thiết bị.", "Vùng bảo vệ");
                return;
            }
            try
            {
                var (circles, gaps) = CoverageDrawer.Draw(_doc, specs, _chkFill.Checked, _chkGaps.Checked);
                _doc.Editor.UpdateScreen();
                MessageBox.Show(this, $"Đã vẽ {circles} vòng bảo vệ" + (_chkGaps.Checked ? $", {gaps} khoảng hở (đoạn đỏ)" : "") +
                                      $".\nLayer {CoverageDrawer.LayerName} không in.", "Vùng bảo vệ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CoverageDialog] Vẽ vùng bảo vệ");
                MessageBox.Show(this, $"Vẽ vùng bảo vệ lỗi: {ex.Message}", "Vùng bảo vệ");
            }
        }
    }
}

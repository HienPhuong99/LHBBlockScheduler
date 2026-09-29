using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>Đánh số thiết bị (Premium P7, lệnh LHBDANHSO): tiền tố + số bắt đầu từng loại, thứ tự, ghi chữ / thuộc tính.</summary>
    public class NumberingDialog : Form
    {
        private readonly Document _doc;
        private readonly List<BlockItem> _items;
        private readonly DataGridView _grid;
        private readonly ComboBox _cboOrder, _cboTag;
        private readonly NumericUpDown _numDigits, _numHeight;
        private readonly CheckBox _chkText, _chkZone;

        public NumberingDialog(Document doc, List<BlockItem> items)
        {
            _doc = doc;
            _items = items.Where(i => i.Instances != null && i.Instances.Count > 0).ToList();
            UiKit.InitForm(this, "Đánh số thiết bị - LHB Premium", 760, 520);
            StartPosition = FormStartPosition.CenterParent;

            _grid = UiKit.Grid();
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colOn", HeaderText = "Đánh số", Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Thiết bị", Width = 260, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCount", HeaderText = "SL", Width = 50, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPrefix", HeaderText = "Tiền tố", Width = 110 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStart", HeaderText = "Số bắt đầu", Width = 80 });
            foreach (var it in _items)
                _grid.Rows.Add(true, (it.DisplayName ?? it.BlockName) + (string.IsNullOrEmpty(it.VisibilityState) ? "" : $" [{it.VisibilityState}]"),
                               it.Count, DeviceNumbering.SuggestPrefix(it), 1);

            var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(6), WrapContents = true };
            _cboOrder = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboOrder.Items.AddRange(new object[] { "Trái -> phải, trên -> dưới", "Trên -> dưới, trái -> phải", "Theo khu vực, rồi trái -> phải" });
            _cboOrder.SelectedIndex = 0;
            _numDigits = UiKit.Number(1, 5, 2, 0, 50);
            double typical = CountChecker.TypicalSize(_items);
            _numHeight = UiKit.Number(0, 100000, (decimal)Math.Round(typical * 0.35, 1), 1, 90);
            _chkText = new CheckBox { Text = "Tạo chữ cạnh block", Checked = true, AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
            _chkZone = new CheckBox { Text = "Đánh lại từ đầu mỗi khu vực", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
            _cboTag = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboTag.Items.Add("(không ghi)");
            foreach (var k in PremiumColumns.CollectAttributeKeys(_items).Where(k => k.StartsWith("A:"))) _cboTag.Items.Add(k.Substring(2));
            _cboTag.SelectedIndex = 0;
            opts.Controls.AddRange(new Control[]
            {
                UiKit.Lbl("Thứ tự:"), _cboOrder, UiKit.Lbl("Số chữ số:"), _numDigits, UiKit.Lbl("Cao chữ:"), _numHeight, _chkText,
                UiKit.Lbl("Ghi vào thuộc tính:"), _cboTag, _chkZone
            });

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Đánh số", (s, e) => Run(), 90, true));
            bar.Controls.Add(UiKit.Btn("Xoá số đã đánh (chữ)", (s, e) =>
            {
                int n = DeviceNumbering.ClearTexts(_doc);
                MessageBox.Show(this, $"Đã xoá {n} chữ số trên layer {DeviceNumbering.TextLayer}.", "Đánh số");
            }, 150));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 70));

            Controls.Add(_grid);
            Controls.Add(UiKit.Header("Chữ số đặt trên layer LHB_DANHSO (in được). Block thừa do trùng không đánh số."));
            Controls.Add(opts);
            Controls.Add(bar);
        }

        private void Run()
        {
            _grid.EndEdit();
            var specs = new List<DeviceNumbering.TypeSpec>();
            var prefixes = SettingsManager.Current.NumberingPrefixes;
            for (int i = 0; i < _items.Count; i++)
            {
                var row = _grid.Rows[i];
                if (!(row.Cells["colOn"].Value is bool on) || !on) continue;
                string prefix = row.Cells["colPrefix"].Value?.ToString() ?? "";
                int.TryParse(row.Cells["colStart"].Value?.ToString(), out int start);
                specs.Add(new DeviceNumbering.TypeSpec { Item = _items[i], Prefix = prefix, Start = start > 0 ? start : 1 });
                prefixes[DrawingHelper.ItemKey(_items[i])] = prefix;
            }
            SettingsManager.SaveSettings();
            if (specs.Count == 0) return;
            try
            {
                var o = new DeviceNumbering.Options
                {
                    Digits = (int)_numDigits.Value,
                    Order = (DeviceNumbering.SortOrder)_cboOrder.SelectedIndex,
                    CreateText = _chkText.Checked,
                    TextHeight = (double)_numHeight.Value,
                    AttributeTag = _cboTag.SelectedIndex > 0 ? _cboTag.SelectedItem.ToString() : "",
                    RestartPerZone = _chkZone.Checked
                };
                int n = DeviceNumbering.Apply(_doc, specs, o);
                _doc.Editor.UpdateScreen();
                MessageBox.Show(this, $"Đã đánh số {n} thiết bị." + (o.CreateText ? $"\nChữ số trên layer {DeviceNumbering.TextLayer}." : ""),
                    "Đánh số", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[NumberingDialog] Đánh số");
                MessageBox.Show(this, $"Đánh số lỗi: {ex.Message}", "Đánh số");
            }
        }
    }
}

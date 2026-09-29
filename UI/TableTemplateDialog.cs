using System;
using System.Linq;
using System.Windows.Forms;
using LHBBlockScheduler.Core;

namespace LHBBlockScheduler.UI
{
    /// <summary>Mẫu bảng xuất (Premium P10, lệnh LHBMAUBANG): tiêu đề, dòng phụ, dòng tổng, font, cỡ chữ, màu. Lưu nhiều mẫu.</summary>
    public class TableTemplateDialog : Form
    {
        private readonly ComboBox _cbo;
        private readonly TextBox _txtTitle, _txtSub, _txtTotal, _txtFont;
        private readonly CheckBox _chkHideTitle, _chkTotal, _chkUpper;
        private readonly NumericUpDown _numText, _numRow, _numHeaderColor, _numTitleColor;

        public TableTemplateDialog()
        {
            UiKit.InitForm(this, "Mẫu bảng xuất - LHB Premium", 560, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _cbo = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDown };
            _cbo.SelectionChangeCommitted += (s, e) => LoadTemplate(_cbo.SelectedItem?.ToString());
            _txtTitle = new TextBox { Width = 340 };
            _txtSub = new TextBox { Width = 340 };
            _chkHideTitle = new CheckBox { Text = "Ẩn dòng tiêu đề", AutoSize = true };
            _chkTotal = new CheckBox { Text = "Thêm dòng tổng cuối bảng", AutoSize = true };
            _txtTotal = new TextBox { Width = 200 };
            _chkUpper = new CheckBox { Text = "Tiêu đề cột viết HOA", AutoSize = true };
            _txtFont = new TextBox { Width = 200 };
            _numText = UiKit.Number(0.3m, 5m, 1m, 2);
            _numRow = UiKit.Number(0.3m, 5m, 1m, 2);
            _numHeaderColor = UiKit.Number(0, 255, 0);
            _numTitleColor = UiKit.Number(0, 255, 0);

            void Row(string label, Control c) { t.Controls.Add(UiKit.Lbl(label)); t.Controls.Add(c); }
            Row("Mẫu:", _cbo);
            Row("Tiêu đề bảng:", _txtTitle);
            Row("Dòng phụ (công trình...):", _txtSub);
            Row("", _chkHideTitle);
            Row("", _chkTotal);
            Row("Nhãn dòng tổng:", _txtTotal);
            Row("", _chkUpper);
            Row("File font (vd arial.ttf):", _txtFont);
            Row("Hệ số cỡ chữ:", _numText);
            Row("Hệ số chiều cao dòng:", _numRow);
            Row("Màu nền header (0-255):", _numHeaderColor);
            Row("Màu nền tiêu đề (0-255):", _numTitleColor);

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Lưu & dùng mẫu này", (s, e) => SaveTemplate(), 140, true));
            bar.Controls.Add(UiKit.Btn("Xoá mẫu", (s, e) => DeleteTemplate(), 80));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 70));
            Controls.Add(t);
            Controls.Add(UiKit.Header("Mã màu AutoCAD: 1 đỏ, 2 vàng, 3 xanh lá, 4 xanh ngọc, 5 xanh dương, 8/9 xám, 0 = không tô"));
            Controls.Add(bar);

            RefreshList();
            LoadTemplate(TableTemplate.Current.Name);
        }

        private void RefreshList()
        {
            _cbo.Items.Clear();
            _cbo.Items.Add(TableTemplate.DefaultName);
            foreach (var x in SettingsManager.Current.TableTemplates) if (x.Name != TableTemplate.DefaultName) _cbo.Items.Add(x.Name);
        }

        private void LoadTemplate(string name)
        {
            var x = SettingsManager.Current.TableTemplates.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? new TableTemplate { Name = name ?? TableTemplate.DefaultName };
            _cbo.Text = x.Name;
            _txtTitle.Text = x.EffectiveTitle;
            _txtSub.Text = x.Subtitle ?? "";
            _chkHideTitle.Checked = x.HideTitle;
            _chkTotal.Checked = x.AddTotalRow;
            _txtTotal.Text = x.EffectiveTotalLabel;
            _chkUpper.Checked = x.UppercaseHeader;
            _txtFont.Text = x.EffectiveFont;
            _numText.Value = (decimal)Math.Max(0.3, Math.Min(5, x.EffectiveTextFactor));
            _numRow.Value = (decimal)Math.Max(0.3, Math.Min(5, x.EffectiveRowFactor));
            _numHeaderColor.Value = Math.Max((short)0, x.HeaderColorIndex);
            _numTitleColor.Value = Math.Max((short)0, x.TitleColorIndex);
        }

        private void SaveTemplate()
        {
            string name = (_cbo.Text ?? "").Trim();
            if (name.Length == 0) name = TableTemplate.DefaultName;
            var s = SettingsManager.Current;
            s.TableTemplates.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            s.TableTemplates.Add(new TableTemplate
            {
                Name = name,
                Title = _txtTitle.Text.Trim(),
                Subtitle = _txtSub.Text.Trim(),
                HideTitle = _chkHideTitle.Checked,
                AddTotalRow = _chkTotal.Checked,
                TotalLabel = _txtTotal.Text.Trim(),
                UppercaseHeader = _chkUpper.Checked,
                FontFile = _txtFont.Text.Trim(),
                TextHeightFactor = (double)_numText.Value,
                RowHeightFactor = (double)_numRow.Value,
                HeaderColorIndex = (short)_numHeaderColor.Value,
                TitleColorIndex = (short)_numTitleColor.Value
            });
            s.CurrentTableTemplate = name;
            SettingsManager.SaveSettings();
            Logger.Log($"[TableTemplateDialog] Lưu và dùng mẫu bảng '{name}'");
            RefreshList();
            _cbo.Text = name;
            MessageBox.Show(this, $"Đã lưu mẫu '{name}'. Lần xuất bảng / Excel sau dùng mẫu này.", "Mẫu bảng", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DeleteTemplate()
        {
            string name = (_cbo.Text ?? "").Trim();
            var s = SettingsManager.Current;
            if (s.TableTemplates.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return;
            if (string.Equals(s.CurrentTableTemplate, name, StringComparison.OrdinalIgnoreCase)) s.CurrentTableTemplate = null;
            SettingsManager.SaveSettings();
            RefreshList();
            LoadTemplate(TableTemplate.Current.Name);
        }
    }
}

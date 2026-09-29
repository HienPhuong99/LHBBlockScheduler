using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>Thay block hàng loạt (Premium P12, lệnh LHBTHAYBLOCK): chọn block đích (trong bản vẽ / block mẫu) + chủng loại.</summary>
    public class ReplaceBlockDialog : Form
    {
        private readonly Document _doc;
        private readonly List<BlockItem> _items;
        private readonly ComboBox _cboTarget, _cboVis;
        private readonly CheckBox _chkFit, _chkRot, _chkLayer, _chkAttr;
        private List<(string Label, string Name, bool FromTemplate)> _targets;

        public BlockReplacer.Result Result { get; private set; }

        public ReplaceBlockDialog(Document doc, List<BlockItem> items)
        {
            _doc = doc;
            _items = items;
            UiKit.InitForm(this, "Thay block - LHB Premium", 560, 330);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;

            int total = items.Sum(i => i.Instances?.Count ?? 0);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _cboTarget = new ComboBox { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboTarget.SelectedIndexChanged += (s, e) => LoadVisibility();
            _cboVis = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            _chkFit = new CheckBox { Text = "Co tỉ lệ theo kích thước block cũ", AutoSize = true };
            _chkRot = new CheckBox { Text = "Giữ góc xoay", AutoSize = true, Checked = true };
            _chkLayer = new CheckBox { Text = "Giữ layer", AutoSize = true, Checked = true };
            _chkAttr = new CheckBox { Text = "Chép giá trị thuộc tính cùng tag", AutoSize = true, Checked = true };
            t.Controls.Add(UiKit.Lbl("Thay bằng block:")); t.Controls.Add(_cboTarget);
            t.Controls.Add(UiKit.Lbl("Chủng loại:")); t.Controls.Add(_cboVis);
            t.Controls.Add(new Label()); t.Controls.Add(_chkFit);
            t.Controls.Add(new Label()); t.Controls.Add(_chkRot);
            t.Controls.Add(new Label()); t.Controls.Add(_chkLayer);
            t.Controls.Add(new Label()); t.Controls.Add(_chkAttr);

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Thay block", (s, e) => Run(), 100, true));
            bar.Controls.Add(UiKit.Btn("Huỷ", (s, e) => Close(), 70));
            Controls.Add(t);
            Controls.Add(UiKit.Header($"Thay {total} block của {items.Count} dòng đã chọn. Ctrl+Z trong AutoCAD để hoàn tác."));
            Controls.Add(bar);

            _targets = BlockReplacer.ListBlocks(doc.Database).Select(b => (b.Name, b.Name, false)).ToList();
            var tpl = TemplateLibraryManager.Load(SettingsManager.Current.CurrentTemplateSet);
            foreach (var n in tpl.Entries.Select(e => e.BlockName).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!_targets.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)))
                    _targets.Add(($"{n}  (block mẫu '{tpl.Name}')", n, true));
            foreach (var x in _targets) _cboTarget.Items.Add(x.Label);
            if (_cboTarget.Items.Count > 0) _cboTarget.SelectedIndex = 0;
        }

        private ObjectId TargetId(bool import)
        {
            if (_cboTarget.SelectedIndex < 0) return ObjectId.Null;
            var x = _targets[_cboTarget.SelectedIndex];
            if (x.FromTemplate)
                return import ? TemplateLibraryManager.ImportBlock(_doc, SettingsManager.Current.CurrentTemplateSet, x.Name) : ObjectId.Null;
            return BlockReplacer.ListBlocks(_doc.Database).FirstOrDefault(b => b.Name == x.Name).Id;
        }

        private void LoadVisibility()
        {
            _cboVis.Items.Clear();
            _cboVis.Items.Add("(giữ mặc định)");
            var id = TargetId(false);
            if (!id.IsNull) foreach (var v in BlockReplacer.VisibilityStates(_doc, id)) _cboVis.Items.Add(v);
            _cboVis.SelectedIndex = 0;
        }

        private void Run()
        {
            try
            {
                ObjectId id;
                using (_doc.LockDocument()) id = TargetId(true);
                if (id.IsNull)
                {
                    MessageBox.Show(this, "Không lấy được block đích (block mẫu chưa có hình trong file .dwg của thư viện?).", "Thay block");
                    return;
                }
                var o = new BlockReplacer.Options
                {
                    TargetBtrId = id,
                    TargetVisibility = _cboVis.SelectedIndex > 0 ? _cboVis.SelectedItem.ToString() : "",
                    FitSize = _chkFit.Checked,
                    KeepRotation = _chkRot.Checked,
                    KeepLayer = _chkLayer.Checked,
                    CopyAttributes = _chkAttr.Checked
                };
                if (MessageBox.Show(this, $"Thay {_items.Sum(i => i.Instances?.Count ?? 0)} block bằng '{_targets[_cboTarget.SelectedIndex].Name}'" +
                                          (o.TargetVisibility.Length > 0 ? $" [{o.TargetVisibility}]" : "") + "?",
                        "Thay block", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Result = BlockReplacer.Replace(_doc, _items, o);
                _doc.Editor.UpdateScreen();
                MessageBox.Show(this, $"Đã thay {Result.Replaced} block." +
                                      (Result.SkippedNested > 0 ? $"\nBỏ qua {Result.SkippedNested} block nằm trong block cha (mở block cha bằng BEDIT để thay)." : ""),
                    "Thay block", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ReplaceBlockDialog] Thay block");
                MessageBox.Show(this, $"Thay block lỗi: {ex.Message}", "Thay block");
            }
        }
    }
}

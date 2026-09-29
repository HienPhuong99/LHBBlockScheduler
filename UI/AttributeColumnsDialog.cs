using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LHBBlockScheduler.Core;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Cột thuộc tính (Premium P5): chọn thuộc tính / tham số dynamic block hiện thành cột, và thuộc tính dùng để tách
    /// dòng (vd tách đầu phun theo thuộc tính K-FACTOR, tủ điện theo CÔNG SUẤT).
    /// </summary>
    public class AttributeColumnsDialog : Form
    {
        private readonly DataGridView _grid;
        private readonly List<string> _keys;

        public List<string> ShowKeys { get; private set; } = new List<string>();
        public List<string> SplitKeys { get; private set; } = new List<string>();

        public AttributeColumnsDialog(List<string> keys)
        {
            _keys = keys;
            UiKit.InitForm(this, "Cột thuộc tính - LHB Premium", 560, 440);
            StartPosition = FormStartPosition.CenterParent;
            _grid = UiKit.Grid();
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKey", HeaderText = "Thuộc tính / tham số", Width = 260, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colShow", HeaderText = "Hiện cột", Width = 80 });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colSplit", HeaderText = "Tách dòng", Width = 80 });
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            var s = SettingsManager.Current;
            foreach (var k in keys)
                _grid.Rows.Add(BlockExtractor.AttributeKeyLabel(k), s.AttributeColumns.Contains(k), s.SplitAttributeKeys.Contains(k));

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Áp dụng", (a, e) => Apply(), 90, true));
            bar.Controls.Add(UiKit.Btn("Huỷ", (a, e) => { DialogResult = DialogResult.Cancel; Close(); }, 80));
            Controls.Add(_grid);
            Controls.Add(UiKit.Header(keys.Count == 0
                ? "Các block đã quét không có thuộc tính / tham số nào"
                : "Tách dòng: mỗi giá trị khác nhau của thuộc tính thành 1 dòng riêng"));
            Controls.Add(bar);
        }

        private void Apply()
        {
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (_grid.Rows[i].Cells["colShow"].Value is bool b1 && b1) ShowKeys.Add(_keys[i]);
                if (_grid.Rows[i].Cells["colSplit"].Value is bool b2 && b2) SplitKeys.Add(_keys[i]);
            }
            // Giữ lựa chọn của thuộc tính không có trong lần quét này (quét bản vẽ khác)
            var s = SettingsManager.Current;
            s.AttributeColumns = s.AttributeColumns.Where(k => !_keys.Contains(k)).Concat(ShowKeys).Distinct().ToList();
            s.SplitAttributeKeys = SplitKeys.ToList();
            SettingsManager.SaveSettings();
            Logger.Log($"[AttributeColumnsDialog] Hiện cột [{string.Join(", ", ShowKeys)}], tách dòng [{string.Join(", ", SplitKeys)}]");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

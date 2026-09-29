using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.UI
{
    public class MergeCandidateGroup
    {
        public bool IsSelected { get; set; } = true;
        public BlockItem PrimaryItem { get; set; }
        public List<BlockItem> CandidateItems { get; set; } = new List<BlockItem>();
        public int MinHammingDistance { get; set; }
        public string Description { get; set; }
        public bool HasDiffVisibility { get; set; }
        public string WarningText { get; set; }
    }

    public class SuggestMergeDialog : Form
    {
        public List<MergeCandidateGroup> SelectedGroups { get; private set; } = new List<MergeCandidateGroup>();

        private readonly List<MergeCandidateGroup> _candidateGroups = new List<MergeCandidateGroup>();
        private DataGridView _grid;
        private Button _btnOk, _btnCancel;
        private Label _lblSummary;

        public SuggestMergeDialog(List<BlockItem> items, int threshold)
        {
            Text = $"Gợi ý gộp Block theo hình dạng (dHash <= {threshold})";
            Width = 720;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;

            FindCandidates(items, threshold);
            BuildUi();
        }

        private void FindCandidates(List<BlockItem> items, int threshold)
        {
            var validItems = items.Where(i => i.ShapeHash != 0).ToList();
            var processed = new HashSet<BlockItem>();

            for (int i = 0; i < validItems.Count; i++)
            {
                var a = validItems[i];
                if (processed.Contains(a)) continue;

                var cluster = new List<BlockItem> { a };
                int bestDist = int.MaxValue;

                for (int j = i + 1; j < validItems.Count; j++)
                {
                    var b = validItems[j];
                    if (processed.Contains(b)) continue;

                    int dist = ThumbnailGenerator.HammingDistance(a.ShapeHash, b.ShapeHash);
                    if (dist <= threshold)
                    {
                        cluster.Add(b);
                        if (dist < bestDist) bestDist = dist;
                        processed.Add(b);
                    }
                }

                if (cluster.Count > 1)
                {
                    processed.Add(a);
                    bool hasDiffVis = cluster.Select(c => c.VisibilityState ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

                    string desc = string.Join(" + ", cluster.Select(c =>
                        !string.IsNullOrEmpty(c.VisibilityState)
                            ? $"{c.BlockName} [{c.VisibilityState}] ({c.Count})"
                            : $"{c.BlockName} ({c.Count})"));

                    string warning = null;
                    if (hasDiffVis)
                    {
                        warning = "⚠️ CẢNH BÁO: Các block khác chủng loại (VisibilityState) - gộp vào sẽ làm sai lệch bảng vật tư!";
                        desc = "⚠️ [KHÁC CHỦNG LOẠI] " + desc;
                    }

                    _candidateGroups.Add(new MergeCandidateGroup
                    {
                        IsSelected = !hasDiffVis, // Mặc định bỏ chọn nếu khác chủng loại để bảo đảm an toàn
                        PrimaryItem = a,
                        CandidateItems = cluster,
                        MinHammingDistance = bestDist == int.MaxValue ? 0 : bestDist,
                        Description = desc,
                        HasDiffVisibility = hasDiffVis,
                        WarningText = warning
                    });
                }
            }
        }

        private void BuildUi()
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10) };
            _lblSummary = new Label
            {
                Dock = DockStyle.Fill,
                Text = _candidateGroups.Count > 0
                    ? $"Tìm thấy {_candidateGroups.Count} nhóm có hình dạng gần giống nhau. Vui lòng kiểm tra và tích chọn nhóm muốn gộp:"
                    : "Không tìm thấy nhóm Block nào có hình dạng trùng khớp trong ngưỡng đã chọn."
            };
            topPanel.Controls.Add(_lblSummary);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                RowTemplate = { Height = 56 }
            };
            UiKit.DoubleBuffer(_grid);

            var colCheck = new DataGridViewCheckBoxColumn
            {
                Name = "colCheck",
                HeaderText = "Gộp",
                Width = 50,
                DataPropertyName = "IsSelected"
            };
            var colThumb = new DataGridViewImageColumn
            {
                Name = "colThumb",
                HeaderText = "Ảnh đại diện",
                Width = 60,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            var colDesc = new DataGridViewTextBoxColumn
            {
                Name = "colDesc",
                HeaderText = "Các Block tương đồng (Số lượng)",
                Width = 420,
                ReadOnly = true
            };
            var colDist = new DataGridViewTextBoxColumn
            {
                Name = "colDist",
                HeaderText = "Khoảng cách",
                Width = 100,
                ReadOnly = true
            };

            _grid.Columns.AddRange(new DataGridViewColumn[] { colCheck, colThumb, colDesc, colDist });

            foreach (var g in _candidateGroups)
            {
                int rowIdx = _grid.Rows.Add();
                var row = _grid.Rows[rowIdx];
                row.Tag = g;
                row.Cells["colCheck"].Value = g.IsSelected;
                row.Cells["colDesc"].Value = g.Description;
                row.Cells["colDist"].Value = $"{g.MinHammingDistance} bit";

                if (g.HasDiffVisibility)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                    row.DefaultCellStyle.ForeColor = Color.DarkRed;
                    row.Cells["colDesc"].ToolTipText = g.WarningText;
                }

                string thumbPath = g.PrimaryItem.ThumbnailPath;
                if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                {
                    try
                    {
                        using (var fs = new FileStream(thumbPath, FileMode.Open, FileAccess.Read))
                        using (var tmp = Image.FromStream(fs))
                            row.Cells["colThumb"].Value = new Bitmap(tmp);
                    }
                    catch { }
                }
            }

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6)
            };

            _btnCancel = new Button { Text = "Huỷ bỏ", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            _btnOk = new Button { Text = "Xác nhận gộp", Width = 110, Height = 30 };
            _btnOk.Click += (s, e) =>
            {
                SelectedGroups.Clear();
                bool hasWarnedGroup = false;

                foreach (DataGridViewRow r in _grid.Rows)
                {
                    if (r.Tag is MergeCandidateGroup grp && Convert.ToBoolean(r.Cells["colCheck"].Value))
                    {
                        if (grp.HasDiffVisibility) hasWarnedGroup = true;
                        SelectedGroups.Add(grp);
                    }
                }

                if (hasWarnedGroup)
                {
                    var confirm = MessageBox.Show(
                        "CẢNH BÁO: Trong danh sách bạn chọn có nhóm gồm các Block KHÁC CHỦNG LOẠI (VisibilityState)!\n\n" +
                        "Việc gộp các chủng loại khác nhau vào 1 dòng sẽ làm sai lệch bảng vật tư.\n\n" +
                        "Bạn có chắc chắn vẫn muốn gộp các nhóm này không?",
                        "Cảnh báo sai lệch bảng vật tư",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes)
                    {
                        return;
                    }
                }

                DialogResult = DialogResult.OK;
                Close();
            };

            bottomPanel.Controls.Add(_btnCancel);
            bottomPanel.Controls.Add(_btnOk);

            Controls.Add(_grid);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }
    }
}

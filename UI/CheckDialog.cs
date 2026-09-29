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
    /// <summary>Soát lỗi đếm (Premium P6, lệnh LHBSOATLOI): danh sách lỗi, double-click để zoom + sáng block / nét lỗi.</summary>
    public class CheckDialog : Form
    {
        private readonly Document _doc;
        private readonly Func<List<BlockItem>> _items;
        private readonly DataGridView _grid;
        private readonly CheckBox _chkExploded;
        private readonly Label _lblSummary;
        private List<CheckIssue> _issues = new List<CheckIssue>();

        /// <param name="items">Hàm lấy danh sách dòng hiện tại (form có thể đã gộp / quét thêm).</param>
        public CheckDialog(Document doc, Func<List<BlockItem>> items)
        {
            _doc = doc;
            _items = items;
            UiKit.InitForm(this, "Soát lỗi đếm - LHB Premium", 900, 520);
            _grid = UiKit.Grid();
            _grid.ReadOnly = true;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKind", HeaderText = "Loại lỗi", Width = 150 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBlock", HeaderText = "Thiết bị", Width = 180 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDetail", HeaderText = "Chi tiết", Width = 430 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPos", HeaderText = "Vị trí", Width = 110 });
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ZoomTo(e.RowIndex); };

            _chkExploded = new CheckBox { Text = "Tìm cả block bị explode (chậm hơn)", AutoSize = true, Checked = true, Padding = new Padding(6, 6, 0, 0) };
            _lblSummary = new Label { AutoSize = true, Padding = new Padding(10, 7, 0, 0) };
            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Soát lại", (s, e) => RunCheck(), 80, true));
            bar.Controls.Add(_chkExploded);
            bar.Controls.Add(UiKit.Btn("Zoom", (s, e) => { if (_grid.CurrentRow != null) ZoomTo(_grid.CurrentRow.Index); }, 60));
            bar.Controls.Add(UiKit.Btn("Khoanh tất cả", (s, e) => CountChecker.DrawMarkers(_doc, _issues), 100));
            bar.Controls.Add(UiKit.Btn("Xoá khoanh", (s, e) => DrawingHelper.EraseOnLayer(_doc, CountChecker.MarkerLayer), 85));
            bar.Controls.Add(UiKit.Btn("Xuất Excel", (s, e) => ExportExcel(), 85));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 60));
            bar.Controls.Add(_lblSummary);

            Controls.Add(_grid);
            Controls.Add(UiKit.Header("Các chỗ có thể làm sai số lượng. Double-click dòng để zoom tới chỗ lỗi."));
            Controls.Add(bar);
            Shown += (s, e) => RunCheck();
        }

        private void RunCheck()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _issues = CountChecker.Run(_doc, _items(), _chkExploded.Checked);
                _grid.Rows.Clear();
                foreach (var i in _issues.OrderBy(i => i.Kind))
                    _grid.Rows.Add(i.Kind, i.BlockName, i.Detail, $"{i.Position.X:0}, {i.Position.Y:0}");
                _issues = _issues.OrderBy(i => i.Kind).ToList();
                _lblSummary.Text = _issues.Count == 0 ? "Không thấy lỗi nào." :
                    $"{_issues.Count} lỗi: " + string.Join(", ", _issues.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count()}"));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CheckDialog] Soát lỗi");
                MessageBox.Show(this, $"Soát lỗi thất bại: {ex.Message}", "Soát lỗi");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ZoomTo(int row)
        {
            if (row < 0 || row >= _issues.Count) return;
            var i = _issues[row];
            DrawingHelper.ZoomToPoint(_doc, i.Position, i.Size);
            if (i.LooseIds != null && i.LooseIds.Count > 0) ScheduleManager.ZoomAndHighlight(_doc, i.LooseIds);
            else if (i.Path != null) ScheduleManager.HighlightPaths(_doc, new List<ObjectId[]> { i.Path });
        }

        private void ExportExcel()
        {
            string path = UiKit.AskExcelPath(this, "Soat loi dem");
            if (path == null) return;
            try
            {
                var w = new XlsxWriter();
                ExcelExporter.AddGridSheet(w, "Soát lỗi", "DANH SÁCH LỖI ĐẾM", new List<string> { "STT", "Loại lỗi", "Thiết bị", "Chi tiết", "X", "Y" },
                    _issues.Select((i, k) => new object[] { k + 1, i.Kind, i.BlockName, i.Detail, Math.Round(i.Position.X, 1), Math.Round(i.Position.Y, 1) }).ToList(),
                    new List<double> { 45, 150, 180, 480, 90, 90 });
                w.Save(path);
                ExcelExporter.Open(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CheckDialog] Xuất Excel");
                MessageBox.Show(this, $"Xuất Excel lỗi: {ex.Message}", "Soát lỗi");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;
using TableTemplate = LHBBlockScheduler.Core.TableTemplate;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Thống kê nhiều bản vẽ (Premium P3, lệnh LHBNHIEUBV): chọn nhiều file DWG, mỗi bản vẽ 1 cột SL + cột Tổng.
    /// Dùng bộ block mẫu + tuỳ chọn quét hiện tại. Xuất AutoCAD Table (có ký hiệu nếu bản vẽ đang mở có block đó
    /// hoặc block mẫu có hình) và Excel.
    /// </summary>
    public class MultiDrawingDialog : Form
    {
        private readonly Document _doc;
        private readonly ListBox _lstFiles;
        private readonly CheckBox _chkCurrent, _chkSplitVis, _chkOnlyTemplate;
        private readonly DataGridView _grid;
        private readonly Label _lblStatus;
        private List<MultiRow> _rows = new List<MultiRow>();
        private List<string> _labels = new List<string>();

        public MultiDrawingDialog(Document doc)
        {
            _doc = doc;
            UiKit.InitForm(this, "Thống kê nhiều bản vẽ - LHB Premium", 1000, 600);

            var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(6) };
            _lstFiles = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };
            var fileBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 150, WrapContents = true };
            _chkCurrent = new CheckBox { Text = "Gồm bản vẽ đang mở", AutoSize = true, Checked = true };
            _chkSplitVis = new CheckBox { Text = "Tách theo chủng loại", AutoSize = true, Checked = true };
            var tpl = TemplateLibraryManager.Load(SettingsManager.Current.CurrentTemplateSet);
            _chkOnlyTemplate = new CheckBox
            {
                Text = $"Chỉ block mẫu (bộ '{tpl.Name}', {tpl.Entries.Count} block)",
                AutoSize = true,
                Checked = TemplateLibraryManager.IsFilterActive(tpl),
                Enabled = tpl.Entries.Count > 0
            };
            fileBar.Controls.Add(UiKit.Btn("Thêm file DWG...", (s, e) => AddFiles(), 130));
            fileBar.Controls.Add(UiKit.Btn("Bỏ file", (s, e) => { foreach (var x in _lstFiles.SelectedItems.Cast<object>().ToList()) _lstFiles.Items.Remove(x); }, 80));
            fileBar.Controls.Add(_chkCurrent);
            fileBar.Controls.Add(_chkSplitVis);
            fileBar.Controls.Add(_chkOnlyTemplate);
            fileBar.Controls.Add(UiKit.Btn("Thống kê", (s, e) => Run(), 120, true));
            left.Controls.Add(_lstFiles);
            left.Controls.Add(fileBar);

            _grid = UiKit.Grid();
            _grid.ReadOnly = true;
            _lblStatus = new Label { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Xuất bảng CAD", (s, e) => ExportAcad(), 110));
            bar.Controls.Add(UiKit.Btn("Xuất Excel", (s, e) => ExportExcel(), 90));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 60));
            bar.Controls.Add(_lblStatus);

            Controls.Add(_grid);
            Controls.Add(left);
            Controls.Add(UiKit.Header("Bản vẽ không cần mở: add-in đọc thẳng file DWG. Bản vẽ đang mở thì đọc cả phần chưa lưu."));
            Controls.Add(bar);
        }

        private void AddFiles()
        {
            using (var ofd = new OpenFileDialog { Filter = "Bản vẽ AutoCAD (*.dwg)|*.dwg", Multiselect = true, Title = "Chọn các bản vẽ cần thống kê" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                foreach (var f in ofd.FileNames)
                    if (!_lstFiles.Items.Contains(f) && !string.Equals(f, _doc.Name, StringComparison.OrdinalIgnoreCase)) _lstFiles.Items.Add(f);
            }
        }

        private void Run()
        {
            var files = _lstFiles.Items.Cast<string>().ToList();
            if (files.Count == 0 && !_chkCurrent.Checked) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                var s = SettingsManager.Current;
                var options = new ExtractionOptions
                {
                    MaxDepth = s.ScanDepth > 0 ? s.ScanDepth : 2,
                    SplitByVisibility = _chkSplitVis.Checked,
                    SplitAttributeKeys = new List<string>()
                };
                var tpl = TemplateLibraryManager.Load(s.CurrentTemplateSet);
                _rows = MultiDrawingCounter.Count(_doc, files, _chkCurrent.Checked, options, tpl, _chkOnlyTemplate.Checked,
                                                  !s.CountDuplicateBlocks, label => { _lblStatus.Text = "Đang đọc " + label + "..."; System.Windows.Forms.Application.DoEvents(); },
                                                  out _labels, out var errors);
                RefreshGrid();
                _lblStatus.Text = $"{_labels.Count} bản vẽ, {_rows.Count} loại thiết bị, tổng {_rows.Sum(r => r.Total)} block";
                if (errors.Count > 0) MessageBox.Show(this, "Không đọc được:\n" + string.Join("\n", errors), "Nhiều bản vẽ");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MultiDrawingDialog] Thống kê");
                MessageBox.Show(this, $"Thống kê lỗi: {ex.Message}", "Nhiều bản vẽ");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void RefreshGrid()
        {
            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên thiết bị", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Block", Width = 150 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chủng loại", Width = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Đơn vị", Width = 60 });
            foreach (var l in _labels) _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = l, Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tổng", Width = 70 });
            foreach (var r in _rows)
            {
                var v = new List<object> { r.DisplayName, r.BlockName, r.VisibilityState, r.Unit };
                foreach (var l in _labels) v.Add(r.Counts.TryGetValue(l, out int n) ? n.ToString() : "");
                v.Add(r.Total);
                _grid.Rows.Add(v.ToArray());
            }
        }

        private List<string> Headers()
        {
            var h = new List<string> { "STT", "Ký hiệu", "Tên thiết bị", "Chủng loại", "Đơn vị" };
            h.AddRange(_labels);
            h.Add("Tổng");
            return h;
        }

        private List<object[]> Rows() => _rows.Select((r, i) =>
        {
            var v = new List<object> { i + 1, "", r.DisplayName, r.VisibilityState ?? "", r.Unit };
            foreach (var l in _labels) v.Add(r.Counts.TryGetValue(l, out int n) ? (object)n : "");
            v.Add(r.Total);
            return v.ToArray();
        }).ToList();

        /// <summary>Ký hiệu: block cùng tên trong bản vẽ đang mở, không có thì lấy từ file .dwg của bộ block mẫu.</summary>
        private List<BlockItem> Symbols()
        {
            var list = new List<BlockItem>();
            var blocks = BlockReplacer.ListBlocks(_doc.Database).ToDictionary(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var r in _rows)
            {
                if (!blocks.TryGetValue(r.BlockName, out var id))
                {
                    try { id = TemplateLibraryManager.ImportBlock(_doc, SettingsManager.Current.CurrentTemplateSet, r.BlockName); }
                    catch { id = ObjectId.Null; }
                }
                list.Add(id.IsNull ? null : new BlockItem { BlockName = r.BlockName, VisibilityState = "", DynamicBtrId = id, SourceBtrId = id });
            }
            return list;
        }

        private void ExportAcad()
        {
            if (_rows.Count == 0) return;
            if (!UiKit.PickPoint(this, _doc, "\nChọn điểm chèn bảng thống kê nhiều bản vẽ: ", out var pt)) return;
            try
            {
                var cfg = new TableExportConfig { TableScale = SettingsManager.Current.TableScale };
                var headers = Headers();
                var text = Rows().Select(r => r.Select(x => x?.ToString() ?? "").ToArray()).ToList();
                List<BlockItem> symbols;
                using (_doc.LockDocument()) symbols = Symbols();
                var sum = new HashSet<int>(Enumerable.Range(5, headers.Count - 5));
                TableExporterAcad.ExportGrid(_doc, TableTemplate.Current.EffectiveTitle, headers, text, pt, cfg.ActualTextHeight, cfg.ActualRowHeight,
                                             symbols, 1, TableTemplate.Current.AddTotalRow ? sum : null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MultiDrawingDialog] Xuất bảng CAD");
                MessageBox.Show(this, $"Xuất bảng lỗi: {ex.Message}", "Nhiều bản vẽ");
            }
        }

        private void ExportExcel()
        {
            if (_rows.Count == 0) return;
            string path = UiKit.AskExcelPath(this, "Thong ke nhieu ban ve");
            if (path == null) return;
            try
            {
                var tpl = TemplateLibraryManager.Load(SettingsManager.Current.CurrentTemplateSet);
                var images = _rows.Select(r =>
                {
                    var e = TemplateLibraryManager.Match(tpl, r.BlockName, r.VisibilityState);
                    if (e == null || string.IsNullOrEmpty(e.ThumbnailBase64)) return null;
                    try { return Convert.FromBase64String(e.ThumbnailBase64); } catch { return null; }
                }).ToList();
                var headers = Headers();
                var w = new XlsxWriter();
                ExcelExporter.AddGridSheet(w, "Nhiều bản vẽ", TableTemplate.Current.EffectiveTitle, headers, Rows(),
                    headers.Select((h, i) => i == 0 ? 45.0 : i == 1 ? 80.0 : i == 2 ? 220.0 : 100.0).ToList(),
                    true, new HashSet<int>(Enumerable.Range(5, headers.Count - 5)), images, 1);
                ExcelExporter.AddGridSheet(w, "Danh sách bản vẽ", "CÁC BẢN VẼ ĐÃ THỐNG KÊ", new List<string> { "STT", "Bản vẽ" },
                    _labels.Select((l, i) => new object[] { i + 1, l }).ToList(), new List<double> { 45, 400 });
                w.Save(path);
                ExcelExporter.Open(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MultiDrawingDialog] Xuất Excel");
                MessageBox.Show(this, $"Xuất Excel lỗi: {ex.Message}", "Nhiều bản vẽ");
            }
        }
    }
}

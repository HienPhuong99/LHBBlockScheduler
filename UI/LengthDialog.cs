using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LHBBlockScheduler.Core;
using Exception = System.Exception;
using TableTemplate = LHBBlockScheduler.Core.TableTemplate;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Thống kê chiều dài ống / dây theo layer (Premium P4, lệnh LHBCHIEUDAI): quét chọn hoặc toàn bản vẽ, đổi ra mét,
    /// hao hụt %, tách theo khu vực, đặt tên thống kê theo layer (nhớ lại lần sau), xuất AutoCAD Table / Excel.
    /// </summary>
    public class LengthDialog : Form
    {
        private readonly Document _doc;
        private readonly DataGridView _grid;
        private readonly NumericUpDown _numWaste, _numUnit;
        private readonly CheckBox _chkZones;
        private readonly Label _lblUnit;
        private List<LengthRow> _rows = new List<LengthRow>();
        private List<string> _zoneNames = new List<string>();
        private List<ObjectId> _lastIds;

        public LengthDialog(Document doc)
        {
            _doc = doc;
            // v9.4: ẩn khi đổi bản vẽ, tự đóng khi bản vẽ đóng
            DocumentBinding.Bind(this, doc);
            UiKit.InitForm(this, "Chiều dài ống / dây - LHB Premium", 980, 540);
            _grid = UiKit.Grid();
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (_grid.Columns[e.ColumnIndex].Name == "colName") _grid.BeginEdit(true);
                else if (e.RowIndex < _rows.Count) ScheduleManager.ZoomAndHighlight(_doc, _rows[e.RowIndex].Ids);
            };

            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6), WrapContents = false };
            _numWaste = UiKit.Number(0, 100, (decimal)SettingsManager.Current.LengthWastePercent, 1, 70);
            _numWaste.ValueChanged += (s, e) => RefreshGrid();
            _numUnit = UiKit.Number(0, 100000, (decimal)SettingsManager.Current.MmPerDrawingUnit, 3, 90);
            _lblUnit = new Label { AutoSize = true, Padding = new Padding(4, 7, 0, 0), ForeColor = System.Drawing.Color.DimGray };
            _numUnit.ValueChanged += (s, e) =>
            {
                SettingsManager.Current.MmPerDrawingUnit = (double)_numUnit.Value;
                SettingsManager.SaveSettings();
                _lblUnit.Text = DrawingHelper.UnitLabel(_doc.Database);
                RefreshGrid();
            };
            _chkZones = new CheckBox { Text = "Tách theo khu vực", AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
            _chkZones.CheckedChanged += (s, e) => { if (_lastIds != null) Recount(_lastIds); };
            opts.Controls.AddRange(new Control[] { UiKit.Lbl("Hao hụt (%):"), _numWaste, UiKit.Lbl("  1 đơn vị bản vẽ = (mm, 0 = tự theo INSUNITS):"), _numUnit, _lblUnit, _chkZones });
            _lblUnit.Text = DrawingHelper.UnitLabel(doc.Database);

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Quét chọn...", (s, e) => Pick(), 95, true));
            bar.Controls.Add(UiKit.Btn("Toàn bản vẽ", (s, e) => Recount(LengthCounter.AllCurves(_doc.Database)), 95));
            bar.Controls.Add(UiKit.Btn("Xuất bảng CAD", (s, e) => ExportAcad(), 105));
            bar.Controls.Add(UiKit.Btn("Xuất Excel", (s, e) => ExportExcel(), 90));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 60));

            Controls.Add(_grid);
            Controls.Add(opts);
            Controls.Add(UiKit.Header("Double-click cột Tên thống kê để sửa (nhớ theo layer). Double-click cột khác để zoom tới các đường của layer."));
            Controls.Add(bar);
            FormClosing += (s, e) => SaveNames();
        }

        private void Pick()
        {
            var owner = Owner;
            Hide();
            owner?.Hide();
            List<ObjectId> ids = null;
            try
            {
                using (_doc.LockDocument())
                {
                    var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, LengthCounter.DxfFilter) });
                    var res = _doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nQuét chọn đường ống / dây cần thống kê chiều dài: " }, filter);
                    if (res.Status == PromptStatus.OK) ids = res.Value.GetObjectIds().ToList();
                }
            }
            finally
            {
                owner?.Show();
                Show();
            }
            if (ids != null) Recount(ids);
        }

        private void Recount(List<ObjectId> ids)
        {
            SaveNames();
            _lastIds = ids;
            var zones = _chkZones.Checked ? ZoneManager.Load(_doc.Database) : new List<LoadedZone>();
            if (_chkZones.Checked && zones.Count == 0)
                MessageBox.Show(this, "Bản vẽ chưa có khu vực. Gõ LHBKHUVUC để tạo khu vực trước.", "Chiều dài");
            _rows = LengthCounter.Count(_doc.Database, ids, zones);
            _zoneNames = zones.Select(z => z.Name).ToList();
            if (_rows.Any(r => r.ZoneLengths.ContainsKey(ZoneManager.OutsideName))) _zoneNames.Add(ZoneManager.OutsideName);
            RefreshGrid();
        }

        private double ToM(double drawingLength) => drawingLength * DrawingHelper.MmPerUnit(_doc.Database) / 1000.0;
        private double Waste => (double)_numWaste.Value;

        private void RefreshGrid()
        {
            SettingsManager.Current.LengthWastePercent = Waste;
            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Tên thống kê", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colLayer", HeaderText = "Layer", Width = 160, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colSeg", HeaderText = "Số đối tượng", Width = 90, ReadOnly = true });
            foreach (var z in _zoneNames) _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "z:" + z, HeaderText = z + " (m)", Width = 90, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colLen", HeaderText = "Chiều dài (m)", Width = 100, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colTotal", HeaderText = $"Tổng + hao hụt {Waste:0.#}% (m)", Width = 150, ReadOnly = true });
            foreach (var r in _rows)
            {
                var values = new List<object> { r.DisplayName, r.Layer, r.Segments };
                foreach (var z in _zoneNames) values.Add(r.ZoneLengths.TryGetValue(z, out double v) ? ToM(v).ToString("0.##") : "");
                values.Add(ToM(r.DrawingLength).ToString("0.##"));
                values.Add((ToM(r.DrawingLength) * (1 + Waste / 100)).ToString("0.##"));
                _grid.Rows.Add(values.ToArray());
            }
        }

        private void SaveNames()
        {
            _grid.EndEdit();
            if (_grid.Columns.Count == 0) return;
            var dict = SettingsManager.Current.LayerDisplayNames;
            for (int i = 0; i < _rows.Count && i < _grid.Rows.Count; i++)
            {
                string name = (_grid.Rows[i].Cells["colName"].Value?.ToString() ?? "").Trim();
                if (name.Length == 0) continue;
                _rows[i].DisplayName = name;
                if (name != _rows[i].Layer) dict[_rows[i].Layer] = name; else dict.Remove(_rows[i].Layer);
            }
            SettingsManager.SaveSettings();
        }

        private (List<string> Headers, List<object[]> Rows, HashSet<int> Sum) BuildTable()
        {
            SaveNames();
            var headers = new List<string> { "STT", "Tên thống kê", "Layer" };
            headers.AddRange(_zoneNames.Select(z => z + " (m)"));
            headers.Add("Chiều dài (m)");
            headers.Add($"Tổng + hao hụt {Waste:0.#}% (m)");
            var rows = new List<object[]>();
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                var v = new List<object> { i + 1, r.DisplayName, r.Layer };
                foreach (var z in _zoneNames) v.Add(r.ZoneLengths.TryGetValue(z, out double d) ? Math.Round(ToM(d), 2) : (object)"");
                v.Add(Math.Round(ToM(r.DrawingLength), 2));
                v.Add(Math.Round(ToM(r.DrawingLength) * (1 + Waste / 100), 2));
                rows.Add(v.ToArray());
            }
            var sum = new HashSet<int>(Enumerable.Range(3, headers.Count - 3));
            return (headers, rows, sum);
        }

        private void ExportAcad()
        {
            if (_rows.Count == 0) return;
            var (headers, rows, sum) = BuildTable();
            // v9.4: bảng vào không gian đang làm việc; ở Layout thì hỏi dùng tỉ lệ 1
            double? scale = UiKit.ScaleForCurrentSpace(this, _doc, SettingsManager.Current.TableScale);
            if (scale == null) return;
            if (!UiKit.PickPoint(this, _doc, "\nChọn điểm chèn bảng chiều dài: ", out var pt)) return;
            try
            {
                var cfg = new TableExportConfig { TableScale = scale.Value };
                var text = rows.Select(r => r.Select(x => x is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : x?.ToString() ?? "").ToArray()).ToList();
                TableExporterAcad.ExportGrid(_doc, "BẢNG THỐNG KÊ CHIỀU DÀI ỐNG / DÂY", headers, text, pt, cfg.ActualTextHeight, cfg.ActualRowHeight,
                                             sumCols: TableTemplate.Current.AddTotalRow ? sum : null,
                                             decimals: headers.Select(_ => 2).ToArray());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LengthDialog] Xuất bảng CAD");
                MessageBox.Show(this, $"Xuất bảng lỗi: {ex.Message}", "Chiều dài");
            }
        }

        private void ExportExcel()
        {
            if (_rows.Count == 0) return;
            string path = UiKit.AskExcelPath(this, "Chieu dai ong day");
            if (path == null) return;
            try
            {
                var (headers, rows, sum) = BuildTable();
                var w = new XlsxWriter();
                ExcelExporter.AddGridSheet(w, "Chiều dài", "BẢNG THỐNG KÊ CHIỀU DÀI ỐNG / DÂY", headers, rows,
                    headers.Select((h, i) => i == 0 ? 45.0 : i == 1 ? 220.0 : 130.0).ToList(), true, sum);
                w.Save(path);
                ExcelExporter.Open(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LengthDialog] Xuất Excel");
                MessageBox.Show(this, $"Xuất Excel lỗi: {ex.Message}", "Chiều dài");
            }
        }
    }
}

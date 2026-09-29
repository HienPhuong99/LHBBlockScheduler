using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LHBBlockScheduler.Core;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Khu vực / tầng (Premium P1, lệnh LHBKHUVUC hoặc Premium ▾ > Tầng / khu vực trên form): chọn các đường bao kín
    /// trên bản vẽ, đặt tên (tự lấy chữ to nhất nằm trong đường bao), sắp thứ tự cột. Lưu vào bản vẽ.
    /// </summary>
    public class ZoneDialog : Form
    {
        private readonly Document _doc;
        private readonly DataGridView _grid;
        private readonly CheckBox _chkShow;
        private List<ZoneDef> _zones;

        /// <summary>Đã lưu khu vực -> form thống kê tính lại cột SL theo khu vực.</summary>
        public event Action Saved;

        public ZoneDialog(Document doc)
        {
            _doc = doc;
            UiKit.InitForm(this, "Tầng / khu vực - LHB Premium", 640, 460);
            _grid = UiKit.Grid();
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Tên khu vực (tên cột trong bảng)", Width = 260 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colArea", HeaderText = "Diện tích (m²)", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHandle", HeaderText = "Handle đường bao", Width = 120, ReadOnly = true });
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0) ZoomTo(e.RowIndex); };

            _chkShow = new CheckBox
            {
                Text = "Hiện cột SL theo khu vực trên bảng thống kê",
                AutoSize = true,
                Checked = !SettingsManager.Current.HideZoneColumns,
                Padding = new Padding(6, 6, 0, 0)
            };

            var bar = UiKit.BottomBar();
            bar.Controls.Add(UiKit.Btn("Chọn đường bao...", (s, e) => AddFromDrawing(), 125));
            bar.Controls.Add(UiKit.Btn("Xoá dòng", (s, e) => DeleteRows(), 80));
            bar.Controls.Add(UiKit.Btn("▲", (s, e) => MoveRow(-1), 35));
            bar.Controls.Add(UiKit.Btn("▼", (s, e) => MoveRow(1), 35));
            bar.Controls.Add(UiKit.Btn("Zoom", (s, e) => { if (_grid.CurrentRow != null) ZoomTo(_grid.CurrentRow.Index); }, 60));
            bar.Controls.Add(UiKit.Btn("Lưu & áp dụng", (s, e) => SaveZones(), 110, true));
            bar.Controls.Add(UiKit.Btn("Đóng", (s, e) => Close(), 70));

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ForeColor = System.Drawing.Color.DimGray,
                Padding = new Padding(8, 4, 8, 0),
                Text = "Đường bao = polyline / circle kín vẽ quanh từng tầng hoặc khu vực. Block tính vào khu vực chứa điểm chèn; " +
                       "khu lồng nhau thì tính vào khu nhỏ nhất. Double-click dòng để zoom."
            };
            Controls.Add(_grid);
            Controls.Add(UiKit.Header("Mỗi khu vực thành 1 cột SL trong bảng thống kê, bảng xuất và Excel"));
            Controls.Add(_chkShow);
            Controls.Add(note);
            Controls.Add(bar);
            _chkShow.Dock = DockStyle.Bottom;

            _zones = ZoneManager.LoadDefs(doc.Database);
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            _grid.Rows.Clear();
            var loaded = ZoneManager.Load(_doc.Database, _zones);
            double mm = DrawingHelper.MmPerUnit(_doc.Database);
            foreach (var z in _zones)
            {
                var lz = loaded.FirstOrDefault(l => l.Handle == z.Handle);
                string area = lz == null ? "(mất đường bao)" : (lz.Area * mm * mm / 1e6).ToString("0.##");
                _grid.Rows.Add(z.Name, area, z.Handle);
            }
        }

        private void ReadNames()
        {
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count && i < _zones.Count; i++)
            {
                string name = (_grid.Rows[i].Cells["colName"].Value?.ToString() ?? "").Trim();
                if (name.Length > 0) _zones[i].Name = name;
            }
        }

        private void AddFromDrawing()
        {
            ReadNames();
            var owner = Owner;
            Hide();
            owner?.Hide();
            int added = 0;
            try
            {
                using (_doc.LockDocument())
                {
                    var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE") });
                    var res = _doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nChọn các đường bao kín của tầng / khu vực: " }, filter);
                    if (res.Status != PromptStatus.OK) return;
                    using (var tr = _doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var id in res.Value.GetObjectIds())
                        {
                            var c = (Curve)tr.GetObject(id, OpenMode.ForRead);
                            bool closed = c.Closed || c.StartPoint.DistanceTo(c.EndPoint) < 1e-6;
                            string h = DrawingHelper.HandleString(id);
                            if (!closed || _zones.Any(z => z.Handle == h)) continue;
                            var poly = DrawingHelper.CurveToPolygon(c);
                            if (poly == null || poly.Count < 3) continue;
                            string name = ZoneManager.SuggestName(_doc.Database, poly) ?? $"Khu {_zones.Count + 1}";
                            if (_zones.Any(z => string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase))) name += $" ({_zones.Count + 1})";
                            _zones.Add(new ZoneDef { Name = name, Handle = h });
                            added++;
                        }
                        tr.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ZoneDialog] Chọn đường bao");
            }
            finally
            {
                owner?.Show();
                Show();
                RefreshGrid();
                _doc.Editor.WriteMessage($"\n[LHB] Thêm {added} khu vực. Sửa tên nếu cần rồi bấm 'Lưu & áp dụng'.\n");
            }
        }

        private void DeleteRows()
        {
            ReadNames();
            var idx = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).OrderByDescending(i => i).ToList();
            foreach (int i in idx) if (i < _zones.Count) _zones.RemoveAt(i);
            RefreshGrid();
        }

        private void MoveRow(int dir)
        {
            ReadNames();
            if (_grid.CurrentRow == null) return;
            int i = _grid.CurrentRow.Index, j = i + dir;
            if (j < 0 || j >= _zones.Count) return;
            (_zones[i], _zones[j]) = (_zones[j], _zones[i]);
            RefreshGrid();
            _grid.ClearSelection();
            _grid.Rows[j].Selected = true;
            _grid.CurrentCell = _grid.Rows[j].Cells[0];
        }

        private void ZoomTo(int row)
        {
            if (row < 0 || row >= _zones.Count) return;
            var z = ZoneManager.Load(_doc.Database, new List<ZoneDef> { _zones[row] }).FirstOrDefault();
            if (z == null) return;
            ScheduleManager.ZoomToExtents(_doc, new Autodesk.AutoCAD.DatabaseServices.Extents3d(
                new Autodesk.AutoCAD.Geometry.Point3d(z.Box.MinPoint.X, z.Box.MinPoint.Y, 0),
                new Autodesk.AutoCAD.Geometry.Point3d(z.Box.MaxPoint.X, z.Box.MaxPoint.Y, 0)));
            ScheduleManager.ZoomAndHighlight(_doc, new List<ObjectId> { z.Id });
        }

        private void SaveZones()
        {
            ReadNames();
            var dup = _zones.GroupBy(z => z.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                MessageBox.Show(this, $"Tên khu vực '{dup.Key}' bị trùng. Mỗi khu vực cần 1 tên khác nhau.", "Khu vực");
                return;
            }
            try
            {
                ZoneManager.SaveDefs(_doc, _zones);
                SettingsManager.Current.HideZoneColumns = !_chkShow.Checked;
                SettingsManager.SaveSettings();
                Saved?.Invoke();
                MessageBox.Show(this, $"Đã lưu {_zones.Count} khu vực vào bản vẽ.", "Khu vực", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ZoneDialog] Lưu khu vực");
                MessageBox.Show(this, $"Lưu khu vực lỗi: {ex.Message}", "Khu vực");
            }
        }
    }
}

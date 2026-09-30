using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>Phần Premium (v9) của form thống kê: menu Premium, cột khu vực / thuộc tính, Excel, thông tin tự cập nhật.</summary>
    public partial class BlockScheduleForm
    {
        private ContextMenuStrip _premiumMenu;
        private List<string> _premiumColumnKeys = new List<string>();
        private ZoneDialog _zoneDialog;
        private CheckDialog _checkDialog;
        private LengthDialog _lengthDialog;
        private MultiDrawingDialog _multiDialog;

        private void BuildPremiumMenu()
        {
            _premiumMenu = new ContextMenuStrip();
            void Item(string text, Action a) => _premiumMenu.Items.Add(text, null, (s, e) =>
            {
                try { a(); }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[Premium] " + text);
                    MessageBox.Show(this, $"{text}: lỗi {ex.Message}\nXem log: {Logger.GetLogFilePath()}", "LHB Premium");
                }
            });
            Item("Tầng / khu vực...", Premium_Zones);
            Item("Cột thuộc tính...", Premium_Attributes);
            Item("Soát lỗi đếm...", Premium_Check);
            Item("Đánh số thiết bị...", Premium_Numbering);
            Item("Vùng bảo vệ PCCC...", Premium_Coverage);
            Item("Thay block (dòng đang chọn)...", Premium_Replace);
            _premiumMenu.Items.Add(new ToolStripSeparator());
            Item("Xuất Excel...", Premium_ExportExcel);
            Item("Mẫu bảng xuất...", () => { if (UiKit.Premium("Mẫu bảng xuất")) using (var d = new TableTemplateDialog()) d.ShowDialog(this); });
            Item("Cập nhật bảng đã xuất (LHBCAPNHAT)", () => { if (UiKit.Premium("Cập nhật bảng") && EnsureDocActive()) _doc.SendStringToExecute("LHBCAPNHAT ", true, false, true); });
            Item("Chiều dài ống / dây...", () =>
            {
                if (!UiKit.Premium("Chiều dài ống / dây")) return;
                if (_lengthDialog == null || _lengthDialog.IsDisposed) { _lengthDialog = new LengthDialog(_doc); _lengthDialog.Show(this); }
                else _lengthDialog.Activate();
            });
            Item("Thống kê nhiều bản vẽ...", () =>
            {
                if (!UiKit.Premium("Nhiều bản vẽ")) return;
                if (_multiDialog == null || _multiDialog.IsDisposed) { _multiDialog = new MultiDrawingDialog(_doc); _multiDialog.Show(this); }
                else _multiDialog.Activate();
            });
            _premiumMenu.Items.Add(new ToolStripSeparator());
            Item("Bản quyền...", () => { using (var d = new LicenseDialog()) d.ShowDialog(this); });
            _premiumMenu.Items.Add(new ToolStripLabel(LicenseManager.StatusText) { ForeColor = System.Drawing.Color.DimGray });
        }

        /// <summary>Dòng đang chọn trên grid, không chọn dòng nào = mọi dòng.</summary>
        private List<BlockItem> SelectedOrAll()
        {
            var sel = GetSelectedItems();
            return sel.Count > 0 ? sel : _allItems.ToList();
        }

        /// <summary>
        /// Tính lại và đồng bộ cột Premium trên grid: SL theo khu vực (đứng trước cột SL) và cột thuộc tính (sau cột
        /// Chủng loại). Gọi sau mỗi lần tính trùng (quét lại, gộp, quét thêm, đổi "Không đếm trùng").
        /// </summary>
        internal void RefreshPremiumColumns()
        {
            if (_grid == null || _doc == null) return;
            var keys = new List<(string Key, string Header, bool IsZone)>();
            try
            {
                var zones = SettingsManager.Current.HideZoneColumns ? new List<LoadedZone>() : ZoneManager.Load(_doc.Database);
                foreach (var k in ZoneManager.ComputeCounts(_allItems, zones))
                    keys.Add((k, k.Substring(ZoneManager.ColumnPrefix.Length), true));

                var present = new HashSet<string>(PremiumColumns.CollectAttributeKeys(_allItems), StringComparer.OrdinalIgnoreCase);
                var shown = (SettingsManager.Current.AttributeColumns ?? new List<string>()).Where(present.Contains).ToList();
                PremiumColumns.ComputeAttributeValues(_allItems, shown);
                foreach (var k in shown) keys.Add((PremiumColumns.AttrPrefix + k, BlockExtractor.AttributeKeyLabel(k), false));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BlockScheduleForm] Tính cột Premium");
            }

            foreach (var old in _premiumColumnKeys.Where(k => !keys.Any(x => x.Key == k)).ToList())
                if (_grid.Columns.Contains(old)) _grid.Columns.Remove(old);

            var headers = SettingsManager.Current.ColumnHeaders ?? new Dictionary<string, string>();
            var vis = SettingsManager.Current.ColumnVisibility ?? new Dictionary<string, bool>();
            int zoneAt = _grid.Columns["colCount"].Index;
            int attrAt = _grid.Columns["colVisibility"].Index + 1;
            foreach (var (key, header, isZone) in keys)
            {
                if (_grid.Columns.Contains(key))
                {
                    if (isZone) zoneAt = Math.Max(zoneAt, _grid.Columns[key].Index + 1);
                    continue;
                }
                var col = new DataGridViewTextBoxColumn
                {
                    Name = key,
                    HeaderText = headers.TryGetValue(key, out var h) && !string.IsNullOrWhiteSpace(h) ? h : header,
                    ReadOnly = true,
                    Width = isZone ? 75 : 110,
                    SortMode = DataGridViewColumnSortMode.Programmatic
                };
                if (vis.TryGetValue(key, out bool v)) col.Visible = v;
                if (isZone) { _grid.Columns.Insert(Math.Min(zoneAt, _grid.Columns.Count), col); zoneAt = col.Index + 1; }
                else { _grid.Columns.Insert(Math.Min(attrAt, _grid.Columns.Count), col); attrAt = col.Index + 1; }
            }
            if (keys.Count > 0 || _premiumColumnKeys.Count > 0)
                Logger.Log($"[BlockScheduleForm] Cột Premium: [{string.Join(", ", keys.Select(k => k.Key))}]");
            _premiumColumnKeys = keys.Select(k => k.Key).ToList();
            _grid.Invalidate();
        }

        private TableScanInfo BuildScanInfo()
        {
            var o = CurrentOptions();
            // v9.4: vùng chọn của riêng form này (không còn biến static dùng chung mọi bản vẽ)
            var roots = _selectedIds.Where(id => !id.IsNull && id.IsValid && !id.IsErased).ToList();
            const int maxRoots = 20000;
            var info = new TableScanInfo
            {
                Version = TableUpdater.ScanInfoVersion,
                MaxDepth = o.MaxDepth,
                CountParentBlocks = o.CountParentBlocks,
                SplitByVisibility = o.SplitByVisibility,
                SplitByLayer = o.SplitByLayer,
                SplitBySize = o.SplitBySize,
                CountXrefBlocks = o.CountXrefBlocks,
                SplitAttributeKeys = o.SplitAttributeKeys,
                TemplateSet = CurrentTemplate().Name,
                OnlyTemplate = _chkOnlyTemplate.Checked,
                IncludeDuplicates = !_chkExcludeDup.Checked,
                UseZones = _premiumColumnKeys.Any(k => k.StartsWith(ZoneManager.ColumnPrefix)),
                RootHandles = roots.Take(maxRoots).Select(DrawingHelper.HandleString).ToList(),
                RootsTruncated = roots.Count > maxRoots,
                // Block tạo sau thời điểm này có handle >= Handseed -> LHBCAPNHAT chỉ thêm block MỚI trong khung vùng quét
                HandseedAtScan = _doc.Database.Handseed.Value.ToString("X")
            };
            TableUpdater.FillScanBox(_doc.Database, roots, info);
            Logger.Log($"[BlockScheduleForm] Thông tin tự cập nhật: {roots.Count} đối tượng gốc{(info.RootsTruncated ? $" (lưu {maxRoots} đầu)" : "")}, " +
                       $"Handseed {info.HandseedAtScan}, [{o}]");
            return info;
        }

        // ============================== HÀNH ĐỘNG MENU PREMIUM ==============================

        private void Premium_Zones()
        {
            if (!UiKit.Premium("Tầng / khu vực")) return;
            if (_zoneDialog == null || _zoneDialog.IsDisposed)
            {
                _zoneDialog = new ZoneDialog(_doc);
                _zoneDialog.Saved += () =>
                {
                    RefreshPremiumColumns();
                    _bindingSource.ResetBindings(false);
                };
                _zoneDialog.Show(this);
            }
            else _zoneDialog.Activate();
        }

        private void Premium_Attributes()
        {
            if (!UiKit.Premium("Cột thuộc tính")) return;
            var keys = PremiumColumns.CollectAttributeKeys(_allItems);
            var oldSplit = (SettingsManager.Current.SplitAttributeKeys ?? new List<string>()).ToList();
            using (var d = new AttributeColumnsDialog(keys))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                bool splitChanged = !oldSplit.OrderBy(x => x).SequenceEqual(d.SplitKeys.OrderBy(x => x));
                if (splitChanged) TriggerReExtraction();
                else RefreshPremiumColumns();
            }
        }

        private void Premium_Check()
        {
            if (!UiKit.Premium("Soát lỗi đếm")) return;
            if (_checkDialog == null || _checkDialog.IsDisposed)
            {
                _checkDialog = new CheckDialog(_doc, () => _allItems.ToList());
                _checkDialog.Show(this);
            }
            else _checkDialog.Activate();
        }

        private void Premium_Numbering()
        {
            if (!UiKit.Premium("Đánh số thiết bị") || !EnsureDocActive()) return;
            using (var d = new NumberingDialog(_doc, SelectedOrAll())) d.ShowDialog(this);
        }

        private void Premium_Coverage()
        {
            if (!UiKit.Premium("Vùng bảo vệ PCCC") || !EnsureDocActive()) return;
            using (var d = new CoverageDialog(_doc, SelectedOrAll())) d.ShowDialog(this);
        }

        private void Premium_Replace()
        {
            if (!UiKit.Premium("Thay block") || !EnsureDocActive()) return;
            var sel = GetSelectedItems();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Chọn dòng thiết bị cần thay block trên bảng trước.", "Thay block");
                return;
            }
            using (var d = new ReplaceBlockDialog(_doc, sel))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null || d.Result.Replaced == 0) return;
                // Block cũ đã xoá, block mới là đối tượng gốc mới -> quét lại (vùng chọn của riêng form này)
                var oldIds = new HashSet<Autodesk.AutoCAD.DatabaseServices.ObjectId>(d.Result.OldIds);
                _selectedIds.RemoveAll(oldIds.Contains);
                _selectedSet.ExceptWith(oldIds);
                foreach (var id in d.Result.NewIds)
                    if (_selectedSet.Add(id)) _selectedIds.Add(id);
                TriggerReExtraction();
            }
        }

        private void Premium_ExportExcel()
        {
            if (!UiKit.Premium("Xuất Excel")) return;
            _grid.EndEdit();
            string name = System.IO.Path.GetFileNameWithoutExtension(_doc.Name);
            string path = UiKit.AskExcelPath(this, "Thong ke block - " + name);
            if (path == null) return;
            try
            {
                var cols = _grid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.Visible && c.Name != "colStatus" && c.Name != "colSource" && c.Name != "colDup")
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => new ExcelExporter.Col { Key = c.Name, Header = c.HeaderText, WidthPx = c.Width }).ToList();
                var w = new XlsxWriter();
                int rows = ExcelExporter.AddItemsSheet(w, "Thống kê", cols, _allItems.ToList());
                w.Save(path);
                _doc.Editor.WriteMessage($"\n[LHB] Đã xuất Excel {rows} dòng: {path}\n");
                ExcelExporter.Open(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BlockScheduleForm] Xuất Excel");
                MessageBox.Show(this, $"Xuất Excel lỗi: {ex.Message}\nFile đang mở trong Excel thì đóng lại rồi xuất lại.", "Xuất Excel");
            }
        }
    }
}

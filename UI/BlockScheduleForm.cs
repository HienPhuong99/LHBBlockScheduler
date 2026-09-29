using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using Image = System.Drawing.Image;

namespace LHBBlockScheduler.UI
{
    public partial class BlockScheduleForm : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private static readonly Bitmap _greenDot = CreateCircleBitmap(Color.FromArgb(46, 204, 113));
        private static readonly Bitmap _redDot = CreateCircleBitmap(Color.FromArgb(231, 76, 60));
        private static readonly Color QuickBlockBack = Color.FromArgb(255, 250, 205);
        private static readonly Color DuplicateRed = Color.FromArgb(192, 57, 43);

        private readonly Document _doc;
        private readonly BindingList<BlockItem> _allItems;
        private readonly BindingSource _bindingSource;

        private DataGridView _grid;
        private TextBox _txtSearch;
        private ComboBox _cboScanDepth;
        private CheckBox _chkCountParents, _chkSplitVisibility, _chkSplitLayer, _chkExcludeDup;
        private DuplicateDialog _dupDialog;
        private ComboBox _cboTemplateSet;
        private CheckBox _chkOnlyTemplate;
        private TemplateLibraryDialog _tplDialog;
        private NumericUpDown _numScale;
        private ComboBox _cboTableKind;
        private const string TableKindAcad = "AutoCAD Table";
        private const string TableKindLines = "Line + Text (cũ)";
        private Button _btnImageMenu;

        private ContextMenuStrip _imageContextMenu;
        private ContextMenuStrip _headerContextMenu;
        private readonly ToolTip _tips = new ToolTip();

        private bool _hasWarnedCountParents = false;
        private bool _isUpdatingUi = false;
        private System.Windows.Forms.Timer _headerSortTimer;
        private System.Windows.Forms.Timer _searchTimer;
        private string _pendingSortColumn;
        private string _sortedColumn = null;
        private bool _sortAscending = true;

        // Ảnh ký hiệu đã đọc từ file PNG, theo đường dẫn: vẽ lưới / sắp xếp / gộp không đọc lại đĩa (v9.1).
        // Ảnh bị thay (render lại) chuyển sang _retiredThumbs, chỉ Dispose khi đóng form (ô lưới có thể đang vẽ ảnh cũ).
        private readonly Dictionary<string, Image> _thumbCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Image> _retiredThumbs = new List<Image>();

        public BlockScheduleForm(List<BlockItem> items, Document doc)
        {
            _doc = doc;
            _allItems = new BindingList<BlockItem>(items);
            _bindingSource = new BindingSource { DataSource = _allItems };

            BuildUi();
            RecomputeDuplicates();
            LoadThumbnailsAsync();
            ApplyColumnVisibilityFromSettings();
            ApplyColumnHeadersFromSettings();
            FormClosed += (s, e) => ReleaseResources();

            Logger.Log($"BlockScheduleForm: mở form với {items.Count} dòng");
        }

        private static Bitmap CreateCircleBitmap(Color color)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(color))
                {
                    g.FillEllipse(b, 2, 2, 12, 12);
                }
            }
            return bmp;
        }

        private void ReleaseResources()
        {
            _searchTimer?.Dispose();
            _headerSortTimer?.Dispose();
            _tips.Dispose();
            foreach (var img in _thumbCache.Values.Concat(_retiredThumbs)) img?.Dispose();
            _thumbCache.Clear();
            _retiredThumbs.Clear();
        }

        // ============================== UI LAYOUT ==============================

        private void BuildUi()
        {
            Text = "LHB Block Scheduler - Thống kê Block v9.2 Premium";
            Width = 1280;
            Height = 710;
            StartPosition = FormStartPosition.CenterScreen;

            // TOP CONTAINER (Toolbar 2 hàng). v9.1 bỏ hàng thư viện thiết bị cũ (Quy hoạch / Thêm vào TV): chỉ còn
            // thư viện block mẫu -> lưới cao thêm 1 hàng.
            var topContainer = new Panel { Dock = DockStyle.Top, Height = 74 };

            // Hàng 1: Tìm kiếm + Bộ block mẫu + Quét thêm + Premium
            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(6, 4, 6, 2),
                WrapContents = false
            };

            _txtSearch = new TextBox { Width = 220, Height = 26 };
            _txtSearch.HandleCreated += (s, e) => SendMessage(_txtSearch.Handle, EM_SETCUEBANNER, IntPtr.Zero, "Tìm kiếm (gõ không dấu)...");
            // Gõ liên tục chỉ lọc 1 lần sau khi ngừng gõ 0.25 giây
            _searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _searchTimer.Tick += (s, e) =>
            {
                _searchTimer.Stop();
                ApplyFilter();
            };
            _txtSearch.TextChanged += (s, e) =>
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            };

            var lblTpl = new Label { Text = " Bộ block mẫu:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            _cboTemplateSet = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            RefreshTemplateSetList(SettingsManager.Current.CurrentTemplateSet);
            _cboTemplateSet.SelectionChangeCommitted += (s, e) =>
            {
                SettingsManager.Current.CurrentTemplateSet = _cboTemplateSet.SelectedItem?.ToString();
                SettingsManager.SaveSettings();
                UpdateTemplateCheckText();
                TriggerReExtraction();
            };
            var btnTemplate = MakeButton("Block mẫu...", (s, e) => OpenTemplateDialog(), 95);
            _chkOnlyTemplate = new CheckBox
            {
                Text = "Chỉ quét block mẫu",
                AutoSize = true,
                Padding = new Padding(8, 4, 0, 0),
                Checked = !SettingsManager.Current.ScanAllBlocks
            };
            _chkOnlyTemplate.CheckedChanged += (s, e) =>
            {
                SettingsManager.Current.ScanAllBlocks = !_chkOnlyTemplate.Checked;
                SettingsManager.SaveSettings();
                Logger.Log($"[BlockScheduleForm] Chỉ quét block mẫu = {_chkOnlyTemplate.Checked}");
                TriggerReExtraction();
            };
            UpdateTemplateCheckText();
            var btnScanMore = MakeButton("Quét thêm", Btn_ScanMore_Click, 90);
            _tips.SetToolTip(btnScanMore, "Chọn thêm vùng trên bản vẽ, cộng dồn vào bảng đang có (giữ tên, đơn vị đã sửa). Vùng đã chọn trước không đếm lại.");
            // Menu tính năng Premium (v9)
            var btnPremium = MakeButton("Premium ▾", (s, e) => _premiumMenu.Show((Control)s, 0, ((Control)s).Height), 95);
            btnPremium.BackColor = UiKit.Gold;
            btnPremium.ForeColor = Color.White;
            BuildPremiumMenu();
            row1.Controls.AddRange(new Control[] { _txtSearch, lblTpl, _cboTemplateSet, btnTemplate, _chkOnlyTemplate, btnScanMore, btnPremium });

            // Hàng 2: Tuỳ chọn quét block (Độ sâu, Block cha, Visibility, Layer), block trùng, căn lề
            var row2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 36,
                Padding = new Padding(6, 2, 6, 4),
                WrapContents = false
            };

            var lblDepth = new Label { Text = "Độ sâu quét:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            _cboScanDepth = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboScanDepth.Items.AddRange(new object[] { "1 (chỉ tầng ngoài)", "2", "3", "Không giới hạn" });
            _cboScanDepth.SelectedIndex = 1; // Mặc định 2
            _cboScanDepth.SelectedIndexChanged += (s, e) => TriggerReExtraction();

            _chkCountParents = new CheckBox
            {
                Text = "Đếm cả block cha",
                AutoSize = true,
                Padding = new Padding(8, 4, 0, 0),
                Checked = false
            };
            _chkCountParents.CheckedChanged += (s, e) =>
            {
                if (_chkCountParents.Checked && !_hasWarnedCountParents)
                {
                    _hasWarnedCountParents = true;
                    MessageBox.Show("Bật 'Đếm cả block cha' sẽ tính cả cụm block phòng/tổ hợp lẫn các block thiết bị con bên trong, có thể dẫn đến trùng lặp số lượng vật tư!",
                        "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                TriggerReExtraction();
            };

            _chkSplitVisibility = new CheckBox
            {
                Text = "Tách theo chủng loại",
                AutoSize = true,
                Padding = new Padding(8, 4, 0, 0),
                Checked = true
            };
            _chkSplitVisibility.CheckedChanged += (s, e) => TriggerReExtraction();

            _chkSplitLayer = new CheckBox
            {
                Text = "Tách theo layer",
                AutoSize = true,
                Padding = new Padding(8, 4, 0, 0),
                Checked = false
            };
            _chkSplitLayer.CheckedChanged += (s, e) => TriggerReExtraction();

            var btnNameFromVis = MakeButton("Lấy tên từ chủng loại", Btn_NameFromVis_Click, 140);

            // Block trùng vị trí (copy đè): mặc định KHÔNG đếm vào SL
            var btnFindDup = MakeButton("Tìm trùng", Btn_FindDup_Click, 75);
            _chkExcludeDup = new CheckBox
            {
                Text = "Không đếm trùng",
                AutoSize = true,
                Padding = new Padding(6, 4, 0, 0),
                Checked = !SettingsManager.Current.CountDuplicateBlocks
            };
            _chkExcludeDup.CheckedChanged += (s, e) =>
            {
                SettingsManager.Current.CountDuplicateBlocks = !_chkExcludeDup.Checked;
                SettingsManager.SaveSettings();
                DuplicateFinder.SetExclusion(_allItems, _chkExcludeDup.Checked);
                RefreshPremiumColumns();
                _bindingSource.ResetBindings(false);
                Logger.Log($"[BlockScheduleForm] Không đếm block trùng = {_chkExcludeDup.Checked}");
            };

            // Căn lề như Excel: quét chọn ô trên grid rồi bấm nút, bảng xuất CAD dùng đúng căn lề này
            var lblAlign = new Label { Text = " Căn lề:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            var btnAlignLeft = MakeButton("Trái", (s, e) => ApplyAlignmentToSelectedCells(CellHAlign.Left), 50);
            var btnAlignCenter = MakeButton("Giữa", (s, e) => ApplyAlignmentToSelectedCells(CellHAlign.Center), 50);
            var btnAlignRight = MakeButton("Phải", (s, e) => ApplyAlignmentToSelectedCells(CellHAlign.Right), 50);

            row2.Controls.AddRange(new Control[] { lblDepth, _cboScanDepth, _chkCountParents, _chkSplitVisibility, _chkSplitLayer, btnNameFromVis,
                                                   btnFindDup, _chkExcludeDup, lblAlign, btnAlignLeft, btnAlignCenter, btnAlignRight });

            // Dock Fill phải thêm TRƯỚC (được xếp sau cùng, lấy phần còn lại dưới hàng Top)
            topContainer.Controls.Add(row2);
            topContainer.Controls.Add(row1);

            // GRID
            _grid = UiKit.DoubleBuffer(new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                DataSource = _bindingSource,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                // Chọn theo ô (quét chọn như Excel để căn lề). RowHeaderSelect thay vì CellSelect để code
                // vẫn chọn được cả dòng bằng row.Selected = true (row header đang ẩn nên user chỉ chọn ô)
                SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
                MultiSelect = true,
                RowHeadersVisible = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                RowTemplate = { Height = 48 }
            });
            BuildGridColumns();
            BuildContextMenuStrips();

            // Sửa trực tiếp trên grid: double-click ô để sửa, Delete xoá dòng, Insert thêm dòng, F2 sửa ô đang chọn
            _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            _grid.KeyDown += Grid_KeyDown;
            _grid.DataError += Grid_DataError;
            _grid.ColumnHeaderMouseDoubleClick += Grid_ColumnHeaderMouseDoubleClick;
            // Click header = sắp xếp, nhưng hoãn lại 1 nhịp double-click để double-click (đổi tên cột) không bị sắp xếp nhầm
            _headerSortTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
            _headerSortTimer.Tick += (s, e) =>
            {
                _headerSortTimer.Stop();
                if (_pendingSortColumn != null) SortByColumn(_pendingSortColumn);
                _pendingSortColumn = null;
            };

            _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
            _grid.CellMouseUp += Grid_CellMouseUp;

            // Nạp lại danh sách (quét lại, sắp xếp...) làm mọi dòng hiện lại -> lọc lại theo ô tìm kiếm
            _bindingSource.ListChanged += (s, e) =>
            {
                if (e.ListChangedType == ListChangedType.Reset && IsHandleCreated && _txtSearch.TextLength > 0)
                    BeginInvoke((Action)ApplyFilter);
            };

            // BOTTOM PANEL
            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                Padding = new Padding(6),
                WrapContents = false
            };

            var btnMerge = MakeButton("Gộp nhóm", Btn_Merge_Click, 80);
            var btnSuggestMerge = MakeButton("Gợi ý gộp", Btn_SuggestMerge_Click, 90);
            _btnImageMenu = MakeButton("Ảnh ▾", (s, e) => _imageContextMenu.Show(_btnImageMenu, 0, _btnImageMenu.Height), 70);
            var btnHighlight = MakeButton("Highlight", Btn_Highlight_Click, 75);
            var btnUnHighlight = MakeButton("Tắt HL", Btn_Unhighlight_Click, 65);
            var btnUp = MakeButton("Lên", (s, e) => MoveSelected(-1), 55);
            var btnDown = MakeButton("Xuống", (s, e) => MoveSelected(1), 65);
            var btnDelete = MakeButton("Xoá", Btn_Delete_Click, 60);
            var btnAddRow = MakeButton("Thêm dòng", (s, e) => AddManualRow(), 80);

            var lblScale = new Label { Text = " Tỉ lệ:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
            double initialScale = _doc?.Database.Dimscale > 0 ? _doc.Database.Dimscale : 100.0;
            _numScale = new NumericUpDown
            {
                Width = 70,
                Minimum = 1,
                Maximum = 10000,
                Value = (decimal)Math.Max(1, Math.Min(10000, initialScale))
            };
            var btnMeasureScale = MakeButton("Đo từ BV", Btn_MeasureScale_Click, 80);
            var btnExport = MakeButton("Xuất bảng", Btn_Export_Click, 95);
            var btnExcel = MakeButton("Xuất Excel", (s, e) => Premium_ExportExcel(), 85);

            // Kiểu bảng xuất: mặc định AutoCAD Table (giống bảng mẫu người dùng), giữ Line + Text cũ để dự phòng
            _cboTableKind = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboTableKind.Items.AddRange(new object[] { TableKindAcad, TableKindLines });
            _cboTableKind.SelectedItem = SettingsManager.Current.TableKind == TableKindLines ? TableKindLines : TableKindAcad;

            bottomPanel.Controls.AddRange(new Control[]
            {
                btnMerge, btnSuggestMerge, _btnImageMenu, btnHighlight, btnUnHighlight,
                btnUp, btnDown, btnAddRow, btnDelete, lblScale, _numScale, btnMeasureScale, _cboTableKind, btnExport, btnExcel
            });

            Controls.Add(_grid);
            Controls.Add(bottomPanel);
            Controls.Add(topContainer);
        }

        private void BuildGridColumns()
        {
            _grid.Columns.Clear();

            // 0. Trạng thái nhận diện (chấm xanh = khớp bộ block mẫu, chấm đỏ = không có trong bộ mẫu)
            var colStatus = new DataGridViewImageColumn
            {
                Name = "colStatus",
                HeaderText = "TT",
                ToolTipText = "Chấm xanh: có trong bộ block mẫu đang chọn (tên, đơn vị theo bộ mẫu)",
                Width = 32,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            _grid.Columns.Add(colStatus);

            // 1. Thumbnail (ảnh lấy qua CellFormatting từ bộ nhớ ảnh _thumbCache)
            var colImg = new DataGridViewImageColumn
            {
                Name = "colThumbnail",
                HeaderText = "Ký hiệu",
                Width = 60,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            colImg.DefaultCellStyle.NullValue = null;
            _grid.Columns.Add(colImg);

            // 2. Block Name
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colBlockName",
                HeaderText = "Block Name",
                DataPropertyName = "BlockName",
                ReadOnly = false,
                Width = 140
            });

            // 3. Tên hiển thị (TÊN THIẾT BỊ)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colDisplayName",
                HeaderText = "Tên thiết bị",
                DataPropertyName = "DisplayName",
                ReadOnly = false,
                Width = 200
            });

            // 4. Chủng loại (Visibility state)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colVisibility",
                HeaderText = "Chủng loại",
                DataPropertyName = "VisibilityState",
                ReadOnly = false,
                Width = 130
            });

            // 5. Loại block (Tĩnh / Động / Có thuộc tính)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colBlockKind",
                HeaderText = "Loại Block",
                DataPropertyName = "BlockKind",
                ReadOnly = false,
                Width = 90
            });

            // 6. Đơn vị
            var colUnit = new DataGridViewComboBoxColumn
            {
                Name = "colUnit",
                HeaderText = "Đơn vị",
                DataPropertyName = "Unit",
                Width = 70,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
            };
            foreach (var u in SettingsManager.Current.Units) colUnit.Items.Add(u);
            _grid.Columns.Add(colUnit);

            // 7. Số lượng
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colCount",
                HeaderText = "SL",
                DataPropertyName = "Count",
                ReadOnly = false,
                Width = 55
            });

            // 7b. Số block thừa do trùng vị trí (không xuất ra bảng CAD)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colDup",
                HeaderText = "Trùng",
                ReadOnly = true,
                Width = 50
            });

            // 8. Nguồn (Model / Lồng)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colSource",
                HeaderText = "Nguồn",
                ReadOnly = true,
                Width = 75
            });

            // 9. Layer (A.4)
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colLayer",
                HeaderText = "Layer",
                ReadOnly = true,
                Width = 100,
                Visible = false // Mặc định ẩn
            });

            // 10. Ghi chú
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colNote",
                HeaderText = "Ghi chú",
                DataPropertyName = "Note",
                ReadOnly = false,
                Width = 120,
                Visible = false // Mặc định ẩn
            });
        }

        private void BuildContextMenuStrips()
        {
            // Context menu cho ảnh
            _imageContextMenu = new ContextMenuStrip();
            _imageContextMenu.Items.Add("Chọn file ảnh...", null, (s, e) => Action_SelectCustomImage());
            _imageContextMenu.Items.Add("Chụp vùng trên bản vẽ...", null, (s, e) => Action_CaptureDrawingWindow());
            _imageContextMenu.Items.Add(new ToolStripSeparator());
            _imageContextMenu.Items.Add("Render lại từ block", null, (s, e) => Action_RerenderFromBlock());
            _imageContextMenu.Items.Add("Xoá ảnh tuỳ chỉnh", null, (s, e) => Action_ClearCustomImage());

            // Context menu header ẩn/hiện cột
            _headerContextMenu = new ContextMenuStrip();
        }

        private Button MakeButton(string text, EventHandler onClick, int width = 80)
        {
            var btn = new Button { Text = text, Width = width, Height = 30 };
            btn.Click += onClick;
            return btn;
        }

        // ============================== FORMATTING & EVENTS ==============================

        /// <summary>Dòng i của lưới = phần tử i của BindingSource (không lọc / sắp xếp trong BindingSource).
        /// Không dùng _grid.Rows[i] trong lúc vẽ: truy cập Rows[i] tách dòng dùng chung, tốn bộ nhớ.</summary>
        private BlockItem ItemAt(int rowIndex) =>
            rowIndex >= 0 && rowIndex < _bindingSource.Count ? _bindingSource[rowIndex] as BlockItem : null;

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            var item = ItemAt(e.RowIndex);
            if (item == null) return;
            string colName = _grid.Columns[e.ColumnIndex].Name;

            // Đánh dấu vàng nhạt block A$C... Tô theo ô đang vẽ; trước đây đổi DefaultCellStyle của cả dòng ngay trong
            // lúc vẽ -> lưới vẽ lại liên tục, và màu còn dính ở dòng đó sau khi sắp xếp.
            if (item.BlockName != null && item.BlockName.StartsWith("A$C", StringComparison.OrdinalIgnoreCase))
                e.CellStyle.BackColor = QuickBlockBack;

            if (colName == "colStatus")
            {
                e.Value = item.IsMatchedByLibrary ? _greenDot : _redDot;
                return;
            }

            // Căn lề theo lựa chọn của user (mặc định căn giữa, giống bảng xuất CAD)
            e.CellStyle.Alignment = ToGridAlignment(item.GetAlignment(colName));

            switch (colName)
            {
                case "colThumbnail":
                    e.Value = GetThumb(item);
                    break;
                // Số block thừa do trùng vị trí: đỏ khi > 0, để trống khi không trùng
                case "colDup":
                    int extra = item.DuplicateExtra;
                    e.Value = extra > 0 ? extra.ToString() : "";
                    if (extra > 0) e.CellStyle.ForeColor = DuplicateRed;
                    break;
                // Nguồn (Model / Lồng)
                case "colSource":
                    e.Value = item.NestDepth == 0 ? "Model" : $"Lồng ({item.NestDepth})";
                    break;
                case "colLayer":
                    e.Value = item.AllLayers != null && item.AllLayers.Length > 1
                        ? $"{item.LayerName} (+{item.AllLayers.Length - 1})"
                        : item.LayerName ?? "";
                    break;
                default:
                    // Cột Premium: SL theo khu vực, thuộc tính
                    if (colName.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase) ||
                        colName.StartsWith(PremiumColumns.AttrPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        e.Value = item.ExtraValues != null && item.ExtraValues.TryGetValue(colName, out var pv) ? pv : "";
                        e.FormattingApplied = true;
                    }
                    break;
            }
        }

        /// <summary>Chú thích khi rê chuột (tính lúc cần, không ghi vào ô trong lúc vẽ).</summary>
        private void Grid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            var item = ItemAt(e.RowIndex);
            if (item == null) return;
            switch (_grid.Columns[e.ColumnIndex].Name)
            {
                case "colStatus":
                    e.ToolTipText = item.IsMatchedByLibrary ? "Có trong bộ block mẫu đang chọn" : "Không có trong bộ block mẫu đang chọn";
                    break;
                case "colDup":
                    int extra = item.DuplicateExtra;
                    if (extra > 0)
                        e.ToolTipText = $"{item.DuplicateGroups.Count} vị trí có block cùng tên chồng / che lấp nhau, thừa {extra} block. Bấm 'Tìm trùng' để xem.";
                    break;
                case "colLayer":
                    if (item.AllLayers != null && item.AllLayers.Length > 1) e.ToolTipText = string.Join("\n", item.AllLayers);
                    break;
            }
        }

        private void Grid_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                if (_grid.Columns[e.ColumnIndex].Name == "colThumbnail")
                {
                    if (!_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected)
                    {
                        _grid.ClearSelection();
                        _grid.Rows[e.RowIndex].Selected = true;
                    }
                    _imageContextMenu.Show(Cursor.Position);
                }
            }
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowColumnVisibilityMenu(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Hoãn sắp xếp: nếu đây là nửa đầu của double-click (đổi tên cột) thì huỷ
                _pendingSortColumn = _grid.Columns[e.ColumnIndex].Name;
                _headerSortTimer.Stop();
                _headerSortTimer.Start();
            }
        }

        /// <summary>Double-click tiêu đề cột -> đổi tên cột. Tên mới dùng luôn làm tiêu đề khi xuất bảng CAD.</summary>
        private void Grid_ColumnHeaderMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;
            _headerSortTimer.Stop();
            _pendingSortColumn = null;

            var col = _grid.Columns[e.ColumnIndex];
            string newName = UiKit.PromptText(this, "Đổi tên cột", $"Tên mới cho cột '{col.HeaderText}':", col.HeaderText);
            if (newName == null) return;
            newName = newName.Trim();
            if (newName.Length == 0 || newName == col.HeaderText) return;

            Logger.Log($"[BlockScheduleForm] Đổi tên cột {col.Name}: '{col.HeaderText}' -> '{newName}'");
            col.HeaderText = newName;
            var dict = SettingsManager.Current.ColumnHeaders ?? new Dictionary<string, string>();
            dict[col.Name] = newName;
            SettingsManager.Current.ColumnHeaders = dict;
            SettingsManager.SaveSettings();
        }

        private void ApplyColumnHeadersFromSettings()
        {
            var dict = SettingsManager.Current.ColumnHeaders;
            if (dict == null || dict.Count == 0) return;
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (!dict.TryGetValue(col.Name, out string header) || string.IsNullOrWhiteSpace(header)) continue;
                // "Ảnh" là tên mặc định cũ của cột ký hiệu (đổi thành "Ký hiệu" 28/09/2026) -> không đè tên mới
                if (col.Name == "colThumbnail" && header == "Ảnh") continue;
                col.HeaderText = header;
            }
        }

        /// <summary>
        /// Double-click: ô sửa được (chữ, số lượng, đơn vị...) -> vào chế độ sửa;
        /// ô Ký hiệu / TT / Nguồn / Layer -> zoom + highlight block trên bản vẽ như trước.
        /// </summary>
        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var col = _grid.Columns[e.ColumnIndex];
            if (IsEditableColumn(col))
            {
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                _grid.BeginEdit(true);
                return;
            }

            var item = ItemAt(e.RowIndex);
            if (item != null && item.ObjectIds != null && item.ObjectIds.Count > 0)
            {
                ScheduleManager.ZoomAndHighlight(_doc, item.ObjectIds);
            }
        }

        private static bool IsEditableColumn(DataGridViewColumn col) =>
            !col.ReadOnly && !(col is DataGridViewImageColumn) && !string.IsNullOrEmpty(col.DataPropertyName);

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (_grid.IsCurrentCellInEditMode) return;

            if (e.KeyCode == Keys.Delete)
            {
                Btn_Delete_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Insert)
            {
                AddManualRow();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2 && _grid.CurrentCell != null && IsEditableColumn(_grid.Columns[_grid.CurrentCell.ColumnIndex]))
            {
                _grid.BeginEdit(true);
                e.Handled = true;
            }
        }

        /// <summary>Nhập sai kiểu (vd chữ vào cột SL) -> báo lỗi thân thiện thay vì hộp thoại lỗi mặc định của DataGridView.</summary>
        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            string colHeader = e.ColumnIndex >= 0 ? _grid.Columns[e.ColumnIndex].HeaderText : "?";
            Logger.Warn($"[BlockScheduleForm.Grid_DataError] Ô [{e.RowIndex},{colHeader}]: {e.Exception?.Message}");
            MessageBox.Show($"Giá trị không hợp lệ cho cột '{colHeader}'.\nCột số lượng chỉ nhận số nguyên.", "Nhập sai",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.ThrowException = false;
            e.Cancel = true;
        }

        /// <summary>Thêm 1 dòng nhập tay (thiết bị không có trong bản vẽ) ngay dưới dòng đang chọn.</summary>
        private void AddManualRow()
        {
            _grid.EndEdit();
            var selected = GetSelectedItems();
            int insertAt = selected.Count > 0 ? selected.Max(i => _allItems.IndexOf(i)) + 1 : _allItems.Count;

            var item = new BlockItem
            {
                BlockName = "",
                DisplayName = "Thiết bị mới",
                VisibilityState = "",
                BlockKind = "Thêm tay",
                Unit = "Cái",
                Count = 1
            };
            _allItems.Insert(insertAt, item);
            RenumberOrder();
            Logger.Log($"[BlockScheduleForm.AddManualRow] Thêm dòng nhập tay tại vị trí {insertAt + 1}/{_allItems.Count}");

            _grid.ClearSelection();
            _grid.Rows[insertAt].Selected = true;
            // Cột Tên thiết bị đang ẩn thì đặt CurrentCell vào nó sẽ ném InvalidOperationException
            if (_grid.Columns["colDisplayName"].Visible)
            {
                _grid.CurrentCell = _grid.Rows[insertAt].Cells["colDisplayName"];
                _grid.BeginEdit(true);
            }
        }

        // ============================== COLUMN VISIBILITY (MỤC 5) ==============================

        private void ShowColumnVisibilityMenu(Point screenPos)
        {
            _headerContextMenu.Items.Clear();

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                var item = new ToolStripMenuItem(col.HeaderText)
                {
                    CheckOnClick = true,
                    Checked = col.Visible,
                    Tag = col
                };

                // Chỉ cột SL không cho ẩn. Block Name cho ẩn (yêu cầu 29/09/2026): ẩn thì bảng xuất cũng bỏ cột này
                if (col.Name == "colCount")
                {
                    item.Enabled = false;
                }
                else
                {
                    item.Click += (s, e) =>
                    {
                        col.Visible = item.Checked;
                        SaveColumnVisibilitySettings();
                    };
                }
                _headerContextMenu.Items.Add(item);
            }

            _headerContextMenu.Items.Add(new ToolStripSeparator());
            _headerContextMenu.Items.Add("Hiện lại tất cả cột", null, (s, e) =>
            {
                foreach (DataGridViewColumn col in _grid.Columns) col.Visible = true;
                SaveColumnVisibilitySettings();
            });

            _headerContextMenu.Show(screenPos);
        }

        private void ApplyColumnVisibilityFromSettings()
        {
            var dict = SettingsManager.Current.ColumnVisibility;
            if (dict == null || dict.Count == 0) return;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (dict.TryGetValue(col.Name, out bool isVis))
                {
                    if (col.Name != "colCount")
                        col.Visible = isVis;
                }
            }
        }

        private void SaveColumnVisibilitySettings()
        {
            var dict = SettingsManager.Current.ColumnVisibility ?? new Dictionary<string, bool>();
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                dict[col.Name] = col.Visible;
            }
            SettingsManager.Current.ColumnVisibility = dict;
            SettingsManager.SaveSettings();
        }

        // ============================== ẢNH KÝ HIỆU ==============================

        /// <summary>Ảnh của 1 dòng: ảnh tuỳ chỉnh nếu có, không thì ảnh render từ block.</summary>
        private Image GetThumb(BlockItem item) =>
            (string.IsNullOrEmpty(item.CustomImagePath) ? null : LoadThumb(item.CustomImagePath)) ?? LoadThumb(item.ThumbnailPath);

        private Image LoadThumb(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_thumbCache.TryGetValue(path, out var img)) return img;
            if (!File.Exists(path)) return null; // chưa có file (đang render) -> lần vẽ sau thử lại
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var tmp = Image.FromStream(fs))
                    img = new Bitmap(tmp);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BlockScheduleForm] Lỗi đọc ảnh '{path}': {ex.Message}");
                img = null;
            }
            _thumbCache[path] = img;
            return img;
        }

        /// <summary>File ảnh sắp bị ghi lại (render lại) -> bỏ bản đã đọc, lần vẽ sau đọc file mới.</summary>
        private void ForgetThumb(string path)
        {
            if (string.IsNullOrEmpty(path) || !_thumbCache.TryGetValue(path, out var img)) return;
            _thumbCache.Remove(path);
            if (img != null) _retiredThumbs.Add(img);
        }

        private void Action_SelectCustomImage()
        {
            var selectedItems = GetSelectedItems();
            if (selectedItems.Count == 0) return;

            using (var ofd = new OpenFileDialog
            {
                Title = "Chọn ảnh cho thiết bị",
                Filter = "File ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string customFolder = ThumbnailGenerator.CustomImagesFolder;
                    if (!Directory.Exists(customFolder)) Directory.CreateDirectory(customFolder);

                    string guid = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string targetFile = Path.Combine(customFolder, $"{Path.GetFileNameWithoutExtension(ofd.FileName)}_{guid}.png");

                    // Convert sang PNG nếu cần
                    using (var srcImg = Image.FromFile(ofd.FileName))
                    {
                        srcImg.Save(targetFile, ImageFormat.Png);
                    }

                    foreach (var item in selectedItems)
                    {
                        item.CustomImagePath = targetFile;
                    }
                    _grid.Invalidate();
                }
            }
        }

        private void Action_CaptureDrawingWindow()
        {
            var selectedItems = GetSelectedItems();
            if (selectedItems.Count == 0) return;

            Hide();
            try
            {
                var ed = _doc.Editor;
                using (_doc.LockDocument())
                {
                    var p1Prompt = ed.GetPoint("\nChọn điểm thứ nhất của vùng chụp: ");
                    if (p1Prompt.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;

                    var p2Prompt = ed.GetCorner("\nChọn góc đối diện: ", p1Prompt.Value);
                    if (p2Prompt.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;

                    var bmp = ThumbnailGenerator.RenderWindowToBitmap(_doc, p1Prompt.Value, p2Prompt.Value, 96, 96);
                    if (bmp != null)
                    {
                        string customFolder = ThumbnailGenerator.CustomImagesFolder;
                        if (!Directory.Exists(customFolder)) Directory.CreateDirectory(customFolder);

                        string guid = Guid.NewGuid().ToString("N").Substring(0, 8);
                        string targetFile = Path.Combine(customFolder, $"capture_{guid}.png");
                        bmp.Save(targetFile, ImageFormat.Png);
                        bmp.Dispose();

                        foreach (var item in selectedItems)
                        {
                            item.CustomImagePath = targetFile;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Action_CaptureDrawingWindow");
            }
            finally
            {
                Show();
                _grid.Invalidate();
            }
        }

        private void Action_RerenderFromBlock()
        {
            var selectedItems = GetSelectedItems();
            if (selectedItems.Count == 0) return;

            foreach (var item in selectedItems)
            {
                item.CustomImagePath = null;
                if (!string.IsNullOrEmpty(item.ThumbnailPath))
                {
                    ForgetThumb(item.ThumbnailPath);
                    ThumbnailGenerator.ForgetHash(item.ThumbnailPath);
                    if (File.Exists(item.ThumbnailPath))
                    {
                        try { File.Delete(item.ThumbnailPath); } catch { }
                    }
                }
                CreateThumbnails(item);
            }
            _grid.Invalidate();
        }

        private void Action_ClearCustomImage()
        {
            var selectedItems = GetSelectedItems();
            if (selectedItems.Count == 0) return;

            foreach (var item in selectedItems)
            {
                item.CustomImagePath = null;
            }
            _grid.Invalidate();
        }

        /// <summary>Ảnh hiển thị (theo chủng loại nếu đang tách) + ShapeHash từ định nghĩa block gốc.</summary>
        private void CreateThumbnails(BlockItem item)
        {
            string cacheKey = (!string.IsNullOrEmpty(item.VisibilityState) && _chkSplitVisibility.Checked)
                ? $"{item.BlockName}_{item.VisibilityState}"
                : item.BlockName;

            item.ThumbnailPath = ThumbnailGenerator.GetOrCreateThumbnail(_doc, item.SourceBtrId, cacheKey);
            if (item.DynamicBtrId.IsValid && !item.DynamicBtrId.IsNull)
            {
                string defThumb = ThumbnailGenerator.GetOrCreateThumbnail(_doc, item.DynamicBtrId, $"{item.BlockName}_defhash");
                if (!string.IsNullOrEmpty(defThumb))
                {
                    item.ShapeHash = ThumbnailGenerator.GetHashFromPngFile(defThumb);
                }
            }
        }

        // ============================== SCAN & RE-EXTRACTION (MỤC 4 & A.4) ==============================

        private int GetSelectedMaxDepth()
        {
            switch (_cboScanDepth.SelectedIndex)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 3;
                case 3: return int.MaxValue;
                default: return 2;
            }
        }

        private ExtractionOptions CurrentOptions() => new ExtractionOptions
        {
            MaxDepth = GetSelectedMaxDepth(),
            CountParentBlocks = _chkCountParents.Checked,
            SplitByVisibility = _chkSplitVisibility.Checked,
            SplitByLayer = _chkSplitLayer.Checked,
            SplitAttributeKeys = (SettingsManager.Current.SplitAttributeKeys ?? new List<string>()).ToList()
        };

        private void TriggerReExtraction()
        {
            if (_isUpdatingUi || BlockExtractor.LastSelectedObjectIds.Count == 0) return;

            Cursor.Current = Cursors.WaitCursor;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var newItems = BlockExtractor.ExtractFromObjectIds(_doc, BlockExtractor.LastSelectedObjectIds, CurrentOptions());
            newItems = TemplateLibraryManager.Apply(newItems, CurrentTemplate(), TemplateLibraryManager.IsFilterActive(CurrentTemplate()));

            ReplaceAllItems(newItems);
            RecomputeDuplicates();
            Logger.Log($"[BlockScheduleForm] Quét lại {BlockExtractor.LastSelectedObjectIds.Count} đối tượng -> {newItems.Count} dòng trong {sw.ElapsedMilliseconds} ms");
        }

        // ============================== BLOCK MẪU + QUÉT THÊM ==============================

        private TemplateLibrary _templateCache;

        /// <summary>Bộ block mẫu đang chọn (đọc lại file mỗi lần đổi bộ / lưu).</summary>
        private TemplateLibrary CurrentTemplate()
        {
            string name = _cboTemplateSet.SelectedItem?.ToString() ?? SettingsManager.Current.CurrentTemplateSet;
            if (_templateCache == null || !string.Equals(_templateCache.Name, name, StringComparison.OrdinalIgnoreCase))
                _templateCache = TemplateLibraryManager.Load(name);
            return _templateCache;
        }

        private void RefreshTemplateSetList(string select)
        {
            _cboTemplateSet.Items.Clear();
            foreach (var n in TemplateLibraryManager.ListSets()) _cboTemplateSet.Items.Add(n);
            if (!string.IsNullOrEmpty(select) && !_cboTemplateSet.Items.Contains(select)) _cboTemplateSet.Items.Add(select);
            _cboTemplateSet.SelectedItem = !string.IsNullOrEmpty(select) ? select : _cboTemplateSet.Items[0];
            _templateCache = null;
        }

        /// <summary>Ghi số block mẫu lên ô tick; bộ trống thì báo đang quét tất cả.</summary>
        private void UpdateTemplateCheckText()
        {
            if (_chkOnlyTemplate == null) return;
            int n = CurrentTemplate().Entries.Count;
            _chkOnlyTemplate.Text = n > 0 ? $"Chỉ quét block mẫu ({n} block)" : "Chỉ quét block mẫu (bộ trống: quét tất cả)";
        }

        private void OpenTemplateDialog()
        {
            if (_tplDialog == null || _tplDialog.IsDisposed)
            {
                _tplDialog = new TemplateLibraryDialog(_doc, _cboTemplateSet.SelectedItem?.ToString());
                _tplDialog.Saved += name =>
                {
                    // Lưu bộ mẫu -> áp lại tên / thứ tự / lọc theo bộ vừa lưu
                    RefreshTemplateSetList(name);
                    UpdateTemplateCheckText();
                    TriggerReExtraction();
                };
                _tplDialog.FormClosed += (s, e) =>
                {
                    RefreshTemplateSetList(SettingsManager.Current.CurrentTemplateSet);
                    UpdateTemplateCheckText();
                };
                _tplDialog.Show(this);
            }
            else
            {
                if (!_tplDialog.Visible) _tplDialog.Show(this);
                _tplDialog.Activate();
            }
        }

        /// <summary>
        /// Quét thêm: chọn thêm vùng, cộng vào bảng đang có. Dòng đã có (cùng tên block + chủng loại) được cộng SL,
        /// giữ nguyên tên / đơn vị / căn lề / ảnh user đã sửa; thiết bị mới thêm xuống cuối.
        /// Đối tượng đã chọn ở lần trước không đếm lại.
        /// </summary>
        private void Btn_ScanMore_Click(object sender, EventArgs e)
        {
            _grid.EndEdit();
            var template = CurrentTemplate();
            bool onlyTemplate = TemplateLibraryManager.IsFilterActive(template);
            string message = null;
            Hide();
            try
            {
                List<BlockItem> added;
                int newObjects, already;
                using (_doc.LockDocument())
                    added = BlockExtractor.ExtractAdditionalSelection(_doc, CurrentOptions(), onlyTemplate ? template : null, out newObjects, out already);
                if (added == null) return;

                added = TemplateLibraryManager.Apply(added, template, onlyTemplate);

                int mergedRows = 0, newRows = 0, blocks = 0;
                RecomputeDuplicates(() =>
                {
                    foreach (var n in added)
                    {
                        blocks += n.Count;
                        var target = FindRowFor(n);
                        if (target != null)
                        {
                            target.Count += n.Count;
                            target.ObjectIds.AddRange(n.ObjectIds);
                            target.Instances.AddRange(n.Instances);
                            target.AllLayers = (target.AllLayers ?? new string[0]).Concat(n.AllLayers ?? new string[0]).Distinct().ToArray();
                            target.NestDepth = Math.Min(target.NestDepth, n.NestDepth);
                            mergedRows++;
                        }
                        else
                        {
                            _allItems.Add(n);
                            newRows++;
                        }
                    }
                });
                RenumberOrder();

                message = $"\n[LHB] Quét thêm: {newObjects} đối tượng mới" + (already > 0 ? $" (bỏ {already} đã chọn trước)" : "") +
                          $", {blocks} block -> cộng vào {mergedRows} dòng có sẵn, thêm {newRows} dòng mới.\n";
                Logger.Log($"[BlockScheduleForm.ScanMore] {message.Trim()} Tổng {_allItems.Count} dòng, chỉ block mẫu={onlyTemplate}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Btn_ScanMore_Click");
                MessageBox.Show($"Quét thêm thất bại: {ex.Message}\nXem chi tiết tại: {Logger.GetLogFilePath()}", "Lỗi");
            }
            finally
            {
                Show();
                if (message != null) _doc.Editor.WriteMessage(message);
            }
        }

        /// <summary>Dòng đang có chứa cùng loại block (tên + chủng loại, cùng layer nếu tách theo layer).</summary>
        private BlockItem FindRowFor(BlockItem n)
        {
            var keys = new HashSet<string>((n.Instances ?? new List<BlockInstanceRef>()).Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
            if (keys.Count == 0) return null;
            return _allItems.FirstOrDefault(r =>
                r.Instances != null && r.Instances.Any(i => keys.Contains(i.Key)) &&
                (!_chkSplitLayer.Checked || (r.AllLayers != null && r.AllLayers.Contains(n.LayerName, StringComparer.OrdinalIgnoreCase))));
        }

        // ============================== BLOCK TRÙNG VỊ TRÍ ==============================

        internal IReadOnlyList<BlockItem> AllItems => _allItems;

        // Vòng khoanh chỗ trùng = nửa chiều cao dòng bảng xuất, chữ "xN" = cỡ chữ bảng (theo ô Tỉ lệ)
        internal double MarkerRadius => new TableExportConfig { TableScale = (double)_numScale.Value }.ActualRowHeight * 0.5;
        internal double MarkerTextHeight => new TableExportConfig { TableScale = (double)_numScale.Value }.ActualTextHeight;

        /// <summary>
        /// Tính lại block trùng cho mọi dòng. Trả SL về số đếm gốc trước, chạy thay đổi (gộp, xoá...) rồi mới
        /// tìm trùng và trừ lại -> không bao giờ trừ 2 lần.
        /// </summary>
        internal void RecomputeDuplicates(Action mutate = null)
        {
            DuplicateFinder.SetExclusion(_allItems, false);
            mutate?.Invoke();
            DuplicateFinder.Detect(_allItems, SettingsManager.Current.DuplicateTolerance, SettingsManager.Current.DuplicateOverlapPercent);
            DuplicateFinder.SetExclusion(_allItems, _chkExcludeDup.Checked);
            RefreshPremiumColumns();
            _bindingSource.ResetBindings(false);
            if (_dupDialog != null && !_dupDialog.IsDisposed) _dupDialog.RefreshList();
        }

        /// <summary>Sau khi xoá block thừa trên bản vẽ: bỏ các block đã xoá khỏi dòng tương ứng rồi tính lại.</summary>
        internal void OnDuplicatesDeleted(List<(BlockItem Item, BlockInstanceRef Instance)> deleted)
        {
            RecomputeDuplicates(() =>
            {
                foreach (var (item, inst) in deleted)
                {
                    item.Instances.Remove(inst);
                    item.ObjectIds.Remove(inst.Path[0]);
                    item.Count -= 1;
                }
            });
            Logger.Log($"[BlockScheduleForm.OnDuplicatesDeleted] Cập nhật {deleted.Select(d => d.Item).Distinct().Count()} dòng sau khi xoá {deleted.Count} block thừa");
        }

        private void Btn_FindDup_Click(object sender, EventArgs e)
        {
            RecomputeDuplicates();
            if (_dupDialog == null || _dupDialog.IsDisposed)
            {
                _dupDialog = new DuplicateDialog(_doc, this);
                _dupDialog.Show(this);
            }
            else
            {
                if (!_dupDialog.Visible) _dupDialog.Show(this);
                _dupDialog.Activate();
            }
            // Chưa có bảng xuất -> chỉ khoanh đỏ chỗ trùng; xuất bảng sẽ vẽ thêm đường dẫn từ bảng
            _dupDialog.RedrawCirclesIfAny();
        }

        // ============================== THUMBNAIL LOADER ==============================

        /// <summary>Tạo ảnh cho dòng chưa có ảnh (lúc quét tạo lỗi / chưa tạo), có thanh tiến trình.</summary>
        private void LoadThumbnailsAsync()
        {
            var itemsNeedingThumb = _allItems.Where(i => string.IsNullOrEmpty(i.ThumbnailPath)).ToList();
            if (itemsNeedingThumb.Count == 0) return;

            using (var progress = new Form
            {
                Width = 340,
                Height = 110,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ControlBox = false,
                Text = "Đang tạo ảnh thu nhỏ (GraphicsSystem)..."
            })
            {
                var bar = new ProgressBar { Left = 15, Top = 15, Width = 295, Maximum = itemsNeedingThumb.Count };
                var lbl = new Label { Left = 15, Top = 45, Width = 295, Text = "" };
                progress.Controls.Add(bar);
                progress.Controls.Add(lbl);

                progress.Shown += (s, e) =>
                {
                    for (int i = 0; i < itemsNeedingThumb.Count; i++)
                    {
                        var item = itemsNeedingThumb[i];
                        lbl.Text = $"{i + 1}/{itemsNeedingThumb.Count}: {item.BlockName}";
                        System.Windows.Forms.Application.DoEvents();

                        try
                        {
                            CreateThumbnails(item);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"LoadThumbnailsAsync: {item.BlockName}");
                        }

                        bar.Value = i + 1;
                    }
                    progress.Close();
                };
                progress.ShowDialog(this);
            }

            _grid.Invalidate();
        }

        // ============================== BUTTON HANDLERS ==============================

        private void Btn_NameFromVis_Click(object sender, EventArgs e)
        {
            var selectedItems = GetSelectedItems();
            var targets = selectedItems.Count > 0 ? selectedItems : _allItems.ToList();
            int count = 0;
            foreach (var item in targets)
            {
                if (!string.IsNullOrEmpty(item.VisibilityState))
                {
                    item.DisplayName = item.VisibilityState;
                    count++;
                }
            }
            _bindingSource.ResetBindings(false);
            MessageBox.Show($"Đã cập nhật tên hiển thị từ chủng loại cho {count} block.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Btn_Highlight_Click(object sender, EventArgs e)
        {
            var items = GetSelectedItems();
            if (items.Count == 0) return;
            ScheduleManager.HighlightItems(_doc, items);
        }

        private void Btn_Unhighlight_Click(object sender, EventArgs e)
        {
            ScheduleManager.UnhighlightPrevious(_doc.Database);
            _doc.Editor.UpdateScreen();
        }

        private void Btn_SuggestMerge_Click(object sender, EventArgs e)
        {
            int threshold = SettingsManager.Current.HashThreshold;
            using (var dlg = new SuggestMergeDialog(_allItems.ToList(), threshold))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedGroups.Count > 0)
                {
                    RecomputeDuplicates(() =>
                    {
                        foreach (var grp in dlg.SelectedGroups)
                        {
                            var primary = grp.PrimaryItem;
                            var others = grp.CandidateItems.Where(x => x != primary).ToList();

                            foreach (var other in others)
                            {
                                primary.Count += other.Count;
                                primary.ObjectIds.AddRange(other.ObjectIds);
                                primary.Instances.AddRange(other.Instances);
                                if (!primary.MergedSourceNames.Contains(other.BlockName))
                                    primary.MergedSourceNames.Add(other.BlockName);
                                _allItems.Remove(other);
                            }
                            primary.IsMergedGroup = true;
                        }
                    });
                    RenumberOrder();
                    MessageBox.Show($"Đã gộp thành công {dlg.SelectedGroups.Count} nhóm.", "Thông báo");
                }
            }
        }

        private void Btn_MeasureScale_Click(object sender, EventArgs e)
        {
            Hide();
            try
            {
                var ed = _doc.Editor;
                using (_doc.LockDocument())
                {
                    var p1Prompt = ed.GetPoint("\nChọn điểm đầu của chiều rộng mong muốn: ");
                    if (p1Prompt.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;

                    var p2Prompt = ed.GetPoint("\nChọn điểm thứ hai: ");
                    if (p2Prompt.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;

                    double dist = p1Prompt.Value.DistanceTo(p2Prompt.Value);
                    double baseTotalWidth = 15.0 + 7.5 + 40.0 + 20.0 + 15.0; // Tổng chiều rộng cơ sở mẫu ~ 97.5
                    double calculatedScale = Math.Round(dist / baseTotalWidth, 1);
                    if (calculatedScale < 1) calculatedScale = 1;
                    if (calculatedScale > 10000) calculatedScale = 10000;

                    _numScale.Value = (decimal)calculatedScale;
                    SettingsManager.Current.TableScale = calculatedScale;
                    SettingsManager.SaveSettings();
                    ed.WriteMessage($"\nĐã xác định hệ số tỉ lệ: {calculatedScale}\n");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Btn_MeasureScale_Click");
            }
            finally
            {
                Show();
            }
        }

        private void Btn_Export_Click(object sender, EventArgs e)
        {
            Hide();
            try
            {
                var ed = _doc.Editor;
                var ppr = ed.GetPoint("\nChọn điểm chèn bảng: ");
                if (ppr.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    Show();
                    return;
                }

                // Cấu hình cột xuất theo các cột đang HIỂN THỊ trong grid
                // Độ rộng cột + chiều cao dòng grid (pixel) -> bảng xuất rộng theo cột user kéo trên form
                var exportConfig = new TableExportConfig
                {
                    TableScale = (double)_numScale.Value,
                    GridRowHeightPx = _grid.RowTemplate.Height,
                    Columns = new List<ColumnExportDef>()
                };

                foreach (DataGridViewColumn col in _grid.Columns)
                {
                    if (col.Visible && col.Name != "colStatus" && col.Name != "colSource" && col.Name != "colDup")
                    {
                        exportConfig.Columns.Add(new ColumnExportDef
                        {
                            Key = col.Name,
                            HeaderText = col.HeaderText,
                            GridWidthPx = col.Width
                        });
                    }
                }

                // GetPoint trả toạ độ theo UCS, entity ghi vào DB theo WCS -> phải đổi hệ toạ độ
                var ptWcs = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                string tableKind = _cboTableKind.SelectedItem?.ToString() ?? TableKindAcad;
                Logger.Log($"Btn_Export_Click: kiểu bảng='{tableKind}', điểm UCS={ppr.Value}, WCS={ptWcs}, " +
                           $"cột=[{string.Join(", ", exportConfig.Columns.Select(c => c.Key))}]");

                SettingsManager.Current.TableKind = tableKind;
                SettingsManager.SaveSettings();

                if (tableKind == TableKindLines)
                    TableExporter.ExportTable(_doc, _allItems.ToList(), ptWcs, exportConfig);
                else
                {
                    // Bảng tự cập nhật (Premium P2): chỉ khi còn dùng thử / đã kích hoạt, không hỏi lại
                    if (Core.LicenseManager.IsLicensed || Core.LicenseManager.TrialDaysLeft > 0) exportConfig.ScanInfo = BuildScanInfo();
                    TableExporterAcad.ExportTable(_doc, _allItems.ToList(), ptWcs, exportConfig);
                }
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Btn_Export_Click");
                MessageBox.Show($"Xuất bảng thất bại: {ex.GetType().Name} - {ex.Message}\nXem chi tiết tại: {Logger.GetLogFilePath()}", "Lỗi");
                Show();
            }
        }

        private void Btn_Merge_Click(object sender, EventArgs e)
        {
            var selected = GetSelectedItems();
            if (selected.Count < 2)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 2 dòng để gộp nhóm.", "Thông báo");
                return;
            }

            string defaultName = selected[0].DisplayName ?? selected[0].BlockName;
            string newName = UiKit.PromptText(this, "Gộp nhóm", "Nhập tên hiển thị cho nhóm gộp:", defaultName);
            if (string.IsNullOrEmpty(newName)) return;

            var primary = selected[0];
            primary.DisplayName = newName;
            primary.IsMergedGroup = true;

            RecomputeDuplicates(() =>
            {
                for (int i = 1; i < selected.Count; i++)
                {
                    primary.Count += selected[i].Count;
                    primary.ObjectIds.AddRange(selected[i].ObjectIds);
                    primary.Instances.AddRange(selected[i].Instances);
                    if (!primary.MergedSourceNames.Contains(selected[i].BlockName))
                        primary.MergedSourceNames.Add(selected[i].BlockName);
                    _allItems.Remove(selected[i]);
                }
            });

            RenumberOrder();
        }

        private void Btn_Delete_Click(object sender, EventArgs e)
        {
            var selected = GetSelectedItems();
            if (selected.Count == 0) return;
            foreach (var item in selected) _allItems.Remove(item);
            RenumberOrder();
        }

        // ============================== HELPERS ==============================

        /// <summary>Grid chọn theo ô -> dòng được chọn = mọi dòng có ít nhất 1 ô đang chọn, theo thứ tự trên grid.</summary>
        private List<BlockItem> GetSelectedItems()
        {
            var rowIndexes = new SortedSet<int>();
            foreach (DataGridViewCell cell in _grid.SelectedCells) rowIndexes.Add(cell.RowIndex);
            return rowIndexes.Select(ItemAt).Where(i => i != null).ToList();
        }

        /// <summary>Căn lề các ô đang quét chọn (như Excel). Lưu vào BlockItem để bảng xuất CAD căn đúng như grid.</summary>
        private void ApplyAlignmentToSelectedCells(CellHAlign align)
        {
            _grid.EndEdit();
            int count = 0;
            var columns = new HashSet<string>();
            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                var col = _grid.Columns[cell.ColumnIndex];
                if (!col.Visible || col.Name == "colStatus") continue;
                var item = ItemAt(cell.RowIndex);
                if (item == null) continue;

                item.CellAlignments ??= new Dictionary<string, CellHAlign>();
                if (align == CellHAlign.Center) item.CellAlignments.Remove(col.Name);
                else item.CellAlignments[col.Name] = align;
                columns.Add(col.Name);
                count++;
            }

            Logger.Log($"[BlockScheduleForm.ApplyAlignment] Căn {align} cho {count} ô, cột [{string.Join(", ", columns)}]");
            _grid.Invalidate();
        }

        private static DataGridViewContentAlignment ToGridAlignment(CellHAlign align)
        {
            switch (align)
            {
                case CellHAlign.Left: return DataGridViewContentAlignment.MiddleLeft;
                case CellHAlign.Right: return DataGridViewContentAlignment.MiddleRight;
                default: return DataGridViewContentAlignment.MiddleCenter;
            }
        }

        /// <summary>Thay toàn bộ dòng, lưới dựng lại 1 lần (trước đây mỗi dòng thêm vào là lưới cập nhật 1 lần).</summary>
        private void ReplaceAllItems(List<BlockItem> newItems)
        {
            _isUpdatingUi = true;
            _allItems.RaiseListChangedEvents = false;
            try
            {
                _allItems.Clear();
                foreach (var i in newItems) _allItems.Add(i);
            }
            finally
            {
                _allItems.RaiseListChangedEvents = true;
                _isUpdatingUi = false;
            }
            _allItems.ResetBindings();
        }

        private void RenumberOrder()
        {
            for (int i = 0; i < _allItems.Count; i++) _allItems[i].Order = i;
        }

        private void MoveSelected(int direction)
        {
            var selected = GetSelectedItems();
            if (selected.Count != 1) return;
            var item = selected[0];
            int idx = _allItems.IndexOf(item);
            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _allItems.Count) return;

            int colIdx = _grid.CurrentCell?.ColumnIndex ?? 0;
            _allItems.RemoveAt(idx);
            _allItems.Insert(newIdx, item);
            RenumberOrder();

            // Chọn lại đúng 1 dòng vừa di chuyển, nếu không ô cũ còn chọn -> lần bấm Lên/Xuống sau thấy 2 dòng và bỏ qua
            _grid.ClearSelection();
            if (_grid.Rows[newIdx].Visible && _grid.Columns[colIdx].Visible) _grid.CurrentCell = _grid.Rows[newIdx].Cells[colIdx];
            _grid.Rows[newIdx].Selected = true;
        }

        /// <summary>
        /// Lọc dòng theo ô tìm kiếm (không dấu, không phân biệt hoa thường). Từ khoá bỏ dấu 1 lần thay vì mỗi ô 1 lần.
        /// Lỗi v9: dòng đang là dòng hiện hành không ẩn được ("Row associated with the currency manager's position
        /// cannot be made invisible") -> gõ từ khoá không khớp dòng đang chọn là báo lỗi. Nay bỏ ô hiện hành trước.
        /// </summary>
        private void ApplyFilter()
        {
            if (_grid == null || _grid.IsDisposed) return;
            string key = VietnameseHelper.Fold(_txtSearch.Text?.Trim());
            try
            {
                _grid.EndEdit();
                if (key.Length > 0) _grid.CurrentCell = null;
                int shown = 0;
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    var item = ItemAt(i);
                    bool match = key.Length == 0 || item == null || MatchesSearch(item, key);
                    var row = _grid.Rows[i];
                    if (row.Visible != match) row.Visible = match;
                    if (match) shown++;
                }
                if (key.Length > 0) Logger.Log($"[BlockScheduleForm] Tìm '{_txtSearch.Text.Trim()}': {shown}/{_grid.Rows.Count} dòng");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BlockScheduleForm.ApplyFilter]");
            }
        }

        private static bool MatchesSearch(BlockItem item, string foldedKey) =>
            VietnameseHelper.Fold(item.BlockName).Contains(foldedKey)
            || VietnameseHelper.Fold(item.DisplayName).Contains(foldedKey)
            || VietnameseHelper.Fold(item.VisibilityState).Contains(foldedKey)
            || VietnameseHelper.Fold(item.Unit).Contains(foldedKey)
            || VietnameseHelper.Fold(item.Note).Contains(foldedKey)
            || VietnameseHelper.Fold(item.LayerName).Contains(foldedKey);

        private void SortByColumn(string colName)
        {
            if (_sortedColumn == colName) _sortAscending = !_sortAscending;
            else { _sortedColumn = colName; _sortAscending = true; }

            var list = _allItems.ToList();
            Func<BlockItem, object> keySelector = null;

            switch (colName)
            {
                case "colBlockName": keySelector = x => x.BlockName; break;
                case "colDisplayName": keySelector = x => x.DisplayName ?? x.BlockName; break;
                case "colVisibility": keySelector = x => x.VisibilityState ?? ""; break;
                case "colBlockKind": keySelector = x => x.BlockKind ?? ""; break;
                case "colUnit": keySelector = x => x.Unit ?? ""; break;
                case "colCount": keySelector = x => x.Count; break;
                case "colDup": keySelector = x => x.DuplicateExtra; break;
                case "colSource": keySelector = x => x.NestDepth; break;
                case "colLayer": keySelector = x => x.LayerName ?? ""; break;
                case "colNote": keySelector = x => x.Note ?? ""; break;
                default:
                    if (colName.StartsWith(ZoneManager.ColumnPrefix, StringComparison.OrdinalIgnoreCase))
                        keySelector = x => x.ExtraValues != null && x.ExtraValues.TryGetValue(colName, out var v) && int.TryParse(v, out int n) ? n : 0;
                    else if (colName.StartsWith(PremiumColumns.AttrPrefix, StringComparison.OrdinalIgnoreCase))
                        keySelector = x => x.ExtraValues != null && x.ExtraValues.TryGetValue(colName, out var v) ? v : "";
                    else keySelector = x => x.Order;
                    break;
            }

            var sorted = _sortAscending ? list.OrderBy(keySelector).ToList() : list.OrderByDescending(keySelector).ToList();
            ReplaceAllItems(sorted);
        }
    }
}

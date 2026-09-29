using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;
using Exception = System.Exception;
using Image = System.Drawing.Image;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Hộp thoại "Thông tin block mẫu" (modeless, lệnh LHBMAU hoặc nút "Block mẫu..." trên form), giống hộp thoại
    /// trong video mẫu: danh sách block mẫu [Xoá / Lên / Xuống] | Ký hiệu | Tên block | Chủng loại | Tên thống kê |
    /// Kiểu block | Đơn vị | Ghi chú; chọn bộ mẫu (Data1, Data2...), thêm block từ bản vẽ, chèn block mẫu, lưu.
    /// </summary>
    public class TemplateLibraryDialog : Form
    {
        private readonly Document _doc;
        private TemplateLibrary _lib;
        private readonly List<ObjectId> _pendingBtrIds = new List<ObjectId>();
        private bool _dirty;
        private bool _loading;

        private DataGridView _grid;
        private ComboBox _cboSet;
        private Label _lblHeader, _lblPath;

        /// <summary>Báo form thống kê đã lưu bộ mẫu (tên bộ) để áp lại tên / thứ tự.</summary>
        public event Action<string> Saved;

        public TemplateLibraryDialog(Document doc, string setName)
        {
            _doc = doc;
            BuildUi();
            RefreshSetList(setName);
            LoadSet(_cboSet.SelectedItem?.ToString() ?? TemplateLibraryManager.DefaultSetName);
        }

        private void BuildUi()
        {
            Text = "Thông tin block mẫu";
            Width = 1060;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            _lblHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.FromArgb(41, 128, 185),
                ForeColor = Color.White,
                Padding = new Padding(8, 5, 0, 0)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                // Shift / Ctrl + click chọn nhiều dòng, phím Delete xoá (yêu cầu 29/09/2026). Không dùng EditOnEnter:
                // ô đang sửa sẽ nuốt phím Delete và Shift + click -> sửa bằng double-click, gõ phím hoặc F2.
                MultiSelect = true,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                RowTemplate = { Height = 44 },
                BackgroundColor = SystemColors.Window
            };
            _grid.Columns.Add(MakeButtonColumn("colDel", "X", Color.FromArgb(192, 57, 43)));
            _grid.Columns.Add(MakeButtonColumn("colUp", "▲", Color.FromArgb(39, 174, 96)));
            _grid.Columns.Add(MakeButtonColumn("colDown", "▼", Color.FromArgb(39, 174, 96)));
            var colSym = new DataGridViewImageColumn { Name = "colSym", HeaderText = "Ký hiệu", Width = 60, ImageLayout = DataGridViewImageCellLayout.Zoom };
            colSym.DefaultCellStyle.NullValue = null;
            _grid.Columns.Add(colSym);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colBlock", HeaderText = "Tên block", Width = 170, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colVis", HeaderText = "Chủng loại", Width = 120, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDisplay", HeaderText = "Tên thống kê", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKind", HeaderText = "Kiểu block", Width = 90, ReadOnly = true });
            var colUnit = new DataGridViewComboBoxColumn { Name = "colUnit", HeaderText = "Đơn vị", Width = 70, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
            _grid.Columns.Add(colUnit);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNote", HeaderText = "Ghi chú", Width = 150 });
            _grid.CellContentClick += Grid_CellContentClick;
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Chọn đơn vị trong combo -> ghi ngay, không chờ rời ô
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.KeyDown += (s, e) =>
            {
                // Ô đang sửa thì Delete xoá chữ trong ô (phím đi vào ô sửa, không tới lưới)
                if (e.KeyCode != Keys.Delete || _grid.IsCurrentCellInEditMode) return;
                e.Handled = true;
                Action_DeleteSelected();
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !_grid.Columns[e.ColumnIndex].ReadOnly) _grid.BeginEdit(true);
            };
            _grid.CellClick += (s, e) =>
            {
                // Cột Đơn vị: click 1 lần là xổ danh sách (khi không giữ Shift / Ctrl để chọn nhiều dòng)
                if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "colUnit" || ModifierKeys != Keys.None) return;
                _grid.BeginEdit(true);
                if (_grid.EditingControl is ComboBox cbo) cbo.DroppedDown = true;
            };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), WrapContents = false };
            bottom.Controls.Add(new Label { Text = "Bộ mẫu:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            _cboSet = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboSet.SelectionChangeCommitted += (s, e) => SwitchSet(_cboSet.SelectedItem?.ToString());
            bottom.Controls.Add(_cboSet);
            bottom.Controls.Add(MakeButton("Bộ mới...", (s, e) => Action_NewSet(), 75));
            bottom.Controls.Add(MakeButton("Xoá bộ...", (s, e) => Action_DeleteSet(), 75));
            bottom.Controls.Add(MakeButton("Thêm từ bản vẽ", (s, e) => Action_AddFromDrawing(), 110));
            var btnDel = MakeButton("Xoá dòng chọn", (s, e) => Action_DeleteSelected(), 105);
            btnDel.ForeColor = Color.FromArgb(192, 57, 43);
            bottom.Controls.Add(btnDel);
            bottom.Controls.Add(MakeButton("Chèn vào bản vẽ", (s, e) => Action_InsertSelected(), 110));
            var btnSave = MakeButton("Lưu thông tin", (s, e) => Action_Save(), 110);
            btnSave.BackColor = Color.FromArgb(41, 128, 185);
            btnSave.ForeColor = Color.White;
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(MakeButton("Thoát", (s, e) => Close(), 80));

            _lblPath = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray, Padding = new Padding(8, 3, 0, 0), AutoEllipsis = true };

            Controls.Add(_grid);
            Controls.Add(_lblHeader);
            Controls.Add(_lblPath);
            Controls.Add(bottom);

            FormClosing += (s, e) =>
            {
                if (!ConfirmDiscard()) e.Cancel = true;
            };
        }

        private static DataGridViewButtonColumn MakeButtonColumn(string name, string text, Color color)
        {
            var col = new DataGridViewButtonColumn
            {
                Name = name,
                HeaderText = "",
                Text = text,
                UseColumnTextForButtonValue = true,
                Width = 30,
                FlatStyle = FlatStyle.Flat
            };
            col.DefaultCellStyle.ForeColor = color;
            return col;
        }

        private static Button MakeButton(string text, EventHandler onClick, int width)
        {
            var btn = new Button { Text = text, Width = width, Height = 30 };
            btn.Click += onClick;
            return btn;
        }

        // ============================== BỘ MẪU ==============================

        private void RefreshSetList(string select)
        {
            _cboSet.Items.Clear();
            foreach (var name in TemplateLibraryManager.ListSets()) _cboSet.Items.Add(name);
            if (!string.IsNullOrEmpty(select) && !_cboSet.Items.Contains(select)) _cboSet.Items.Add(select);
            _cboSet.SelectedItem = !string.IsNullOrEmpty(select) ? select : _cboSet.Items[0];
        }

        private void SwitchSet(string name)
        {
            if (string.IsNullOrEmpty(name) || name == _lib?.Name) return;
            if (!ConfirmDiscard())
            {
                _cboSet.SelectedItem = _lib.Name;
                return;
            }
            LoadSet(name);
        }

        private void LoadSet(string name)
        {
            _lib = TemplateLibraryManager.Load(name);
            _pendingBtrIds.Clear();
            _dirty = false;
            SettingsManager.Current.CurrentTemplateSet = _lib.Name;
            SettingsManager.SaveSettings();
            RefreshGrid();
            Logger.Log($"[TemplateLibraryDialog] Mở bộ '{_lib.Name}': {_lib.Entries.Count} block mẫu");
        }

        /// <summary>Có thay đổi chưa lưu: hỏi lưu. Trả false nếu user bấm Huỷ.</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var answer = MessageBox.Show(this, $"Bộ mẫu '{_lib.Name}' có thay đổi chưa lưu. Lưu trước khi đóng / chuyển bộ?",
                "Block mẫu", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return Action_Save();
            _dirty = false;
            return true;
        }

        private void Action_NewSet()
        {
            string name = PromptText("Bộ mẫu mới", "Tên bộ mẫu mới (vd Data2, Nha xuong):", "Data" + (_cboSet.Items.Count + 1));
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Tên bộ mẫu không hợp lệ (không dùng các ký tự \\ / : * ? \" < > |).", "Block mẫu");
                return;
            }
            if (!ConfirmDiscard()) return;
            if (!File.Exists(TemplateLibraryManager.JsonPath(name)))
                TemplateLibraryManager.Save(new TemplateLibrary { Name = name });
            RefreshSetList(name);
            LoadSet(name);
        }

        /// <summary>Xoá cả bộ mẫu đang mở (file .json + .dwg, cả bản dự phòng). Không lấy lại được.</summary>
        private void Action_DeleteSet()
        {
            string name = _lib.Name;
            var answer = MessageBox.Show(this,
                $"Xoá HẲN bộ mẫu '{name}' ({_lib.Entries.Count} block mẫu)?\n\nXoá file {name}.json và {name}.dwg trong:\n" +
                $"{TemplateLibraryManager.Folder}\nvà bản dự phòng trong {TemplateLibraryManager.BackupFolder}\n\nKhông lấy lại được.",
                "Xoá bộ mẫu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                int files = TemplateLibraryManager.DeleteSet(name);
                _dirty = false;
                Logger.Log($"[TemplateLibraryDialog] Xoá bộ mẫu '{name}' ({_lib.Entries.Count} block, {files} file)");
                var remaining = TemplateLibraryManager.ListSets();
                RefreshSetList(remaining[0]);
                LoadSet(remaining[0]);
                // Form thống kê đọc lại danh sách bộ + áp bộ mới
                Saved?.Invoke(_lib.Name);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TemplateLibraryDialog] Xoá bộ '{name}'");
                MessageBox.Show(this, $"Xoá bộ mẫu lỗi: {ex.Message}\nXem log: {Logger.GetLogFilePath()}", "Block mẫu");
            }
        }

        // ============================== LƯỚI ==============================

        /// <summary>
        /// Xoá các dòng đang chọn (Shift / Ctrl + click chọn nhiều, phím Delete hoặc nút "Xoá dòng chọn").
        /// Chỉ xoá trong danh sách đang mở; bấm "Lưu thông tin" mới ghi file, Thoát > No là lấy lại.
        /// </summary>
        private void Action_DeleteSelected()
        {
            var indices = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToList();
            if (indices.Count == 0 && _grid.CurrentRow != null) indices.Add(_grid.CurrentRow.Index);
            indices = indices.Where(i => i >= 0 && i < _lib.Entries.Count).Distinct().OrderByDescending(i => i).ToList();
            if (indices.Count == 0)
            {
                MessageBox.Show(this, "Chọn dòng block mẫu cần xoá (Shift / Ctrl + click để chọn nhiều dòng).", "Block mẫu");
                return;
            }

            var names = indices.OrderBy(i => i).Select(i => _lib.Entries[i])
                               .Select(x => x.DisplayName + (string.IsNullOrEmpty(x.VisibilityState) ? "" : $" [{x.VisibilityState}]")).ToList();
            var answer = MessageBox.Show(this,
                $"Xoá {indices.Count} block mẫu khỏi bộ '{_lib.Name}'?\n\n" + string.Join("\n", names.Take(15)) +
                (names.Count > 15 ? $"\n... và {names.Count - 15} block khác" : "") +
                "\n\nBấm 'Lưu thông tin' để ghi vào file. Lỡ tay: bấm Thoát > No để bỏ thay đổi.",
                "Xoá block mẫu", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            foreach (int i in indices)
            {
                var entry = _lib.Entries[i];
                _lib.Entries.RemoveAt(i);
                Logger.Log($"[TemplateLibraryDialog] Xoá block mẫu '{entry.BlockName}' ({entry.VisibilityState}) [chọn nhiều]");
            }
            MarkDirty();
            RefreshGrid(Math.Min(indices.Last(), _lib.Entries.Count - 1));
        }

        private void RefreshGrid(int selectIndex = -1)
        {
            _loading = true;
            try
            {
                var colUnit = (DataGridViewComboBoxColumn)_grid.Columns["colUnit"];
                colUnit.Items.Clear();
                foreach (var u in SettingsManager.Current.Units.Concat(_lib.Entries.Select(e => e.Unit))
                                                   .Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
                    colUnit.Items.Add(u);

                foreach (DataGridViewRow row in _grid.Rows)
                    if (row.Cells["colSym"].Value is Image old) old.Dispose();
                _grid.Rows.Clear();

                foreach (var e in _lib.Entries)
                {
                    int r = _grid.Rows.Add();
                    var row = _grid.Rows[r];
                    row.Cells["colSym"].Value = DecodeImage(e.ThumbnailBase64);
                    row.Cells["colBlock"].Value = e.BlockName;
                    row.Cells["colVis"].Value = e.VisibilityState;
                    row.Cells["colDisplay"].Value = e.DisplayName;
                    row.Cells["colKind"].Value = e.BlockKind;
                    row.Cells["colUnit"].Value = e.Unit;
                    row.Cells["colNote"].Value = e.Note;
                }
                if (selectIndex >= 0 && selectIndex < _grid.Rows.Count)
                {
                    _grid.ClearSelection();
                    _grid.Rows[selectIndex].Selected = true;
                    _grid.CurrentCell = _grid.Rows[selectIndex].Cells["colBlock"];
                }
            }
            finally
            {
                _loading = false;
            }
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            _lblHeader.Text = $"Danh sách block mẫu — bộ '{_lib.Name}': {_lib.Entries.Count} block" + (_dirty ? "  (chưa lưu)" : "") +
                              "      Shift / Ctrl + click chọn nhiều dòng, phím Delete xoá; double-click để sửa ô";
            _lblPath.Text = $"Lưu tại: {TemplateLibraryManager.Folder}   —   mang cả thư mục add-in sang máy khác là có đủ block mẫu";
        }

        private static Image DecodeImage(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch
            {
                return null;
            }
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _lib.Entries.Count) return;
            string col = _grid.Columns[e.ColumnIndex].Name;
            int i = e.RowIndex;
            if (col == "colDel")
            {
                var entry = _lib.Entries[i];
                _lib.Entries.RemoveAt(i);
                Logger.Log($"[TemplateLibraryDialog] Xoá block mẫu '{entry.BlockName}' ({entry.VisibilityState})");
                MarkDirty();
                RefreshGrid(Math.Min(i, _lib.Entries.Count - 1));
            }
            else if ((col == "colUp" && i > 0) || (col == "colDown" && i < _lib.Entries.Count - 1))
            {
                int j = col == "colUp" ? i - 1 : i + 1;
                (_lib.Entries[i], _lib.Entries[j]) = (_lib.Entries[j], _lib.Entries[i]);
                MarkDirty();
                RefreshGrid(j);
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0 || e.RowIndex >= _lib.Entries.Count) return;
            var entry = _lib.Entries[e.RowIndex];
            string value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
            switch (_grid.Columns[e.ColumnIndex].Name)
            {
                case "colDisplay": entry.DisplayName = value; break;
                case "colUnit": entry.Unit = value; break;
                case "colNote": entry.Note = value; break;
                default: return;
            }
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            UpdateHeader();
        }

        // ============================== THÊM / CHÈN / LƯU ==============================

        /// <summary>Quét chọn block trên bản vẽ -> thêm vào bộ mẫu (mỗi cặp tên block + chủng loại 1 dòng).</summary>
        private void Action_AddFromDrawing()
        {
            int added = 0, existed = 0, skipped = 0;
            var ownerForm = Owner;
            Hide();
            ownerForm?.Hide();
            try
            {
                using (_doc.LockDocument())
                {
                    var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                    var res = _doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nChọn các block cần thêm vào thư viện mẫu: " }, filter);
                    if (res.Status != PromptStatus.OK) return;

                    using (var tr = _doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var id in res.Value.GetObjectIds())
                        {
                            if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                            var def = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                            string name = def.Name;
                            if (string.IsNullOrEmpty(name) || name.StartsWith("*") || def.IsLayout || def.IsFromExternalReference)
                            {
                                skipped++;
                                continue;
                            }
                            string vis = BlockExtractor.ReadVisibility(br, name);
                            if (_lib.Entries.Any(x => string.Equals(x.BlockName, name, StringComparison.OrdinalIgnoreCase) &&
                                                      string.Equals(x.VisibilityState ?? "", vis, StringComparison.OrdinalIgnoreCase)))
                            {
                                existed++;
                                continue;
                            }

                            // Ảnh ký hiệu render từ BTR thực của block (đúng chủng loại), cùng cache với cột Ký hiệu của form
                            string cacheKey = string.IsNullOrEmpty(vis) ? name : $"{name}_{vis}";
                            string png = ThumbnailGenerator.GetOrCreateThumbnail(_doc, br.BlockTableRecord, cacheKey);
                            string b64 = !string.IsNullOrEmpty(png) && File.Exists(png) ? Convert.ToBase64String(File.ReadAllBytes(png)) : null;

                            _lib.Entries.Add(new TemplateEntry
                            {
                                BlockName = name,
                                VisibilityState = vis,
                                DisplayName = Regex.IsMatch(name, @"^A\$[A-Za-z]") && !string.IsNullOrEmpty(vis) ? vis : name,
                                BlockKind = br.IsDynamicBlock ? "Động" : def.HasAttributeDefinitions ? "Có thuộc tính" : "Tĩnh",
                                Unit = "Cái",
                                Note = "",
                                ThumbnailBase64 = b64,
                                InsertScale = Math.Abs(br.ScaleFactors.X)
                            });
                            if (!_pendingBtrIds.Contains(def.ObjectId)) _pendingBtrIds.Add(def.ObjectId);
                            added++;
                            Logger.Log($"[TemplateLibraryDialog] Thêm block mẫu '{name}' ({vis}), ảnh={(b64 != null ? "có" : "KHÔNG")}, tỉ lệ={Math.Abs(br.ScaleFactors.X):0.####}");
                        }
                        tr.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TemplateLibraryDialog] Thêm block mẫu từ bản vẽ");
                MessageBox.Show($"Thêm block mẫu lỗi: {ex.Message}\nXem log: {Logger.GetLogFilePath()}", "Block mẫu");
            }
            finally
            {
                ownerForm?.Show();
                Show();
                if (added > 0) _dirty = true;
                RefreshGrid(added > 0 ? _lib.Entries.Count - 1 : -1);
                Logger.Log($"[TemplateLibraryDialog] Thêm từ bản vẽ vào bộ '{_lib.Name}': mới {added}, đã có {existed}, bỏ {skipped}");
                if (added + existed + skipped > 0)
                    _doc.Editor.WriteMessage($"\n[LHB] Block mẫu: thêm {added}, đã có sẵn {existed}" + (skipped > 0 ? $", bỏ {skipped} (block ẩn danh / xref)" : "") +
                                             ". Bấm 'Lưu thông tin' để lưu.\n");
            }
        }

        /// <summary>Chèn block mẫu đang chọn vào bản vẽ (bản vẽ chưa có định nghĩa thì lấy từ file .dwg của thư viện).</summary>
        private void Action_InsertSelected()
        {
            int i = _grid.CurrentRow?.Index ?? -1;
            if (i < 0 || i >= _lib.Entries.Count)
            {
                MessageBox.Show(this, "Chọn 1 dòng block mẫu cần chèn.", "Block mẫu");
                return;
            }
            var entry = _lib.Entries[i];
            string error = null;
            var ownerForm = Owner;
            Hide();
            ownerForm?.Hide();
            try
            {
                using (_doc.LockDocument())
                {
                    var btrId = TemplateLibraryManager.ImportBlock(_doc, _lib.Name, entry.BlockName);
                    if (btrId.IsNull)
                    {
                        error = $"Không tìm thấy định nghĩa block '{entry.BlockName}' trong bản vẽ và trong file thư viện:\n" +
                                $"{TemplateLibraryManager.DwgPath(_lib.Name)}\n\nMở bản vẽ có block này, bấm 'Thêm từ bản vẽ' rồi 'Lưu thông tin' để lưu hình block.";
                        return;
                    }
                    var ppr = _doc.Editor.GetPoint($"\nChọn điểm chèn block mẫu '{entry.DisplayName}': ");
                    if (ppr.Status != PromptStatus.OK) return;
                    var ptWcs = ppr.Value.TransformBy(_doc.Editor.CurrentUserCoordinateSystem);
                    TemplateLibraryManager.InsertBlock(_doc, btrId, entry, ptWcs);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TemplateLibraryDialog] Chèn block mẫu '{entry.BlockName}'");
                error = $"Chèn block mẫu lỗi: {ex.Message}\nXem log: {Logger.GetLogFilePath()}";
            }
            finally
            {
                ownerForm?.Show();
                Show();
            }
            if (error != null) MessageBox.Show(this, error, "Block mẫu");
        }

        /// <summary>Lưu file .json (thông tin) và chép định nghĩa block mới thêm vào file .dwg. Trả false nếu lỗi.</summary>
        private bool Action_Save()
        {
            _grid.EndEdit();
            try
            {
                TemplateLibraryManager.Save(_lib);
                int copied = _pendingBtrIds.Count > 0 ? TemplateLibraryManager.SaveBlockDefinitions(_doc, _lib.Name, _pendingBtrIds) : 0;
                _pendingBtrIds.Clear();

                var inDwg = TemplateLibraryManager.ListDwgBlocks(_lib.Name);
                var missing = _lib.Entries.Select(x => x.BlockName).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !inDwg.Contains(n)).ToList();
                _dirty = false;
                UpdateHeader();
                Logger.Log($"[TemplateLibraryDialog] Lưu bộ '{_lib.Name}': {_lib.Entries.Count} block mẫu, chép {copied} định nghĩa block, " +
                           $"file .dwg có {inDwg.Count} block" + (missing.Count > 0 ? $", THIẾU hình block: [{string.Join(", ", missing)}]" : ""));

                Saved?.Invoke(_lib.Name);
                MessageBox.Show(this,
                    $"Đã lưu bộ '{_lib.Name}': {_lib.Entries.Count} block mẫu.\nLưu tại: {TemplateLibraryManager.Folder}" +
                    (missing.Count > 0
                        ? $"\n\n{missing.Count} block chưa có hình block trong file .dwg của thư viện (vẫn thống kê được, chỉ chưa chèn được):\n" +
                          string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? "..." : "")
                        : ""),
                    "Block mẫu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[TemplateLibraryDialog] Lưu bộ '{_lib?.Name}'");
                MessageBox.Show(this, $"Lưu thư viện mẫu lỗi: {ex.Message}\nXem log: {Logger.GetLogFilePath()}", "Block mẫu");
                return false;
            }
        }

        private string PromptText(string title, string label, string defaultValue)
        {
            using (var dlg = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 110)
            })
            {
                var lbl = new Label { Text = label, Left = 10, Top = 10, Width = 340, AutoEllipsis = true };
                var txt = new TextBox { Text = defaultValue, Left = 10, Top = 34, Width = 340 };
                var ok = new Button { Text = "OK", Left = 190, Top = 70, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Huỷ", Left = 275, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }
    }
}

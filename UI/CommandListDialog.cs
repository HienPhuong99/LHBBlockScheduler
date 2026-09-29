using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LHBBlockScheduler.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Bảng danh sách lệnh của add-in (lệnh LHBLENH, mặc định gõ LHB): xem chức năng, chạy lệnh, đổi phím tắt.
    /// Mỗi lệnh có thể có nhiều phím tắt, cách nhau dấu phẩy. Bấm "Lưu phím tắt" là dùng được ngay.
    /// </summary>
    public class CommandListDialog : Form
    {
        private static CommandListDialog _instance;

        private DataGridView _grid;
        private bool _dirty;
        private bool _loading;

        /// <summary>Mở bảng lệnh (chỉ 1 bảng; đang mở thì đưa lên trước).</summary>
        public static void ShowSingle()
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new CommandListDialog();
                AcApp.ShowModelessDialog(_instance);
            }
            else
            {
                if (!_instance.Visible) _instance.Show();
                _instance.Activate();
            }
        }

        private CommandListDialog()
        {
            BuildUi();
            RefreshGrid(CommandAliasManager.Current);
        }

        private void BuildUi()
        {
            Text = "Danh sách lệnh LHB";
            Width = 900;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = Color.FromArgb(41, 128, 185),
                ForeColor = Color.White,
                Padding = new Padding(8, 5, 0, 0),
                Text = "Gõ phím tắt vào cột Phím tắt (nhiều phím tắt cách nhau dấu phẩy), bấm Lưu phím tắt. Bấm Chạy hoặc double-click tên lệnh để chạy."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                RowTemplate = { Height = 28 },
                BackgroundColor = SystemColors.Window
            };
            UiKit.DoubleBuffer(_grid);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCmd", HeaderText = "Lệnh", Width = 120, ReadOnly = true });
            var colAlias = new DataGridViewTextBoxColumn { Name = "colAlias", HeaderText = "Phím tắt", Width = 110 };
            colAlias.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            colAlias.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 225);
            _grid.Columns.Add(colAlias);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDesc", HeaderText = "Chức năng", Width = 360, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStatus", HeaderText = "Trạng thái phím tắt", Width = 200, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewButtonColumn { Name = "colRun", HeaderText = "", Text = "Chạy", UseColumnTextForButtonValue = true, Width = 60 });
            _grid.EditingControlShowing += (s, e) =>
            {
                if (e.Control is TextBox tb) tb.CharacterCasing = CharacterCasing.Upper;
            };
            _grid.CellValueChanged += (s, e) =>
            {
                if (_loading || e.RowIndex < 0) return;
                _dirty = true;
                Text = "Danh sách lệnh LHB (chưa lưu)";
            };
            _grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "colRun") RunCommand(e.RowIndex);
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                string col = _grid.Columns[e.ColumnIndex].Name;
                if (col == "colCmd" || col == "colDesc") RunCommand(e.RowIndex);
            };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), WrapContents = false };
            var btnSave = MakeButton("Lưu phím tắt", (s, e) => Action_Save(), 110);
            btnSave.BackColor = Color.FromArgb(41, 128, 185);
            btnSave.ForeColor = Color.White;
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(MakeButton("Mặc định", (s, e) => Action_Defaults(), 90));
            bottom.Controls.Add(MakeButton("Đóng", (s, e) => Close(), 80));
            bottom.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Padding = new Padding(10, 8, 0, 0),
                Text = "Phím tắt không sửa acad.pgp, không che lệnh có sẵn của AutoCAD. Lưu trong settings.json."
            });

            Controls.Add(_grid);
            Controls.Add(header);
            Controls.Add(bottom);

            FormClosing += (s, e) =>
            {
                if (!ConfirmDiscard()) e.Cancel = true;
            };
        }

        private static Button MakeButton(string text, EventHandler onClick, int width)
        {
            var btn = new Button { Text = text, Width = width, Height = 30 };
            btn.Click += onClick;
            return btn;
        }

        private void RefreshGrid(List<CommandAlias> aliases)
        {
            _loading = true;
            try
            {
                _grid.Rows.Clear();
                foreach (var cmd in CommandAliasManager.AllCommands)
                {
                    var mine = aliases.Where(a => a.Command == cmd.Name).Select(a => a.Alias).ToList();
                    int r = _grid.Rows.Add(cmd.Name, string.Join(", ", mine), cmd.Description,
                        string.Join("; ", mine.Select(a => CommandAliasManager.Status.TryGetValue(a, out var st) ? $"{a}: {st}" : $"{a}: chưa lưu")));
                    var row = _grid.Rows[r];
                    row.Tag = cmd;
                    if (cmd.IsDebug)
                    {
                        row.Cells["colCmd"].Style.ForeColor = Color.Gray;
                        row.Cells["colDesc"].Style.ForeColor = Color.Gray;
                    }
                    if (mine.Any(a => CommandAliasManager.Status.TryGetValue(a, out var st) && st != "Đang dùng"))
                        row.Cells["colStatus"].Style.ForeColor = Color.FromArgb(192, 57, 43);
                }
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Đọc phím tắt trên lưới, kiểm tra, lưu settings.json và đăng ký lại. Trả false nếu có lỗi / user huỷ.</summary>
        private bool Action_Save()
        {
            _grid.EndEdit();
            var list = new List<CommandAlias>();
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            var pgpHits = new List<string>();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is LhbCommandInfo cmd)) continue;
                string text = row.Cells["colAlias"].Value?.ToString() ?? "";
                foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string alias = CommandAliasManager.Normalize(part);
                    if (!CommandAliasManager.IsValidName(alias))
                    {
                        errors.Add($"{cmd.Name}: '{part}' không hợp lệ (bắt đầu bằng chữ, chỉ gồm chữ A-Z, số, _ -)");
                        continue;
                    }
                    if (owner.TryGetValue(alias, out var other))
                    {
                        if (other != cmd.Name) errors.Add($"'{alias}' đặt cho 2 lệnh {other} và {cmd.Name}");
                        continue;
                    }
                    string conflict = CommandAliasManager.FindCommandConflict(alias);
                    if (conflict != null)
                    {
                        errors.Add($"{cmd.Name}: '{alias}' {conflict}");
                        continue;
                    }
                    string pgp = CommandAliasManager.FindPgpCommand(alias);
                    if (pgp != null) pgpHits.Add($"{alias} (đang là lệnh tắt của {pgp})");
                    owner[alias] = cmd.Name;
                    list.Add(new CommandAlias { Command = cmd.Name, Alias = alias });
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "Chưa lưu. Sửa các phím tắt sau:\n\n" + string.Join("\n", errors.Take(15)),
                    "Phím tắt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (pgpHits.Count > 0 &&
                MessageBox.Show(this, "Phím tắt trùng lệnh tắt trong acad.pgp của AutoCAD:\n\n" + string.Join("\n", pgpHits) +
                                      "\n\nDùng phím tắt này có thể che mất lệnh tắt cũ. Vẫn lưu?",
                    "Phím tắt", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;

            try
            {
                SettingsManager.Current.CommandAliases = list;
                SettingsManager.SaveSettings();
                int ok = CommandAliasManager.RegisterAll();
                _dirty = false;
                Text = "Danh sách lệnh LHB";
                RefreshGrid(CommandAliasManager.Current);
                Logger.Log($"[CommandListDialog] Lưu {list.Count} phím tắt, dùng được {ok}");
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[LHB] {CommandAliasManager.Summary()}\n");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[CommandListDialog] Lưu phím tắt");
                MessageBox.Show(this, $"Lưu phím tắt lỗi: {ex.Message}\nXem log: {Logger.GetLogFilePath()}", "Phím tắt");
                return false;
            }
        }

        /// <summary>Điền lại phím tắt mặc định lên lưới (bấm Lưu mới áp dụng).</summary>
        private void Action_Defaults()
        {
            RefreshGrid(CommandAliasManager.Defaults.ToList());
            _dirty = true;
            Text = "Danh sách lệnh LHB (chưa lưu)";
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var answer = MessageBox.Show(this, "Phím tắt có thay đổi chưa lưu. Lưu trước khi đóng?", "Phím tắt",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return Action_Save();
            _dirty = false;
            return true;
        }

        /// <summary>Đóng bảng rồi gửi lệnh vào dòng lệnh AutoCAD (form modeless không chạy lệnh trực tiếp được).</summary>
        private void RunCommand(int rowIndex)
        {
            if (!(_grid.Rows[rowIndex].Tag is LhbCommandInfo cmd) || cmd.Name == "LHBLENH") return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!ConfirmDiscard()) return;
            Logger.Log($"[CommandListDialog] Chạy lệnh {cmd.Name}");
            Close();
            doc.SendStringToExecute(cmd.Name + " ", true, false, false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.UI
{
    public class LegendTableMapDialog : Form
    {
        private readonly Document _doc;
        private readonly ObjectId _tableId;
        private readonly List<string[]> _rawRows = new List<string[]>();
        private readonly List<ObjectId[]> _rawBlockIds = new List<ObjectId[]>();
        private int _headerRowIndex = 0;

        private ComboBox[] _cboColumnMappings;
        private ComboBox _cboLibrary;
        private DataGridView _gridPreview;
        private Button _btnSave, _btnCancel;

        public LegendTableMapDialog(Document doc, ObjectId tableId)
        {
            _doc = doc;
            _tableId = tableId;

            Text = "Quét bảng Legend - Ánh xạ cột vào Thư viện thiết bị";
            Width = 850;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            // Font phải đặt TRƯỚC khi thêm control con để tiếng Việt hiển thị đúng
            Font = new System.Drawing.Font("Segoe UI", 9F);

            ReadTableData();
            BuildUi();
            GeneratePreview();
        }

        private void ReadTableData()
        {
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var tb = tr.GetObject(_tableId, OpenMode.ForRead) as Table;
                if (tb == null)
                {
                    Logger.Warn($"[LegendTableMapDialog.ReadTableData] ObjectId={_tableId} không phải là AutoCAD Table!");
                    return;
                }

                int rows = tb.Rows.Count;
                int cols = tb.Columns.Count;
                Logger.Log($"[LegendTableMapDialog.ReadTableData] Đọc dữ liệu Table: rows={rows}, cols={cols}");

                // Xác định dòng header (bỏ qua dòng Title nếu có)
                for (int r = 0; r < rows; r++)
                {
#pragma warning disable 618
                    var rowType = tb.RowType(r);
#pragma warning restore 618
                    if (rowType == RowType.HeaderRow || (rowType != RowType.TitleRow && _headerRowIndex == 0))
                    {
                        _headerRowIndex = r;
                        break;
                    }
                }
                Logger.Log($"[LegendTableMapDialog.ReadTableData] Dòng header tại index={_headerRowIndex}");

                for (int r = 0; r < rows; r++)
                {
                    var rowTexts = new string[cols];
                    var rowBlocks = new ObjectId[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        try
                        {
                            var cell = tb.Cells[r, c];
                            // TextString trả chuỗi MText thô ("Minh h\U+1ECDa") -> làm sạch trước khi dùng
                            string rawText = cell.TextString ?? "";
                            rowTexts[c] = VietnameseHelper.CleanMTextString(rawText);
                            if (rawText != rowTexts[c])
                                Logger.Log($"[LegendTableMapDialog] Ô [{r},{c}] làm sạch MText: '{rawText}' -> '{rowTexts[c]}'");
                            rowBlocks[c] = cell.BlockTableRecordId;
                        }
                        catch
                        {
                            rowTexts[c] = "";
                            rowBlocks[c] = ObjectId.Null;
                        }
                    }
                    _rawRows.Add(rowTexts);
                    _rawBlockIds.Add(rowBlocks);

                    string blocksStr = string.Join(", ", rowBlocks.Select(b => b.IsNull ? "Null" : b.Handle.ToString()));
                    Logger.Log($"[LegendTableMapDialog] Hàng {r}: texts=[{string.Join(" | ", rowTexts)}], blockIds=[{blocksStr}]");
                }
                tr.Commit();
            }
        }

        private void BuildUi()
        {
            int colCount = _rawRows.Count > 0 ? _rawRows[0].Length : 0;
            var headerRow = _rawRows.Count > _headerRowIndex ? _rawRows[_headerRowIndex] : new string[colCount];

            var topPanel = new GroupBox
            {
                Text = "1. Ánh xạ các cột của Bảng Legend",
                Dock = DockStyle.Top,
                Height = 120,
                Padding = new Padding(10)
            };

            var mappingFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            string[] mappingOptions = {
                "(Bỏ qua)",
                "STT",
                "Ký hiệu (Block)",
                "Tên thiết bị",
                "Chủng loại",
                "Đơn vị",
                "Số lượng",
                "Ghi chú"
            };

            _cboColumnMappings = new ComboBox[colCount];

            for (int c = 0; c < colCount; c++)
            {
                var colPanel = new Panel { Width = 150, Height = 65 };
                string headerName = c < headerRow.Length ? headerRow[c] : $"Cột {c + 1}";
                if (string.IsNullOrWhiteSpace(headerName)) headerName = $"Cột {c + 1}";

                var lbl = new Label { Text = headerName, Top = 2, Left = 2, Width = 145, AutoEllipsis = true };
                var cbo = new ComboBox { Top = 26, Left = 2, Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
                cbo.Items.AddRange(mappingOptions);

                // Gợi ý tự động dựa trên tiêu đề cột (so sánh không dấu: "Minh họa" == "minh hoa")
                string h = VietnameseHelper.RemoveDiacritics(headerName).ToLowerInvariant();
                bool colHasBlocks = ColumnHasBlocks(c);
                if (h.Contains("stt")) cbo.SelectedItem = "STT";
                else if (colHasBlocks || h.Contains("ky hieu") || h.Contains("hinh") || h.Contains("minh hoa")) cbo.SelectedItem = "Ký hiệu (Block)";
                else if (h.Contains("chung loai") || h.Contains("quy cach") || h.Contains("trang thai")) cbo.SelectedItem = "Chủng loại";
                else if (h.Contains("ten") || h.Contains("thiet bi")) cbo.SelectedItem = "Tên thiết bị";
                else if (h.Contains("don vi") || h.Contains("dvt")) cbo.SelectedItem = "Đơn vị";
                else if (h.Contains("so luong") || h == "sl") cbo.SelectedItem = "Số lượng";
                else if (h.Contains("ghi chu")) cbo.SelectedItem = "Ghi chú";
                else cbo.SelectedIndex = 0;
                Logger.Log($"[LegendTableMapDialog.BuildUi] Cột {c} header='{headerName}' colHasBlocks={colHasBlocks} -> gợi ý '{cbo.SelectedItem}'");

                cbo.SelectedIndexChanged += (s, e) => GeneratePreview();

                _cboColumnMappings[c] = cbo;
                colPanel.Controls.Add(lbl);
                colPanel.Controls.Add(cbo);
                mappingFlow.Controls.Add(colPanel);
            }
            topPanel.Controls.Add(mappingFlow);

            var midPanel = new GroupBox
            {
                Text = "2. Xem trước dữ liệu trích xuất",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            _gridPreview = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                RowTemplate = { Height = 48 }
            };

            _gridPreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "STT", Width = 50 });
            var colImg = new DataGridViewImageColumn { Name = "Thumb", HeaderText = "Ký hiệu", Width = 60, ImageLayout = DataGridViewImageCellLayout.Zoom };
            colImg.DefaultCellStyle.NullValue = null;
            _gridPreview.Columns.Add(colImg);
            _gridPreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Tên thiết bị", Width = 220 });
            _gridPreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "Visibility", HeaderText = "Chủng loại", Width = 150 });
            _gridPreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "Đơn vị", Width = 70 });
            _gridPreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Ghi chú", Width = 150 });

            midPanel.Controls.Add(_gridPreview);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };
            var lblLib = new Label { Text = "Lưu vào Thư viện:", Top = 14, Left = 10, Width = 110 };
            _cboLibrary = new ComboBox { Top = 10, Left = 125, Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var lib in DeviceLibraryManager.ListLibraries())
                _cboLibrary.Items.Add(lib);
            if (_cboLibrary.Items.Count > 0) _cboLibrary.SelectedIndex = 0;

            // Không gán DialogResult cho nút Lưu: nếu lưu lỗi thì dialog phải ở lại để người dùng thử lại
            _btnSave = new Button { Text = "Lưu vào Thư viện", Top = 8, Left = 670, Width = 140, Height = 32 };
            _btnSave.Click += Btn_Save_Click;
            _btnCancel = new Button { Text = "Đóng", Top = 8, Left = 570, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };

            bottomPanel.Controls.AddRange(new Control[] { lblLib, _cboLibrary, _btnSave, _btnCancel });

            Controls.Add(midPanel);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }

        /// <summary>Cột có chứa block ở ít nhất 1 dòng dữ liệu (sau header) -> nhiều khả năng là cột Ký hiệu.</summary>
        private bool ColumnHasBlocks(int col)
        {
            for (int r = _headerRowIndex + 1; r < _rawBlockIds.Count; r++)
            {
                var ids = _rawBlockIds[r];
                if (col < ids.Length && !ids[col].IsNull) return true;
            }
            return false;
        }

        private void GeneratePreview()
        {
            _gridPreview.Rows.Clear();

            int nameCol = -1, visCol = -1, unitCol = -1, noteCol = -1, sttCol = -1, symbolCol = -1;
            var mappingLogs = new List<string>();
            for (int c = 0; c < _cboColumnMappings.Length; c++)
            {
                string sel = _cboColumnMappings[c].SelectedItem?.ToString();
                mappingLogs.Add($"Cột {c} -> '{sel}'");
                if (sel == "Tên thiết bị") nameCol = c;
                else if (sel == "Chủng loại") visCol = c;
                else if (sel == "Đơn vị") unitCol = c;
                else if (sel == "Ghi chú") noteCol = c;
                else if (sel == "STT") sttCol = c;
                else if (sel == "Ký hiệu (Block)") symbolCol = c;
            }
            Logger.Log($"[LegendTableMapDialog.GeneratePreview] Ánh xạ cột: {string.Join("; ", mappingLogs)}");

            int order = 1;
            for (int r = _headerRowIndex + 1; r < _rawRows.Count; r++)
            {
                var rowTexts = _rawRows[r];
                var rowBlocks = _rawBlockIds[r];

                string name = nameCol >= 0 && nameCol < rowTexts.Length ? rowTexts[nameCol] : "";
                string vis = visCol >= 0 && visCol < rowTexts.Length ? rowTexts[visCol] : "";
                string unit = unitCol >= 0 && unitCol < rowTexts.Length ? rowTexts[unitCol] : "Cái";
                string note = noteCol >= 0 && noteCol < rowTexts.Length ? rowTexts[noteCol] : "";
                string sttStr = sttCol >= 0 && sttCol < rowTexts.Length ? rowTexts[sttCol] : order.ToString();

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(vis)) continue;

                int rowIdx = _gridPreview.Rows.Add();
                var gridRow = _gridPreview.Rows[rowIdx];
                gridRow.Cells["Order"].Value = sttStr;
                gridRow.Cells["Name"].Value = name;
                gridRow.Cells["Visibility"].Value = vis;
                gridRow.Cells["Unit"].Value = string.IsNullOrWhiteSpace(unit) ? "Cái" : unit;
                gridRow.Cells["Note"].Value = note;

                ObjectId btrId = symbolCol >= 0 && symbolCol < rowBlocks.Length ? rowBlocks[symbolCol] : ObjectId.Null;
                gridRow.Tag = btrId;

                // Nếu có block, thử render thumbnail
                if (!btrId.IsNull && btrId.IsValid)
                {
                    try
                    {
                        var bmp = ThumbnailGenerator.RenderBtrToBitmap(_doc, btrId, 48, 48, Color.White);
                        if (bmp != null)
                        {
                            gridRow.Cells["Thumb"].Value = bmp;
                        }
                    }
                    catch { }
                }

                order++;
            }
        }

        /// <summary>
        /// Tìm block definition gốc của block trong ô Legend.
        /// - Ô chứa "block nhanh" (Ctrl+Shift+V, tên A$C...) bọc đúng 1 BlockReference -> lấy definition của
        ///   BlockReference bên trong (dynamic thì lấy DynamicBlockTableRecord) + visibility của nó.
        /// - Ngược lại -> chính block trong ô là definition.
        /// </summary>
        private ObjectId ResolveLegendDefinition(ObjectId cellBtrId, out string defName, out string innerVis)
        {
            defName = null;
            innerVis = null;
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var cellBtr = (BlockTableRecord)tr.GetObject(cellBtrId, OpenMode.ForRead);
                var ents = new List<Entity>();
                foreach (ObjectId id in cellBtr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent) ents.Add(ent);
                }

                ObjectId defId = cellBtrId;
                if (ents.Count == 1 && ents[0] is BlockReference inner)
                {
                    defId = inner.IsDynamicBlock ? inner.DynamicBlockTableRecord : inner.BlockTableRecord;
                    if (inner.IsDynamicBlock)
                    {
                        foreach (DynamicBlockReferenceProperty prop in inner.DynamicBlockReferencePropertyCollection)
                        {
                            if (prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                innerVis = prop.Value?.ToString();
                                break;
                            }
                        }
                    }
                }

                defName = ((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name;
                Logger.Log($"[LegendTableMapDialog.ResolveLegendDefinition] Ô block '{cellBtr.Name}' ({ents.Count} entity) -> block gốc '{defName}'" +
                           (innerVis != null ? $", visibility='{innerVis}'" : ""));
                tr.Commit();
                return defId;
            }
        }

        private void Btn_Save_Click(object sender, EventArgs e)
        {
            string libName = _cboLibrary.Text?.Trim();
            if (string.IsNullOrEmpty(libName)) libName = "default";

            int N = _gridPreview.Rows.Count;
            Logger.Log($"[LegendTableMapDialog.Btn_Save_Click] Số entry chuẩn bị lưu: N = {N} (thư viện mục tiêu: '{libName}')");

            if (N == 0)
            {
                Logger.Warn("[LegendTableMapDialog.Btn_Save_Click] N = 0: Không đọc được dòng nào từ bảng Legend (hoặc tất cả các dòng đều bị bỏ qua do rỗng). Không lưu!");
                MessageBox.Show("Không đọc được dòng nào từ bảng Legend! Vui lòng kiểm tra lại ánh xạ cột.", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var library = DeviceLibraryManager.LoadLibrary(libName);
            int addedCount = 0;

            foreach (DataGridViewRow row in _gridPreview.Rows)
            {
                string name = row.Cells["Name"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                string vis = row.Cells["Visibility"].Value?.ToString() ?? "";
                string unit = row.Cells["Unit"].Value?.ToString() ?? "Cái";
                string note = row.Cells["Note"].Value?.ToString() ?? "";
                int order = int.TryParse(row.Cells["Order"].Value?.ToString(), out int o) ? o : library.Entries.Count + 1;

                ObjectId btrId = row.Tag is ObjectId id ? id : ObjectId.Null;
                ulong hash = 0;
                string base64Thumb = null;
                string defName = null;

                if (!btrId.IsNull && btrId.IsValid)
                {
                    // 1. Ảnh hiển thị: render đúng block trong ô Legend (hình người dùng nhìn thấy)
                    try
                    {
                        var bmp = ThumbnailGenerator.RenderBtrToBitmap(_doc, btrId, 96, 96, Color.White);
                        if (bmp != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                base64Thumb = Convert.ToBase64String(ms.ToArray());
                            }
                            bmp.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"LegendTableMapDialog: render block trong cell thất bại");
                    }

                    // 2. ShapeHash: PHẢI tính giống hệt BlockExtractor (definition gốc, cùng cache "<tên>_defhash"),
                    //    nếu không thì hash thư viện và hash lúc quét không bao giờ khớp.
                    try
                    {
                        ObjectId defId = ResolveLegendDefinition(btrId, out defName, out string innerVis);
                        if (!defId.IsNull && !string.IsNullOrEmpty(defName))
                        {
                            string defThumb = ThumbnailGenerator.GetOrCreateThumbnail(_doc, defId, $"{defName}_defhash");
                            if (!string.IsNullOrEmpty(defThumb))
                                hash = ThumbnailGenerator.GetHashFromPngFile(defThumb);
                        }
                        if (string.IsNullOrWhiteSpace(vis) && !string.IsNullOrWhiteSpace(innerVis))
                        {
                            Logger.Log($"[LegendTableMapDialog.Btn_Save_Click] '{name}': bảng Legend không ghi chủng loại, lấy từ block trong ô: '{innerVis}'");
                            vis = innerVis;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"LegendTableMapDialog: xác định block gốc của ô '{name}' thất bại");
                    }
                }
                Logger.Log($"[LegendTableMapDialog.Btn_Save_Click] '{name}' vis='{vis}': block gốc='{defName}', ShapeHash={hash}, ảnh={(base64Thumb != null ? "có" : "KHÔNG")}");

                // Cập nhật hoặc thêm mới vào thư viện
                var existing = library.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.StandardName, name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.VisibilityState, vis, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.Unit = unit;
                    existing.Note = note;
                    existing.Order = order;
                    if (hash != 0) existing.ShapeHash = hash;
                    if (!string.IsNullOrEmpty(base64Thumb)) existing.ThumbnailBase64 = base64Thumb;
                }
                else
                {
                    existing = new DeviceLibraryEntry
                    {
                        StandardName = name,
                        VisibilityState = vis,
                        Unit = unit,
                        Note = note,
                        Order = order,
                        ShapeHash = hash,
                        ThumbnailBase64 = base64Thumb,
                        KnownBlockNames = new List<string>()
                    };
                    library.Entries.Add(existing);
                }

                // Ghi nhớ tên block gốc -> khớp theo đường "KnownBlockNames + VisibilityState" kể cả khi hash lệch
                if (!string.IsNullOrEmpty(defName) && !defName.StartsWith("*"))
                {
                    if (existing.KnownBlockNames == null) existing.KnownBlockNames = new List<string>();
                    if (!existing.KnownBlockNames.Any(n => string.Equals(n, defName, StringComparison.OrdinalIgnoreCase)))
                        existing.KnownBlockNames.Add(defName);
                }
                addedCount++;
            }

            Logger.Log($"[LegendTableMapDialog.Btn_Save_Click] Đã chuẩn bị {addedCount} entries để ghi vào file thư viện '{libName}'.");

            try
            {
                DeviceLibraryManager.SaveLibrary(library);
                MessageBox.Show($"Đã lưu thành công {addedCount} thiết bị vào Thư viện '{libName}'.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[LegendTableMapDialog.Btn_Save_Click] Ghi thư viện '{libName}' thất bại");
                MessageBox.Show($"Lỗi khi lưu thư viện: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

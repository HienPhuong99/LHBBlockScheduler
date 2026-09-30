using System;
using System.Drawing;
using System.Windows.Forms;
using LHBBlockScheduler.Core;

namespace LHBBlockScheduler.UI
{
    /// <summary>
    /// Hộp thoại bản quyền Premium (lệnh LHBBANQUYEN, menu Premium > Bản quyền...). v9.5: mã LHB2 (bản quyền v2) - hiện
    /// loại key (theo máy / dùng chung / review), cấp cho ai, serial, hạn; mã máy theo bo mạch chủ; nút Xoá mã.
    /// </summary>
    public class LicenseDialog : Form
    {
        private readonly Label _lblStatus;
        private readonly Label _lblDetail;
        private readonly TextBox _txtKey;
        private readonly Button _btnRemove;

        public LicenseDialog(string message = null)
        {
            Text = "Bản quyền LHB Premium";
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            int y = 12;
            if (!string.IsNullOrEmpty(message))
            {
                Controls.Add(new Label { Text = message, Left = 12, Top = y, Width = 576, Height = 36, ForeColor = Color.FromArgb(192, 57, 43) });
                y += 40;
            }
            _lblStatus = new Label { Left = 12, Top = y, Width = 576, Height = 36, Font = new Font(Font, FontStyle.Bold) };
            Controls.Add(_lblStatus);
            y += 38;
            _lblDetail = new Label { Left = 12, Top = y, Width = 576, Height = 36, ForeColor = Color.DimGray };
            Controls.Add(_lblDetail);
            y += 42;

            Controls.Add(new Label { Text = "Mã máy (gửi mã này cho người cấp để nhận key THEO MÁY; key dùng chung / review không cần):", Left = 12, Top = y, Width = 576 });
            y += 22;
            var txtMachine = new TextBox { Left = 12, Top = y, Width = 440, ReadOnly = true, Text = LicenseManager.MachineCode, Font = new Font("Consolas", 11F) };
            var btnCopy = new Button { Text = "Chép mã máy", Left = 460, Top = y - 1, Width = 128, Height = 28 };
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(LicenseManager.MachineCode); btnCopy.Text = "Đã chép"; }
                catch (Exception ex) { Logger.Warn($"[LicenseDialog] Chép mã máy lỗi: {ex.Message}"); }
            };
            Controls.Add(txtMachine);
            Controls.Add(btnCopy);
            y += 30;
            Controls.Add(new Label { Text = "Nguồn mã máy: " + SourceText(LicenseManager.MachineSource), Left = 12, Top = y, Width = 576, ForeColor = Color.DimGray });
            y += 26;

            Controls.Add(new Label { Text = "Mã kích hoạt LHB2-... (dán nguyên chuỗi, xuống dòng / dấu cách không sao):", Left = 12, Top = y, Width = 576 });
            y += 22;
            _txtKey = new TextBox
            {
                Left = 12, Top = y, Width = 576, Height = 96, Multiline = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F), Text = SettingsManager.Current.LicenseKey ?? ""
            };
            Controls.Add(_txtKey);
            y += 106;

            _btnRemove = new Button { Text = "Xoá mã", Left = 12, Top = y, Width = 100, Height = 30 };
            _btnRemove.Click += (s, e) => Remove_Click();
            var btnActivate = new Button { Text = "Kích hoạt", Left = 380, Top = y, Width = 100, Height = 30, BackColor = Color.FromArgb(41, 128, 185), ForeColor = Color.White };
            btnActivate.Click += (s, e) => Activate_Click();
            var btnClose = new Button { Text = "Đóng", Left = 488, Top = y, Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(_btnRemove);
            Controls.Add(btnActivate);
            Controls.Add(btnClose);
            AcceptButton = btnActivate;
            CancelButton = btnClose;
            ClientSize = new Size(600, y + 44);

            UpdateStatus();
        }

        private static string SourceText(string source)
        {
            if (string.IsNullOrEmpty(source)) return "?";
            if (source.StartsWith("SMBIOS")) return "UUID bo mạch chủ (cài lại Windows không đổi mã)";
            if (source.StartsWith("MachineGuid")) return "MachineGuid của Windows (bo mạch không có UUID dùng được; cài lại Windows sẽ đổi mã)";
            return source + " (không đọc được thông tin máy - báo người cấp)";
        }

        private void UpdateStatus()
        {
            if (!LicenseManager.Enforced && string.IsNullOrWhiteSpace(_txtKey.Text))
                _txtKey.Text = "(Bản hiện tại miễn phí: không cần mã kích hoạt, mọi tính năng Premium đã mở)";
            var cur = LicenseManager.Current;
            _lblStatus.Text = LicenseManager.StatusText;
            _lblStatus.ForeColor = cur.Valid || !LicenseManager.Enforced ? Color.FromArgb(39, 174, 96)
                                 : LicenseManager.TrialDaysLeft > 0 ? Color.FromArgb(211, 84, 0) : Color.FromArgb(192, 57, 43);
            if (cur.Data != null && cur.Valid)
                _lblDetail.Text = $"Serial {cur.Data.SerialText} · cấp ngày {cur.Data.Issued:dd/MM/yyyy} · " +
                                  (cur.Data.Kind == LicenseKind.Machine ? "chỉ dùng trên máy này" : "key dùng chung - đừng gửi người ngoài nhóm");
            else if (!string.IsNullOrWhiteSpace(SettingsManager.Current.LicenseKey))
                _lblDetail.Text = "Mã đang lưu không dùng được: " + cur.Error;
            else
                _lblDetail.Text = "Chưa nhập mã kích hoạt.";
            _btnRemove.Enabled = !string.IsNullOrWhiteSpace(SettingsManager.Current.LicenseKey);
        }

        private void Activate_Click()
        {
            string text = _txtKey.Text.Trim();
            if (!LicenseManager.Enforced && !text.StartsWith(LicenseCodec.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Bản hiện tại miễn phí, không cần mã kích hoạt. Mọi tính năng Premium đã dùng được.",
                    "Kích hoạt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            var info = LicenseManager.Check(text);
            if (!info.Valid)
            {
                Logger.Log($"[LicenseDialog] Kích hoạt thất bại: {info.Error}");
                MessageBox.Show(this, info.Error, "Kích hoạt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LicenseManager.SaveKey(text);
            Logger.Log($"[LicenseDialog] Kích hoạt OK: serial {info.Data.SerialText}, {info.Data.KindText}, cấp cho '{info.Data.Label}', " +
                       $"hạn {(info.Expiry.HasValue ? info.Expiry.Value.ToString("dd/MM/yyyy") : "trọn đời")}");
            UpdateStatus();
            MessageBox.Show(this, LicenseManager.StatusText, "Kích hoạt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Remove_Click()
        {
            if (MessageBox.Show(this, "Xoá mã kích hoạt khỏi máy này? (quay về dùng thử / hết hạn dùng thử)", "Xoá mã",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LicenseManager.SaveKey(null);
            _txtKey.Text = "";
            UpdateStatus();
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;
using LHBBlockScheduler.Core;

namespace LHBBlockScheduler.UI
{
    /// <summary>Hộp thoại bản quyền Premium (lệnh LHBBANQUYEN): mã máy, trạng thái, nhập mã kích hoạt.</summary>
    public class LicenseDialog : Form
    {
        private readonly Label _lblStatus;
        private readonly TextBox _txtKey;

        public LicenseDialog(string message = null)
        {
            Text = "Bản quyền LHB Premium";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(560, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            int y = 12;
            if (!string.IsNullOrEmpty(message))
            {
                Controls.Add(new Label { Text = message, Left = 12, Top = y, Width = 536, Height = 36, ForeColor = Color.FromArgb(192, 57, 43) });
                y += 40;
            }
            _lblStatus = new Label { Left = 12, Top = y, Width = 536, Height = 22, Font = new Font(Font, FontStyle.Bold) };
            Controls.Add(_lblStatus);
            y += 30;

            Controls.Add(new Label { Text = "Mã máy (gửi mã này cho người bán để nhận mã kích hoạt):", Left = 12, Top = y, Width = 536 });
            y += 22;
            var txtMachine = new TextBox { Left = 12, Top = y, Width = 420, ReadOnly = true, Text = LicenseManager.MachineCode, Font = new Font("Consolas", 11F) };
            var btnCopy = new Button { Text = "Chép mã máy", Left = 440, Top = y - 1, Width = 108, Height = 28 };
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(LicenseManager.MachineCode); btnCopy.Text = "Đã chép"; }
                catch (Exception ex) { Logger.Warn($"[LicenseDialog] Chép mã máy lỗi: {ex.Message}"); }
            };
            Controls.Add(txtMachine);
            Controls.Add(btnCopy);
            y += 40;

            Controls.Add(new Label { Text = "Mã kích hoạt (dán vào đây):", Left = 12, Top = y, Width = 536 });
            y += 22;
            _txtKey = new TextBox { Left = 12, Top = y, Width = 536, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = SettingsManager.Current.LicenseKey ?? "" };
            Controls.Add(_txtKey);
            y += 100;

            var btnActivate = new Button { Text = "Kích hoạt", Left = 340, Top = y, Width = 100, Height = 30, BackColor = Color.FromArgb(41, 128, 185), ForeColor = Color.White };
            btnActivate.Click += (s, e) => Activate_Click();
            var btnClose = new Button { Text = "Đóng", Left = 448, Top = y, Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(btnActivate);
            Controls.Add(btnClose);
            CancelButton = btnClose;
            ClientSize = new Size(560, y + 44);

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            _lblStatus.Text = LicenseManager.StatusText;
            _lblStatus.ForeColor = LicenseManager.IsLicensed ? Color.FromArgb(39, 174, 96)
                                 : LicenseManager.TrialDaysLeft > 0 ? Color.FromArgb(211, 84, 0) : Color.FromArgb(192, 57, 43);
        }

        private void Activate_Click()
        {
            var info = LicenseManager.Check(_txtKey.Text);
            if (!info.Valid)
            {
                Logger.Log($"[LicenseDialog] Kích hoạt thất bại: {info.Error}");
                MessageBox.Show(this, info.Error, "Kích hoạt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SettingsManager.Current.LicenseKey = _txtKey.Text.Trim();
            SettingsManager.SaveSettings();
            Logger.Log($"[LicenseDialog] Kích hoạt OK, hạn {(info.Expiry.HasValue ? info.Expiry.Value.ToString("dd/MM/yyyy") : "vĩnh viễn")}");
            UpdateStatus();
            MessageBox.Show(this, LicenseManager.StatusText, "Kích hoạt", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

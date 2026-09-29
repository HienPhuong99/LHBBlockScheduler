using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Bản quyền Premium (P13). Mã máy = 10 byte đầu SHA-256 của MachineGuid Windows, hiện dạng XXXXX-XXXXX-XXXXX-XXXXX.
    /// Mã kích hoạt = "LHB1." + base64url( mã máy 10 byte | hạn dùng 2 byte (số ngày từ 01/01/2020, 0 = vĩnh viễn)
    /// | bản 1 byte | chữ ký RSA-SHA256 ). Chỉ người giữ khoá bí mật (LHB_private_key.xml, KHÔNG nằm trong mã nguồn)
    /// mới tạo được mã, bằng tools\LHBKeyGen. Chưa kích hoạt: dùng thử Premium 30 ngày tính từ lần dùng đầu.
    /// Tính năng bản thường (quét, xuất bảng, block mẫu...) không cần mã.
    /// </summary>
    public static class LicenseManager
    {
        public const int TrialDays = 30;
        private const string KeyPrefix = "LHB1.";
        private const string RegPath = @"Software\LHBBlockScheduler";
        private static readonly DateTime Epoch = new DateTime(2020, 1, 1);

        // Khoá công khai: chỉ kiểm tra được chữ ký, không tạo được mã
        private const string PublicKeyXml =
            "<RSAKeyValue><Modulus>4C2xpf9GFra5BUuzVo+aePdGJ1swysEKzoNLM1kHo11GgcXwUM5x5IMFoJFvHYJ9N2GBfNAHUgloLXxp6QemILzU9QhN/yUyyGvPDUqecDPAJ7EYGwY2sPspEcjbVWKkvrB1VETExIa9a2sKWwGBJjtQgH3W3Yu7kyQXR6M5HpU=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static bool _trialNoticeShown;

        /// <summary>Trạng thái đọc từ mã đã nhập.</summary>
        public class LicenseInfo
        {
            public bool Valid;
            public DateTime? Expiry; // null = vĩnh viễn
            public string Error;
        }

        private static byte[] _machineBytes;

        public static byte[] MachineBytes
        {
            get
            {
                if (_machineBytes != null) return _machineBytes;
                string guid = null;
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                        guid = k?.GetValue("MachineGuid") as string;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[License] Không đọc được MachineGuid: {ex.Message}");
                }
                if (string.IsNullOrEmpty(guid)) guid = Environment.MachineName + "|" + Environment.UserName;
                using (var sha = SHA256.Create())
                    _machineBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("LHB|" + guid.Trim().ToUpperInvariant())).Take(10).ToArray();
                return _machineBytes;
            }
        }

        /// <summary>Mã máy hiển thị cho khách gửi về để tạo mã kích hoạt.</summary>
        public static string MachineCode
        {
            get
            {
                string b32 = Base32(MachineBytes); // 16 ký tự
                return string.Join("-", Enumerable.Range(0, 4).Select(i => b32.Substring(i * 4, 4)));
            }
        }

        public static LicenseInfo Check(string key)
        {
            var info = new LicenseInfo();
            try
            {
                key = (key ?? "").Trim().Replace(" ", "").Replace("\r", "").Replace("\n", "");
                if (key.Length == 0) { info.Error = "Chưa nhập mã kích hoạt"; return info; }
                if (!key.StartsWith(KeyPrefix)) { info.Error = "Mã không đúng định dạng (phải bắt đầu bằng LHB1.)"; return info; }
                byte[] all = FromBase64Url(key.Substring(KeyPrefix.Length));
                if (all.Length < 14) { info.Error = "Mã quá ngắn"; return info; }
                byte[] payload = all.Take(13).ToArray();
                byte[] sig = all.Skip(13).ToArray();
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(PublicKeyXml);
                    if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), sig)) { info.Error = "Chữ ký mã không hợp lệ"; return info; }
                }
                if (!payload.Take(10).SequenceEqual(MachineBytes)) { info.Error = "Mã kích hoạt của máy khác"; return info; }
                int days = payload[10] | (payload[11] << 8);
                info.Expiry = days == 0 ? (DateTime?)null : Epoch.AddDays(days);
                if (info.Expiry.HasValue && DateTime.Today > info.Expiry.Value)
                {
                    info.Error = $"Mã đã hết hạn ngày {info.Expiry.Value:dd/MM/yyyy}";
                    return info;
                }
                info.Valid = true;
            }
            catch (Exception ex)
            {
                info.Error = "Mã không đọc được: " + ex.Message;
            }
            return info;
        }

        // Kiểm chữ ký RSA 1 lần cho mỗi mã / mỗi ngày (mỗi nút Premium đều hỏi trạng thái bản quyền)
        private static string _checkedKey;
        private static DateTime _checkedDay;
        private static LicenseInfo _checkedInfo;

        public static LicenseInfo Current
        {
            get
            {
                string key = SettingsManager.Current.LicenseKey ?? "";
                if (_checkedInfo == null || _checkedKey != key || _checkedDay != DateTime.Today)
                {
                    _checkedInfo = Check(key);
                    _checkedKey = key;
                    _checkedDay = DateTime.Today;
                }
                return _checkedInfo;
            }
        }

        /// <summary>
        /// false = CHƯA bắt bản quyền (yêu cầu user 29/09/2026: "để xài free, khi nào nói bắt bản quyền thì hãy tính"):
        /// Premium mở cho mọi máy, không cần mã, chưa ghi / chưa đếm ngày dùng thử. Mã kích hoạt vẫn nhập và kiểm được.
        /// Khi user bảo bắt bản quyền: đổi thành true rồi build bản mới -> dùng thử 30 ngày, hết hạn cần mã theo mã máy.
        /// (static readonly thay vì const để không có cảnh báo "code không chạy tới".)
        /// </summary>
        public static readonly bool Enforced = false;

        public static bool IsLicensed => !Enforced || Current.Valid;

        /// <summary>Ngày bắt đầu dùng thử (ghi registry lần đầu gọi).</summary>
        public static DateTime TrialStart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                    {
                        string s = k?.GetValue("PremiumTrialStart") as string;
                        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
                        k?.SetValue("PremiumTrialStart", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        Logger.Log("[License] Bắt đầu dùng thử Premium hôm nay");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[License] Không ghi được ngày dùng thử: {ex.Message}");
                }
                return DateTime.Today;
            }
        }

        /// <summary>Số ngày dùng thử còn lại. Chưa bắt bản quyền: không đụng registry (ngày dùng thử chưa bắt đầu).</summary>
        public static int TrialDaysLeft => !Enforced ? TrialDays : Math.Max(0, TrialDays - (int)(DateTime.Today - TrialStart).TotalDays);

        public static string StatusText
        {
            get
            {
                var c = Current;
                if (c.Valid) return c.Expiry.HasValue ? $"Đã kích hoạt Premium, hạn đến {c.Expiry.Value:dd/MM/yyyy}" : "Đã kích hoạt Premium vĩnh viễn";
                if (!Enforced) return "Premium miễn phí (chưa bật bản quyền, không cần mã)";
                int left = TrialDaysLeft;
                return left > 0 ? $"Dùng thử Premium: còn {left} ngày" : "Hết hạn dùng thử Premium";
            }
        }

        /// <summary>
        /// Gọi trước mỗi tính năng Premium. Đã kích hoạt / còn dùng thử -> true. Hết hạn -> mở hộp thoại kích hoạt,
        /// trả true nếu user nhập mã đúng.
        /// </summary>
        public static bool EnsurePremium(string feature)
        {
            if (IsLicensed) return true;
            int left = TrialDaysLeft;
            if (left > 0)
            {
                if (!_trialNoticeShown)
                {
                    _trialNoticeShown = true;
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage($"\n[LHB Premium] Đang dùng thử, còn {left} ngày. Gõ LHBBANQUYEN để kích hoạt.\n");
                }
                return true;
            }
            Logger.Log($"[License] '{feature}': hết hạn dùng thử, chưa kích hoạt");
            using (var dlg = new UI.LicenseDialog($"Tính năng Premium \"{feature}\" cần kích hoạt (đã hết {TrialDays} ngày dùng thử)."))
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(dlg);
            return IsLicensed;
        }

        // ============================== MÃ HOÁ ==============================

        private const string B32 = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // bỏ I, O, 0, 1 cho dễ đọc

        public static string Base32(byte[] data)
        {
            var sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(B32[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }
            if (bits > 0) sb.Append(B32[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }
}

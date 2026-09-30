using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Bản quyền Premium v2 (từ v9.5; v9 - v9.4 dùng RSA-1024 + MachineGuid, mã "LHB1." không còn nhận).
    ///  - Mã kích hoạt "LHB2-..." ký ECDSA P-256 (Core/LicenseCodec.cs). Add-in chỉ giữ khoá CÔNG KHAI (Core/LicensePolicy.cs):
    ///    kiểm được, không tạo được key. Nhiều khoá ký theo kid, danh sách serial thu hồi, bật / tắt nhận key review.
    ///  - 3 loại key: theo máy (mã máy = UUID bo mạch chủ, Core/MachineFingerprint.cs), dùng chung, review (dùng chung).
    ///  - Chưa có key: dùng thử Premium 30 ngày tính từ lần đầu nạp bản có bản quyền. Ngày dùng thử lưu 2 nơi
    ///    (registry + %LOCALAPPDATA%) có mã HMAC gắn mã máy, phát hiện sửa tay / lùi đồng hồ (TrialStore).
    ///  - Tính năng bản thường (quét, xuất bảng, block mẫu...) KHÔNG cần mã.
    /// Công cụ cấp key: tools\LHBKeyGen (hướng dẫn: docs/BAN_QUYEN_VA_CAP_KEY.md).
    /// </summary>
    public static class LicenseManager
    {
        public const int TrialDays = 30;

        /// <summary>
        /// true = bắt bản quyền (bật từ v9.5 theo yêu cầu user 30/09/2026 "làm bảo mật hơn + cấp key trọn đời cho đồng nghiệp").
        /// false = Premium mở cho mọi máy, không đếm ngày dùng thử (như v9.3 - v9.4).
        /// (static readonly thay vì const để không có cảnh báo "code không chạy tới".)
        /// </summary>
        public static readonly bool Enforced = true;

        private static bool _trialNoticeShown;

        /// <summary>Trạng thái mã đã nhập.</summary>
        public class LicenseInfo
        {
            public bool Valid;
            public DateTime? Expiry; // null = trọn đời
            public string Error;
            public LicenseData Data;
        }

        // ============================== MÃ MÁY ==============================

        private static byte[] _machineBytes;
        private static string _machineSource;

        /// <summary>10 byte mã máy (UUID bo mạch chủ; dự phòng MachineGuid). Tính 1 lần / phiên.</summary>
        public static byte[] MachineBytes
        {
            get
            {
                if (_machineBytes != null) return _machineBytes;
                try
                {
                    _machineBytes = MachineFingerprint.Compute(out _machineSource);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[License] Tính mã máy");
                    _machineSource = "lỗi";
                    using (var sha = SHA256.Create())
                    {
                        byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("LHB-HW-V2|NAME|" + Environment.MachineName.ToUpperInvariant()));
                        _machineBytes = new byte[LicenseCodec.MachineLength];
                        Buffer.BlockCopy(h, 0, _machineBytes, 0, _machineBytes.Length);
                    }
                }
                Logger.Log($"[License] Mã máy {LicenseCodec.FormatMachineCode(_machineBytes)} (nguồn: {_machineSource})");
                return _machineBytes;
            }
        }

        /// <summary>Nguồn của mã máy: SMBIOS-UUID / MachineGuid / MachineName (hiện trong LHBBANQUYEN, LHBDIAG).</summary>
        public static string MachineSource
        {
            get
            {
                var _ = MachineBytes;
                return _machineSource;
            }
        }

        /// <summary>Mã máy hiển thị cho khách gửi về để nhận key theo máy: XXXX-XXXX-XXXX-XXXX.</summary>
        public static string MachineCode => LicenseCodec.FormatMachineCode(MachineBytes);

        // ============================== KIỂM MÃ ==============================

        public static LicenseInfo Check(string key)
        {
            var info = new LicenseInfo();
            try
            {
                var r = LicensePolicy.Validate(key, MachineBytes, DateTime.Today);
                info.Valid = r.Valid;
                info.Error = r.Error;
                info.Data = r.Data;
                info.Expiry = r.Data?.Expiry;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    Logger.Log(r.Data == null
                        ? $"[License] Kiểm mã: {r.Error}"
                        : $"[License] Kiểm mã serial {r.Data.SerialText}, kid {r.Data.KeyId}, {r.Data.KindText}, cấp cho '{r.Data.Label}', " +
                          $"hạn {(r.Data.Expiry.HasValue ? r.Data.Expiry.Value.ToString("dd/MM/yyyy") : "trọn đời")}: " +
                          (r.Valid ? "HỢP LỆ" : "KHÔNG hợp lệ - " + r.Error));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[License] Kiểm mã kích hoạt");
                info.Valid = false;
                info.Error = "Mã không đọc được: " + ex.Message;
            }
            return info;
        }

        // Kiểm chữ ký 1 lần cho mỗi mã / mỗi ngày (mỗi nút Premium đều hỏi trạng thái bản quyền)
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

        public static bool IsLicensed => !Enforced || Current.Valid;

        // ============================== DÙNG THỬ ==============================

        /// <summary>Số ngày dùng thử còn lại (0 = hết / đồng hồ bị lùi / dữ liệu dùng thử bị sửa).</summary>
        public static int TrialDaysLeft => !Enforced ? TrialDays : TrialStore.DaysLeft(MachineBytes);

        /// <summary>Lý do dùng thử = 0 bất thường (lùi đồng hồ, sửa dữ liệu), null nếu bình thường.</summary>
        public static string TrialProblem => Enforced ? TrialStore.Problem : null;

        public static string StatusText
        {
            get
            {
                var c = Current;
                if (c.Valid && c.Data != null)
                {
                    string exp = c.Expiry.HasValue ? $"hạn đến {c.Expiry.Value:dd/MM/yyyy}" : "trọn đời";
                    return $"Đã kích hoạt Premium ({c.Data.KindText}, {exp}) - cấp cho {c.Data.Label}";
                }
                if (!Enforced) return "Premium miễn phí (chưa bật bản quyền, không cần mã)";
                int left = TrialDaysLeft;
                if (left > 0) return $"Dùng thử Premium: còn {left} ngày";
                return TrialProblem ?? "Hết hạn dùng thử Premium - cần mã kích hoạt (LHBBANQUYEN)";
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
            string why = TrialProblem ?? $"đã hết {TrialDays} ngày dùng thử";
            Logger.Log($"[License] '{feature}': chặn - {why}, chưa có mã hợp lệ ({Current.Error})");
            using (var dlg = new UI.LicenseDialog($"Tính năng Premium \"{feature}\" cần mã kích hoạt ({why})."))
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(dlg);
            return IsLicensed;
        }

        /// <summary>Lưu mã mới (đã kiểm hợp lệ) hoặc xoá mã (key = null) -> tính lại trạng thái.</summary>
        public static void SaveKey(string key)
        {
            SettingsManager.Current.LicenseKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            SettingsManager.SaveSettings();
            _checkedInfo = null;
            Logger.Log(key == null ? "[License] Đã xoá mã kích hoạt, về chế độ dùng thử" : "[License] Đã lưu mã kích hoạt mới");
        }

        /// <summary>
        /// Ngày dùng thử v2: đọc / ghi 2 nơi - registry HKCU\Software\LHBBlockScheduler (giá trị "P2") và file
        /// %LOCALAPPDATA%\LHBBlockScheduler\p2.dat. Quy tắc tính nằm ở Core/TrialLogic.cs (hàm thuần, có kiểm thử):
        /// xoá 1 nơi không reset được; sửa tay / chép từ máy khác -> hết hạn; lùi đồng hồ -> tạm khoá tới khi chỉnh lại giờ.
        /// Người rành máy vẫn xoá được cả 2 nơi (giới hạn của dùng thử offline) - xem docs/BAN_QUYEN_VA_CAP_KEY.md.
        /// Tên mới (v9 dùng "PremiumTrialStart") -> máy đã chạy v9 - v9.2 được đủ 30 ngày tính từ bản v9.5.
        /// </summary>
        private static class TrialStore
        {
            private const string RegPath = @"Software\LHBBlockScheduler";
            private const string RegValue = "P2";
            private static int _cacheDay = -1;
            private static int _cacheLeft;
            public static string Problem { get; private set; }

            private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                           "LHBBlockScheduler", "p2.dat");

            public static int DaysLeft(byte[] machine)
            {
                int today = TrialLogic.Today(DateTime.Today);
                if (_cacheDay == today) return _cacheLeft;
                _cacheDay = today;
                try
                {
                    byte[] reg = ReadRegistry(), file = ReadFile();
                    var o = TrialLogic.Evaluate(reg, file, machine, today, TrialDays);
                    Problem = o.Problem;
                    if (o.Started) Logger.Log($"[License] Bắt đầu dùng thử Premium {TrialDays} ngày từ hôm nay");
                    if (o.Tampered)
                        Logger.Warn($"[License] Dữ liệu dùng thử sai HMAC (registry={(reg != null ? "có" : "không")}, file={(file != null ? "có" : "không")}) -> hết hạn dùng thử");
                    if (o.ClockBack)
                        Logger.Warn($"[License] Phát hiện lùi đồng hồ: hôm nay {DateTime.Today:dd/MM/yyyy}, lần dùng gần nhất {LicenseCodec.Epoch.AddDays(o.Last):dd/MM/yyyy}");
                    if (o.WriteRegistry) WriteRegistry(o.Blob);
                    if (o.WriteFile) WriteFile(o.Blob);
                    if ((o.WriteRegistry || o.WriteFile) && !o.Started)
                        Logger.Log($"[License] Ghi lại dữ liệu dùng thử (registry={o.WriteRegistry}, file={o.WriteFile}), bắt đầu {LicenseCodec.Epoch.AddDays(o.Start):dd/MM/yyyy}, còn {o.DaysLeft} ngày");
                    _cacheLeft = o.DaysLeft;
                    return _cacheLeft;
                }
                catch (Exception ex)
                {
                    // Lỗi đọc / ghi (registry bị chặn...) -> không khoá oan người dùng: cho dùng thử hôm nay, ghi log
                    Logger.Error(ex, "[License] Đọc / ghi ngày dùng thử");
                    Problem = null;
                    _cacheLeft = 1;
                    return 1;
                }
            }

            private static byte[] ReadRegistry()
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
                        return k?.GetValue(RegValue) is string s && s.Length > 0 ? Convert.FromBase64String(s) : null;
                }
                catch (FormatException) { return new byte[0]; } // có nhưng hỏng -> tính là dữ liệu sai
            }

            private static void WriteRegistry(byte[] blob)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                    k?.SetValue(RegValue, Convert.ToBase64String(blob));
            }

            private static byte[] ReadFile()
            {
                string p = FilePath;
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            }

            private static void WriteFile(byte[] blob)
            {
                string p = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, blob);
            }
        }
    }
}

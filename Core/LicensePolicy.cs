using System;
using System.Collections.Generic;
using System.Linq;

namespace LHBBlockScheduler.Core
{
    /// <summary>Kết quả kiểm 1 mã kích hoạt theo chính sách của bản add-in này.</summary>
    public sealed class LicenseCheckResult
    {
        public bool Valid { get; set; }
        public string Error { get; set; }
        public LicenseData Data { get; set; }
    }

    /// <summary>
    /// Chính sách bản quyền biên dịch vào add-in (từ v9.5). DÙNG CHUNG với tools\LHBKeyGen để lệnh "verify" báo giống add-in.
    /// Thay đổi ở đây chỉ có hiệu lực với bản add-in build sau đó (bản cũ đã phát hành vẫn giữ chính sách cũ).
    /// </summary>
    public static class LicensePolicy
    {
        /// <summary>
        /// Khoá CÔNG KHAI theo kid: base64 của 64 byte (X | Y) ECDSA P-256 - chỉ kiểm được chữ ký, không tạo được key.
        /// Thêm khoá ký mới: "LHBKeyGen init --kid N" trên máy người bán, dán dòng "LHBKeyGen pubkey" in ra vào đây.
        /// Bỏ 1 kid = mọi key ký bằng kid đó hết hiệu lực ở bản build sau.
        /// </summary>
        public static readonly Dictionary<byte, string> TrustedKeys = new Dictionary<byte, string>
        {
            // kid 1: tạo 30/09/2026 trong phiên Claude Code (cloud). Khoá bí mật chỉ gửi riêng cho chủ sản phẩm.
            { 1, "XUtGwQ0oQm9DKQhDb5znyNqV8Mdpc2KyTqfM70L7T7NpIxOrpt9+Spyt247XBfn7NjwuKDw8yaBVjVNyEqx0Kw==" },
        };

        /// <summary>
        /// Serial các key đã thu hồi (hex 8 ký tự, "LHBKeyGen revoke" in sẵn danh sách). Key bị lộ / hoàn tiền -> thêm serial,
        /// phát hành bản mới. Bản cũ chạy offline vẫn nhận key đó (giới hạn của bản quyền offline).
        /// </summary>
        public static readonly HashSet<uint> RevokedSerials = new HashSet<uint>
        {
        };

        /// <summary>Bản này còn nhận key loại Review (dùng chung cho nhóm review). Khi bán chính thức có thể đặt false.</summary>
        public static readonly bool AcceptReviewKeys = true;

        /// <summary>
        /// Kiểm 1 mã theo đúng chính sách bản này: định dạng, khoá ký (kid) có trong add-in, chữ ký, thu hồi, loại review,
        /// mã máy (key theo máy), hạn, gói Premium. Hàm thuần (không đụng AutoCAD / registry): add-in và LHBKeyGen dùng chung.
        /// machine = 10 byte mã máy của máy đang kiểm (null = bỏ qua kiểm mã máy, chỉ dùng trong công cụ).
        /// </summary>
        public static LicenseCheckResult Validate(string key, byte[] machine, DateTime today)
        {
            var r = new LicenseCheckResult();
            var d = LicenseCodec.Parse(key, out string error);
            r.Data = d;
            if (d == null) { r.Error = error; return r; }
            if (!TrustedKeys.TryGetValue(d.KeyId, out string b64) || b64.StartsWith("@@"))
            {
                r.Error = $"Mã ký bằng khoá số {d.KeyId} mà bản add-in này chưa nhận - cập nhật add-in bản mới";
                return r;
            }
            if (!LicenseCodec.VerifySignature(d, Convert.FromBase64String(b64)))
            {
                r.Error = "Chữ ký mã không hợp lệ (mã bị sửa hoặc chép sai ký tự)";
                return r;
            }
            if (RevokedSerials.Contains(d.Serial)) { r.Error = $"Mã (serial {d.SerialText}) đã bị thu hồi - liên hệ người cấp"; return r; }
            if (d.Kind == LicenseKind.Review && !AcceptReviewKeys) { r.Error = "Mã bản review không còn dùng cho phiên bản này"; return r; }
            if (d.Kind == LicenseKind.Machine && machine != null && !d.Machine.SequenceEqual(machine))
            {
                r.Error = $"Mã kích hoạt của máy khác (mã máy trong key: {d.MachineCode}, máy này: {LicenseCodec.FormatMachineCode(machine)})";
                return r;
            }
            if (d.Expiry.HasValue && today.Date > d.Expiry.Value) { r.Error = $"Mã đã hết hạn ngày {d.Expiry.Value:dd/MM/yyyy}"; return r; }
            if ((d.Features & LicenseFeatures.Premium) == 0) { r.Error = "Mã không có gói Premium"; return r; }
            r.Valid = true;
            return r;
        }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Tính ngày dùng thử Premium v2 (từ v9.5) - hàm thuần, không đọc / ghi đâu cả (LicenseManager lo registry + file) ->
    /// kiểm thử được ngoài AutoCAD (tests/LHBLicenseTests).
    ///
    /// Dữ liệu dùng thử lưu 2 nơi (registry + file), mỗi nơi 17 byte:
    ///   phiên bản(1)=2 | ngày bắt đầu(2) | ngày dùng gần nhất(2) | 4 byte đầu mã máy | 8 byte HMAC-SHA256
    ///   (khoá HMAC = SHA-256("LHB-TRIAL-V2|" + mã máy) -> chép dữ liệu sang máy khác là sai HMAC). Ngày tính từ 01/01/2020.
    /// Quy tắc:
    ///  - Không có dữ liệu nào: bắt đầu dùng thử hôm nay.
    ///  - Có bản hợp lệ: ngày bắt đầu = SỚM NHẤT, ngày dùng gần nhất = MUỘN NHẤT; nơi thiếu / cũ thì ghi lại (xoá 1 nơi không reset).
    ///  - Có dữ liệu nhưng không bản nào hợp lệ (sửa tay, chép từ máy khác): coi như hết hạn và ghi lại bản "đã hết hạn".
    ///  - Hôm nay sớm hơn ngày dùng gần nhất quá 1 ngày (lùi đồng hồ): tạm 0 ngày, không ghi gì; chỉnh lại giờ là dùng tiếp.
    /// </summary>
    public static class TrialLogic
    {
        public const int BlobLength = 17;
        private const byte BlobVersion = 2;

        public sealed class Outcome
        {
            public int DaysLeft;
            public int Start;
            public int Last;
            /// <summary>Bản cần ghi (null = không ghi).</summary>
            public byte[] Blob;
            public bool WriteRegistry;
            public bool WriteFile;
            public bool Started;
            public bool Tampered;
            public bool ClockBack;
            /// <summary>Câu báo người dùng khi dùng thử = 0 bất thường (null = bình thường).</summary>
            public string Problem;
        }

        public static int Today(DateTime date) => (int)(date.Date - LicenseCodec.Epoch).TotalDays;

        public static Outcome Evaluate(byte[] registryBlob, byte[] fileBlob, byte[] machine, int today, int trialDays)
        {
            var o = new Outcome();
            byte[] macKey = MacKey(machine);
            bool regOk = TryDecode(registryBlob, macKey, machine, out int regStart, out int regLast);
            bool fileOk = TryDecode(fileBlob, macKey, machine, out int fileStart, out int fileLast);

            if (regOk || fileOk)
            {
                o.Start = Math.Min(regOk ? regStart : int.MaxValue, fileOk ? fileStart : int.MaxValue);
                o.Last = Math.Max(regOk ? regLast : 0, fileOk ? fileLast : 0);
            }
            else if (registryBlob != null || fileBlob != null)
            {
                o.Tampered = true;
                o.Start = today - trialDays;
                o.Last = today;
                o.Problem = "Dữ liệu dùng thử không hợp lệ (bị sửa hoặc chép từ máy khác) - cần mã kích hoạt";
            }
            else
            {
                o.Started = true;
                o.Start = o.Last = today;
            }

            if (today + 1 < o.Last)
            {
                o.ClockBack = true;
                o.DaysLeft = 0;
                o.Problem = $"Ngày giờ máy đang lùi về trước lần dùng gần nhất ({LicenseCodec.Epoch.AddDays(o.Last):dd/MM/yyyy}) - chỉnh lại ngày giờ để dùng thử tiếp";
                return o;
            }

            o.Last = Math.Max(o.Last, today);
            o.Start = Math.Max(1, o.Start);
            o.WriteRegistry = !regOk || regStart != o.Start || regLast != o.Last;
            o.WriteFile = !fileOk || fileStart != o.Start || fileLast != o.Last;
            if (o.WriteRegistry || o.WriteFile) o.Blob = Encode(o.Start, o.Last, machine);
            o.DaysLeft = Math.Max(0, trialDays - (today - o.Start));
            return o;
        }

        public static byte[] Encode(int start, int last, byte[] machine)
        {
            var b = new byte[BlobLength];
            b[0] = BlobVersion;
            b[1] = (byte)start; b[2] = (byte)(start >> 8);
            b[3] = (byte)last; b[4] = (byte)(last >> 8);
            Buffer.BlockCopy(machine, 0, b, 5, 4);
            using (var h = new HMACSHA256(MacKey(machine)))
                Buffer.BlockCopy(h.ComputeHash(b, 0, 9), 0, b, 9, 8);
            return b;
        }

        private static bool TryDecode(byte[] b, byte[] macKey, byte[] machine, out int start, out int last)
        {
            start = last = 0;
            if (b == null || b.Length != BlobLength || b[0] != BlobVersion) return false;
            byte[] mac;
            using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(b, 0, 9);
            int diff = 0;
            for (int i = 0; i < 8; i++) diff |= mac[i] ^ b[9 + i];
            for (int i = 0; i < 4; i++) diff |= machine[i] ^ b[5 + i];
            if (diff != 0) return false;
            start = b[1] | (b[2] << 8);
            last = b[3] | (b[4] << 8);
            return start > 0 && last >= start;
        }

        private static byte[] MacKey(byte[] machine)
        {
            byte[] prefix = Encoding.ASCII.GetBytes("LHB-TRIAL-V2|");
            var m = new byte[prefix.Length + machine.Length];
            Buffer.BlockCopy(prefix, 0, m, 0, prefix.Length);
            Buffer.BlockCopy(machine, 0, m, prefix.Length, machine.Length);
            using (var sha = SHA256.Create()) return sha.ComputeHash(m);
        }
    }
}

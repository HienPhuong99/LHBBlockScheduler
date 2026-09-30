using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>Loại mã kích hoạt (bản quyền v2, từ v9.5).</summary>
    public enum LicenseKind : byte
    {
        /// <summary>Theo máy: chỉ chạy trên đúng máy có mã máy ghi trong key (bán lẻ từng máy).</summary>
        Machine = 1,
        /// <summary>Dùng chung: không gắn máy (công ty / nhóm). Ai có key là dùng được -> chỉ gửi người tin cậy.</summary>
        Floating = 2,
        /// <summary>Bản review: dùng chung như Floating, hiện chữ "bản review"; bản sau có thể thôi nhận (LicensePolicy).</summary>
        Review = 3
    }

    /// <summary>Gói tính năng trong key (bit). Hiện chỉ có Premium; để chỗ cho gói Pro / Team sau này.</summary>
    [Flags]
    public enum LicenseFeatures : ushort
    {
        None = 0,
        Premium = 1
    }

    /// <summary>Nội dung 1 mã kích hoạt v2 đã giải mã.</summary>
    public sealed class LicenseData
    {
        public byte Version { get; set; } = LicenseCodec.FormatVersion;
        /// <summary>Số hiệu khoá ký (kid): add-in giữ khoá công khai theo kid -> đổi khoá ký mà key cũ vẫn chạy.</summary>
        public byte KeyId { get; set; }
        public LicenseKind Kind { get; set; } = LicenseKind.Machine;
        public byte Flags { get; set; }
        public LicenseFeatures Features { get; set; } = LicenseFeatures.Premium;
        /// <summary>Số serial ngẫu nhiên, dùng để thu hồi key và tra sổ key.</summary>
        public uint Serial { get; set; }
        public DateTime Issued { get; set; } = DateTime.Today;
        /// <summary>null = trọn đời.</summary>
        public DateTime? Expiry { get; set; }
        /// <summary>10 byte mã máy (key theo máy); toàn 0 với key dùng chung / review.</summary>
        public byte[] Machine { get; set; } = new byte[LicenseCodec.MachineLength];
        /// <summary>Số máy dự kiến (chỉ để ghi chú, key offline không đếm được máy).</summary>
        public byte Seats { get; set; }
        /// <summary>Cấp cho ai (tên khách / công ty / nhóm) - hiện trong LHBBANQUYEN.</summary>
        public string Label { get; set; } = "";
        public byte[] Payload { get; internal set; }
        public byte[] Signature { get; internal set; }

        public bool IsFloating => Kind != LicenseKind.Machine;
        public string SerialText => LicenseCodec.FormatSerial(Serial);
        public string MachineCode => IsFloating ? "" : LicenseCodec.FormatMachineCode(Machine);

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case LicenseKind.Machine: return "theo máy";
                    case LicenseKind.Floating: return "dùng chung";
                    case LicenseKind.Review: return "bản review (dùng chung)";
                    default: return "loại " + (int)Kind;
                }
            }
        }
    }

    /// <summary>
    /// Định dạng mã kích hoạt v2 - DÙNG CHUNG cho add-in (kiểm tra) và tools\LHBKeyGen (ký). Sửa file này phải build lại cả 2.
    ///
    ///   Key = "LHB2-" + Base32( payload | chữ ký ECDSA P-256 SHA-256 64 byte ), nhóm 5 ký tự bằng dấu "-".
    ///   payload (26 byte + nhãn): phiên bản(1)=2 | kid(1) | loại(1) | cờ(1) | tính năng(2) | serial(4) | ngày cấp(2)
    ///            | ngày hết hạn(2, 0 = trọn đời) | mã máy(10, toàn 0 = dùng chung) | số máy(1) | độ dài nhãn(1) | nhãn UTF-8
    ///   Chữ ký ký trên "LHB-LICENSE-V2|" + payload. Ngày = số ngày tính từ 01/01/2020. Số nguyên little-endian.
    ///
    /// Add-in chỉ có KHOÁ CÔNG KHAI (kiểm được, không tạo được key). Khoá bí mật chỉ nằm ở máy người bán / GitHub Secrets.
    /// Không tạo được key giả nếu không có khoá bí mật (ECDSA P-256 ~ 128 bit an toàn; RSA-1024 của v9 dưới chuẩn).
    /// </summary>
    public static class LicenseCodec
    {
        public const string Prefix = "LHB2-";
        /// <summary>Tiền tố sau khi bỏ dấu gạch (Parse bỏ mọi dấu gạch trước khi đọc).</summary>
        private const string PrefixCore = "LHB2";
        public const string LegacyPrefix = "LHB1.";
        public const byte FormatVersion = 2;
        public const int MachineLength = 10;
        public const int SignatureLength = 64;
        public const int PublicKeyLength = 64;
        public const int MaxLabelBytes = 48;
        private const int FixedLength = 26;
        public static readonly DateTime Epoch = new DateTime(2020, 1, 1);
        private static readonly byte[] Domain = Encoding.ASCII.GetBytes("LHB-LICENSE-V2|");

        // ============================== TẠO KEY (công cụ cấp key) ==============================

        public static byte[] BuildPayload(LicenseData d)
        {
            byte[] label = Encoding.UTF8.GetBytes((d.Label ?? "").Trim());
            if (label.Length > MaxLabelBytes)
                throw new ArgumentException($"Tên 'cấp cho' dài quá ({label.Length} byte UTF-8, tối đa {MaxLabelBytes}) - rút gọn lại");
            byte[] machine = d.Machine ?? new byte[MachineLength];
            if (machine.Length != MachineLength) throw new ArgumentException("Mã máy phải đúng 10 byte");
            if (d.Kind == LicenseKind.Machine && machine.All(b => b == 0)) throw new ArgumentException("Key theo máy cần mã máy");
            if (d.Kind != LicenseKind.Machine && machine.Any(b => b != 0)) throw new ArgumentException("Key dùng chung / review không ghi mã máy");
            if (d.Expiry.HasValue && d.Expiry.Value.Date < d.Issued.Date) throw new ArgumentException("Ngày hết hạn trước ngày cấp");

            var p = new byte[FixedLength + label.Length];
            int i = 0;
            p[i++] = FormatVersion;
            p[i++] = d.KeyId;
            p[i++] = (byte)d.Kind;
            p[i++] = d.Flags;
            WriteU16(p, ref i, (ushort)d.Features);
            WriteU32(p, ref i, d.Serial);
            WriteU16(p, ref i, ToDay(d.Issued));
            WriteU16(p, ref i, d.Expiry.HasValue ? ToDay(d.Expiry.Value) : (ushort)0);
            Buffer.BlockCopy(machine, 0, p, i, MachineLength);
            i += MachineLength;
            p[i++] = d.Seats;
            p[i++] = (byte)label.Length;
            Buffer.BlockCopy(label, 0, p, i, label.Length);
            return p;
        }

        /// <summary>Ký key bằng khoá bí mật (chỉ công cụ cấp key gọi). Trả chuỗi key "LHB2-...".</summary>
        public static string Sign(LicenseData d, ECDsa privateKey)
        {
            d.Payload = BuildPayload(d);
            d.Signature = privateKey.SignData(SignedMessage(d.Payload), HashAlgorithmName.SHA256);
            if (d.Signature.Length != SignatureLength) throw new CryptographicException("Chữ ký không đúng 64 byte (khoá ký phải là ECDSA P-256)");
            return Format(d.Payload, d.Signature);
        }

        public static string Format(byte[] payload, byte[] signature)
        {
            var all = new byte[payload.Length + signature.Length];
            Buffer.BlockCopy(payload, 0, all, 0, payload.Length);
            Buffer.BlockCopy(signature, 0, all, payload.Length, signature.Length);
            return Prefix + Group(Base32Encode(all), 5);
        }

        // ============================== ĐỌC + KIỂM KEY (add-in) ==============================

        /// <summary>
        /// Giải mã key (chưa kiểm chữ ký). Lỗi định dạng -> null + error tiếng Việt cho người dùng.
        /// Dán cả tin nhắn / cả file .txt chứa key cũng đọc được: bỏ khoảng trắng, xuống dòng, mọi kiểu gạch (Zalo, Word đổi "-"
        /// thành "–"), ký tự vô hình (U+200B, U+FEFF...) rồi thử từng chỗ có "LHB2". Số ký tự của key tính từ phần đầu key
        /// (độ dài nhãn) nên chữ dính liền sau key được bỏ qua.
        /// </summary>
        public static LicenseData Parse(string key, out string error)
        {
            error = null;
            string s = new string((key ?? "").Where(ch => !IsIgnorable(ch)).ToArray()).ToUpperInvariant();
            if (s.Length == 0) { error = "Chưa nhập mã kích hoạt"; return null; }

            string bestError = null;
            int bestRun = -1;
            for (int start = s.IndexOf(PrefixCore, StringComparison.Ordinal); start >= 0;
                 start = s.IndexOf(PrefixCore, start + 1, StringComparison.Ordinal))
            {
                int bodyStart = start + PrefixCore.Length, end = bodyStart;
                while (end < s.Length && Base32Alphabet.IndexOf(s[end]) >= 0) end++;
                var d = ParseBody(s.Substring(bodyStart, end - bodyStart), out string err);
                if (d != null) return d;
                // Báo lỗi của chỗ có nhiều ký tự nhất (chỗ gần giống key nhất), không phải chữ "LHB2" lẫn trong câu
                if (end - bodyStart > bestRun) { bestRun = end - bodyStart; bestError = err; }
            }
            if (bestRun > 0) { error = bestError; return null; }
            if (s.Contains(LegacyPrefix))
            {
                error = "Mã dạng LHB1 (bản v9 - v9.4) không còn dùng từ v9.5. Gửi mã máy MỚI (trong LHBBANQUYEN) để nhận mã LHB2.";
                return null;
            }
            error = bestRun == 0 ? "Mã bị thiếu ký tự (chép chưa hết?)" : "Mã không đúng định dạng (phải bắt đầu bằng LHB2-)";
            return null;
        }

        /// <summary>Đọc phần sau "LHB2" (chỉ gồm ký tự Base32, đã viết hoa). Ký tự thừa phía sau key bị bỏ qua.</summary>
        private static LicenseData ParseBody(string body, out string error)
        {
            error = null;
            // Đọc phần cố định trước để biết độ dài nhãn -> biết key dài bao nhiêu ký tự
            if (body.Length < CharsFor(FixedLength + SignatureLength)) { error = "Mã bị thiếu ký tự (chép chưa hết?)"; return null; }
            int labelLen = Base32DecodePrefix(body, FixedLength)[FixedLength - 1];
            int need = CharsFor(FixedLength + labelLen + SignatureLength);
            if (body.Length < need) { error = "Mã bị thiếu ký tự (chép chưa hết?)"; return null; }

            byte[] all;
            try
            {
                all = Base32Decode(body.Substring(0, need));
            }
            catch (FormatException)
            {
                // Bit đệm ở ký tự cuối sai: thường do chép thiếu ký tự cuối và chữ phía sau dính vào
                error = "Mã bị thiếu / thừa ký tự (chép chưa hết?)";
                return null;
            }
            if (all.Length != FixedLength + labelLen + SignatureLength) { error = "Mã bị thiếu / thừa ký tự (chép chưa hết?)"; return null; }
            if (all[0] != FormatVersion) { error = $"Mã phiên bản {all[0]} - add-in này chỉ đọc được phiên bản {FormatVersion}"; return null; }

            var d = new LicenseData();
            int i = 0;
            d.Version = all[i++];
            d.KeyId = all[i++];
            d.Kind = (LicenseKind)all[i++];
            d.Flags = all[i++];
            d.Features = (LicenseFeatures)ReadU16(all, ref i);
            d.Serial = ReadU32(all, ref i);
            d.Issued = FromDay(ReadU16(all, ref i));
            ushort exp = ReadU16(all, ref i);
            d.Expiry = exp == 0 ? (DateTime?)null : FromDay(exp);
            d.Machine = new byte[MachineLength];
            Buffer.BlockCopy(all, i, d.Machine, 0, MachineLength);
            i += MachineLength;
            d.Seats = all[i++];
            i++; // độ dài nhãn
            try
            {
                d.Label = new UTF8Encoding(false, true).GetString(all, i, labelLen);
            }
            catch (ArgumentException)
            {
                error = "Mã hỏng (tên cấp cho không đọc được)";
                return null;
            }
            i += labelLen;
            d.Payload = new byte[FixedLength + labelLen];
            Buffer.BlockCopy(all, 0, d.Payload, 0, d.Payload.Length);
            d.Signature = new byte[SignatureLength];
            Buffer.BlockCopy(all, i, d.Signature, 0, SignatureLength);
            if (!Enum.IsDefined(typeof(LicenseKind), d.Kind)) { error = $"Loại key {(int)d.Kind} không hỗ trợ - cập nhật add-in bản mới"; return null; }
            return d;
        }

        /// <summary>Kiểm chữ ký ECDSA P-256 bằng khoá công khai 64 byte (X | Y).</summary>
        public static bool VerifySignature(LicenseData d, byte[] publicKeyXY)
        {
            if (d?.Payload == null || d.Signature == null || publicKeyXY == null || publicKeyXY.Length != PublicKeyLength) return false;
            using (var ec = CreatePublicKey(publicKeyXY))
                return ec.VerifyData(SignedMessage(d.Payload), d.Signature, HashAlgorithmName.SHA256);
        }

        public static ECDsa CreatePublicKey(byte[] publicKeyXY)
        {
            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(publicKeyXY, 0, x, 0, 32);
            Buffer.BlockCopy(publicKeyXY, 32, y, 0, 32);
            return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        }

        /// <summary>Khoá công khai 64 byte (X | Y) của 1 khoá ECDSA P-256.</summary>
        public static byte[] ExportPublicKey(ECDsa key)
        {
            var q = key.ExportParameters(false).Q;
            if (q.X == null || q.Y == null || q.X.Length != 32 || q.Y.Length != 32) throw new CryptographicException("Khoá ký phải là ECDSA P-256");
            var r = new byte[64];
            Buffer.BlockCopy(q.X, 0, r, 0, 32);
            Buffer.BlockCopy(q.Y, 0, r, 32, 32);
            return r;
        }

        private static byte[] SignedMessage(byte[] payload)
        {
            var m = new byte[Domain.Length + payload.Length];
            Buffer.BlockCopy(Domain, 0, m, 0, Domain.Length);
            Buffer.BlockCopy(payload, 0, m, Domain.Length, payload.Length);
            return m;
        }

        // ============================== MÃ MÁY / SERIAL ==============================

        /// <summary>10 byte -> "XXXX-XXXX-XXXX-XXXX" (Base32 16 ký tự).</summary>
        public static string FormatMachineCode(byte[] machine) => Group(Base32Encode(machine ?? new byte[MachineLength]), 4);

        /// <summary>"XXXX-XXXX-XXXX-XXXX" -> 10 byte; sai định dạng -> null.</summary>
        public static byte[] ParseMachineCode(string code)
        {
            try
            {
                string s = new string((code ?? "").Where(ch => !IsIgnorable(ch)).ToArray()).ToUpperInvariant();
                if (s.Length != 16) return null;
                var b = Base32Decode(s);
                return b.Length == MachineLength ? b : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static string FormatSerial(uint serial) => serial.ToString("X8");

        public static bool TryParseSerial(string s, out uint serial) =>
            uint.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out serial);

        // ============================== BASE32 / NGÀY ==============================

        private const string LastCharError = "ký tự cuối không đúng";

        /// <summary>Ký tự bỏ qua khi đọc key / mã máy: khoảng trắng, dấu gạch các kiểu (Pd + dấu trừ U+2212), ký tự định dạng vô hình (Cf).</summary>
        private static bool IsIgnorable(char ch)
        {
            if (char.IsWhiteSpace(ch) || ch == '\u2212') return true;
            var cat = char.GetUnicodeCategory(ch);
            return cat == System.Globalization.UnicodeCategory.DashPunctuation || cat == System.Globalization.UnicodeCategory.Format;
        }

        /// <summary>Bảng Base32 bỏ I, O, 0, 1 (dễ đọc, giống mã máy v9).</summary>
        public const string Base32Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Số ký tự Base32 của n byte.</summary>
        private static int CharsFor(int bytes) => (bytes * 8 + 4) / 5;

        /// <summary>Giải mã count byte đầu của chuỗi Base32 hợp lệ (không kiểm ký tự cuối) - để đọc độ dài nhãn.</summary>
        private static byte[] Base32DecodePrefix(string s, int count)
        {
            var r = new byte[count];
            int buffer = 0, bits = 0, n = 0;
            foreach (char ch in s)
            {
                buffer = (buffer << 5) | Base32Alphabet.IndexOf(ch);
                bits += 5;
                if (bits >= 8)
                {
                    r[n++] = (byte)((buffer >> (bits - 8)) & 0xFF);
                    bits -= 8;
                    if (n == count) return r;
                }
                buffer &= (1 << bits) - 1;
            }
            throw new FormatException("thiếu ký tự");
        }

        public static string Base32Encode(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] Base32Decode(string s)
        {
            var bytes = new List<byte>(s.Length * 5 / 8 + 1);
            int buffer = 0, bits = 0;
            foreach (char ch in s)
            {
                int v = Base32Alphabet.IndexOf(char.ToUpperInvariant(ch));
                if (v < 0) throw new FormatException("ký tự '" + ch + "'");
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
                buffer &= (1 << bits) - 1;
            }
            // Chỉ nhận cách viết chuẩn: bit đệm ở ký tự cuối phải bằng 0, không thừa ký tự -> mỗi key đúng 1 chuỗi
            // (trước đây đổi ký tự cuối vẫn ra cùng dữ liệu, kiểm thử v9.5 phát hiện)
            if (bits >= 5 || buffer != 0) throw new FormatException(LastCharError);
            return bytes.ToArray();
        }

        private static string Group(string s, int size)
        {
            var sb = new StringBuilder(s.Length + s.Length / size);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && i % size == 0) sb.Append('-');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static ushort ToDay(DateTime d)
        {
            int days = (int)(d.Date - Epoch).TotalDays;
            if (days < 1 || days > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(d), "Ngày ngoài khoảng 2020 - 2199");
            return (ushort)days;
        }

        public static DateTime FromDay(ushort day) => Epoch.AddDays(day);

        private static void WriteU16(byte[] p, ref int i, ushort v) { p[i++] = (byte)v; p[i++] = (byte)(v >> 8); }
        private static void WriteU32(byte[] p, ref int i, uint v) { for (int k = 0; k < 4; k++) p[i++] = (byte)(v >> (8 * k)); }
        private static ushort ReadU16(byte[] p, ref int i) { ushort v = (ushort)(p[i] | (p[i + 1] << 8)); i += 2; return v; }
        private static uint ReadU32(byte[] p, ref int i) { uint v = (uint)(p[i] | (p[i + 1] << 8) | (p[i + 2] << 16) | (p[i + 3] << 24)); i += 4; return v; }
    }
}

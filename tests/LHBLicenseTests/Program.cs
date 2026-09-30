using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LHBBlockScheduler.Core;
using LHBKeyGen;

namespace LHBLicenseTests
{
    /// <summary>
    /// Kiểm thử bản quyền v2 (không cần AutoCAD, không cần thư viện test): định dạng key, chữ ký, chính sách của add-in,
    /// dùng thử, mã máy từ bảng SMBIOS, file khoá ký có mật khẩu. Thoát 0 = đạt hết.
    /// </summary>
    internal static class Program
    {
        private static int _pass, _fail;
        private const byte TestKid = 250;

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            var key = KeyFile.Generate(TestKid);
            LicensePolicy.TrustedKeys[TestKid] = key.PublicKeyBase64; // khoá thử, chỉ trong tiến trình kiểm thử
            var machine = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var other = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 };
            var today = new DateTime(2026, 9, 30);

            Section("Base32 / mã máy / serial");
            var rnd = new Random(7);
            for (int n = 0; n < 200; n++)
            {
                var b = new byte[rnd.Next(0, 140)];
                rnd.NextBytes(b);
                if (!LicenseCodec.Base32Decode(LicenseCodec.Base32Encode(b)).SequenceEqual(b)) { Fail($"Base32 {b.Length} byte"); goto doneB32; }
            }
            Pass("Base32 200 mẫu ngẫu nhiên");
            doneB32:
            string mc = LicenseCodec.FormatMachineCode(machine);
            Check(mc.Length == 19 && mc[4] == '-', "mã máy dạng XXXX-XXXX-XXXX-XXXX: " + mc);
            Check(LicenseCodec.ParseMachineCode(mc.ToLowerInvariant().Replace("-", " ")).SequenceEqual(machine), "đọc mã máy chữ thường / có dấu cách");
            Check(LicenseCodec.ParseMachineCode(mc.Replace('-', '\u2013') + "\u200B").SequenceEqual(machine), "đọc mã máy có dấu – và ký tự vô hình");
            Check(LicenseCodec.ParseMachineCode("ABCD-EFGH") == null, "mã máy thiếu ký tự -> null");
            Check(LicenseCodec.ParseMachineCode("ABCD-EFGH-IJKL-MNOP") == null, "mã máy có I/O (ngoài bảng) -> null");
            Check(LicenseCodec.TryParseSerial("64f68b85", out uint sr) && sr == 0x64F68B85, "serial hex");

            Section("Cấp + kiểm key");
            string kMachine = Issue(key, LicenseKind.Machine, machine, "Nguyễn Văn A - Cty PCCC Sài Gòn", null);
            string kReview = Issue(key, LicenseKind.Review, null, "Nhóm review", null);
            string kFloat = Issue(key, LicenseKind.Floating, null, "Công ty ABC", today.AddDays(365));
            string kExpired = Issue(key, LicenseKind.Floating, null, "Hết hạn", today.AddDays(-1), issued: today.AddDays(-10));
            Expect(kMachine, machine, today, true, "key theo máy đúng máy");
            Expect(kMachine, other, today, false, "key theo máy sai máy", "máy khác");
            Expect(kReview, other, today, true, "key review chạy mọi máy");
            Expect(kFloat, other, today, true, "key dùng chung còn hạn");
            Expect(kFloat, other, today.AddDays(366), false, "key dùng chung quá hạn", "hết hạn");
            Expect(kExpired, other, today, false, "key đã hết hạn", "hết hạn");
            Expect(Mess(kReview), other, today, true, "key dán lẫn xuống dòng, dấu cách, chữ thường");
            Expect(kReview.Replace('-', '\u2013'), other, today, true, "key có dấu gạch bị đổi thành – (Zalo / Word)");
            Expect(kReview.Replace("-", " "), other, today, true, "key dấu gạch thành dấu cách");
            Expect("\uFEFF" + kReview.Replace("-", "-\u200B") + "\u00A0", other, today, true, "key lẫn ký tự vô hình (U+200B, U+FEFF, NBSP)");
            Expect(kReview.Replace("-", ""), other, today, true, "key bỏ hết dấu gạch");
            Expect("Key của anh: " + kReview + " (trọn đời).", other, today, true, "dán cả câu nhắn có chữ trước / sau key");
            Expect(kReview + "CAM ON", other, today, true, "key dính chữ ở cuối (dán cả câu)");
            Expect("KEY LHB PREMIUM\r\nDùng cho: LHB Block Scheduler từ v9.5 Premium (mã LHB2)\r\n\r\nCách kích hoạt: dán vào LHBBANQUYEN\r\n\r\n"
                   + kReview + "\r\n\r\n- Không cần gửi mã máy.\r\n", other, today, true, "dán cả file KEY_REVIEW (có chữ LHB2 trước key, chữ sau key)");
            Expect("Dùng cho bản LHB2 nhé", other, today, false, "chỉ có chữ LHB2 trong câu, không có key", "thiếu");
            Expect("LHB1.AAAA", machine, today, false, "mã LHB1 cũ", "LHB1");
            Expect("", machine, today, false, "trống", "Chưa nhập");
            Expect(kReview.Substring(0, kReview.Length - 7), machine, today, false, "key chép thiếu", "thiếu");
            Expect(kReview.Replace("LHB2-", "LHB2-9"), machine, today, false, "chèn ký tự lạ", null);

            // Sửa bất kỳ ký tự nào (trừ dấu -) -> key hỏng (định dạng hoặc chữ ký)
            int tampered = 0, accepted = 0;
            string alpha = LicenseCodec.Base32Alphabet;
            for (int i = 5; i < kReview.Length; i++)
            {
                if (kReview[i] == '-') continue;
                char c = kReview[i];
                char repl = alpha[(alpha.IndexOf(c) + 7) % 32];
                string t = kReview.Substring(0, i) + repl + kReview.Substring(i + 1);
                tampered++;
                if (LicensePolicy.Validate(t, other, today).Valid) accepted++;
            }
            Check(accepted == 0, $"sửa 1 ký tự ở {tampered} vị trí: không vị trí nào còn hợp lệ");

            // Chuỗi ngẫu nhiên (có / không "LHB2", đủ mọi độ dài) -> không ném lỗi, không hợp lệ
            var fuzz = new Random(12345);
            int thrown = 0, fuzzValid = 0;
            const string junk = "LHB2-ABCDEFGHJKLMNPQRSTUVWXYZ23456789 .:–\u200Bđâyêôơư01IO";
            for (int n = 0; n < 3000; n++)
            {
                var sb = new StringBuilder(n % 3 == 0 ? "" : "LHB2-");
                int len = fuzz.Next(0, 700);
                for (int k = 0; k < len; k++)
                    sb.Append(n % 2 == 0 ? alpha[fuzz.Next(32)] : junk[fuzz.Next(junk.Length)]);
                try { if (LicensePolicy.Validate(sb.ToString(), other, today).Valid) fuzzValid++; }
                catch (Exception) { thrown++; }
            }
            Check(thrown == 0 && fuzzValid == 0, $"3000 chuỗi ngẫu nhiên: {thrown} lần ném lỗi, {fuzzValid} lần hợp lệ");

            // Khoá lạ (không có trong add-in) ký -> từ chối
            var rogue = KeyFile.Generate(TestKid);
            Expect(Issue(rogue, LicenseKind.Review, null, "Giả", null), other, today, false, "key ký bằng khoá lạ cùng kid", "Chữ ký");
            var rogue2 = KeyFile.Generate(77);
            Expect(Issue(rogue2, LicenseKind.Review, null, "Giả", null), other, today, false, "key ký bằng kid add-in chưa nhận", "chưa nhận");

            // Thu hồi
            var d = LicenseCodec.Parse(kFloat, out _);
            LicensePolicy.RevokedSerials.Add(d.Serial);
            Expect(kFloat, other, today, false, "key đã thu hồi", "thu hồi");
            LicensePolicy.RevokedSerials.Remove(d.Serial);

            // Nhãn dài quá / key theo máy thiếu mã máy -> không cấp
            Throws(() => Issue(key, LicenseKind.Review, null, new string('x', 49), null), "nhãn > 48 byte");
            Throws(() => Issue(key, LicenseKind.Machine, null, "A", null), "key theo máy thiếu mã máy");
            Throws(() => Issue(key, LicenseKind.Review, machine, "A", null), "key review có mã máy");

            Section("Key review THẬT (kid 1) nếu có biến LHB_TEST_REAL_KEY");
            string real = Environment.GetEnvironmentVariable("LHB_TEST_REAL_KEY");
            if (!string.IsNullOrEmpty(real))
            {
                var r = LicensePolicy.Validate(real, other, today);
                Check(r.Valid && r.Data.KeyId == 1, $"key thật hợp lệ với khoá kid 1 của add-in: {(r.Valid ? r.Data.SerialText + " " + r.Data.Label : r.Error)}");
            }
            else Console.WriteLine("  (bỏ qua)");

            Section("Dùng thử (TrialLogic)");
            int t0 = TrialLogic.Today(today);
            var o = TrialLogic.Evaluate(null, null, machine, t0, 30);
            Check(o.Started && o.DaysLeft == 30 && o.WriteRegistry && o.WriteFile, "máy mới: bắt đầu 30 ngày, ghi 2 nơi");
            byte[] blob = o.Blob;
            o = TrialLogic.Evaluate(blob, blob, machine, t0 + 10, 30);
            Check(o.DaysLeft == 20 && o.WriteRegistry && o.WriteFile && !o.Started, "sau 10 ngày còn 20, cập nhật ngày dùng gần nhất");
            byte[] blob10 = o.Blob;
            o = TrialLogic.Evaluate(null, blob10, machine, t0 + 10, 30);
            Check(o.DaysLeft == 20 && o.WriteRegistry && !o.WriteFile, "xoá registry: vẫn còn 20 ngày, tự ghi lại registry");
            o = TrialLogic.Evaluate(blob10, null, machine, t0 + 10, 30);
            Check(o.DaysLeft == 20 && !o.WriteRegistry && o.WriteFile, "xoá file: vẫn còn 20 ngày, tự ghi lại file");
            o = TrialLogic.Evaluate(TrialLogic.Encode(t0 + 5, t0 + 5, machine), blob10, machine, t0 + 10, 30);
            Check(o.Start == t0 && o.DaysLeft == 20, "2 nơi khác ngày bắt đầu: lấy ngày SỚM nhất");
            o = TrialLogic.Evaluate(blob10, blob10, machine, t0 + 30, 30);
            Check(o.DaysLeft == 0 && o.Problem == null, "ngày thứ 30: hết dùng thử");
            var bad = (byte[])blob10.Clone();
            bad[1] ^= 0x10; // sửa ngày bắt đầu
            o = TrialLogic.Evaluate(bad, bad, machine, t0 + 10, 30);
            Check(o.Tampered && o.DaysLeft == 0 && o.WriteRegistry, "sửa tay ngày bắt đầu: HMAC sai -> hết hạn, ghi bản hết hạn");
            o = TrialLogic.Evaluate(bad, blob10, machine, t0 + 10, 30);
            Check(!o.Tampered && o.DaysLeft == 20 && o.WriteRegistry, "1 nơi bị sửa, 1 nơi đúng: dùng bản đúng, ghi đè bản sửa");
            o = TrialLogic.Evaluate(blob10, blob10, other, t0 + 10, 30);
            Check(o.Tampered && o.DaysLeft == 0, "chép dữ liệu dùng thử sang máy khác: hết hạn");
            o = TrialLogic.Evaluate(blob10, blob10, machine, t0 + 5, 30);
            Check(o.ClockBack && o.DaysLeft == 0 && o.Blob == null, "lùi đồng hồ 5 ngày: tạm 0 ngày, không ghi");
            o = TrialLogic.Evaluate(blob10, blob10, machine, t0 + 9, 30);
            Check(!o.ClockBack && o.DaysLeft == 21, "lệch đồng hồ 1 ngày: vẫn cho dùng");
            o = TrialLogic.Evaluate(new byte[0], null, machine, t0, 30);
            Check(o.Tampered && o.DaysLeft == 0, "giá trị registry hỏng: coi như dữ liệu sai");

            Section("Mã máy từ bảng SMBIOS");
            var uuid = new byte[] { 0x4C, 0x4C, 0x45, 0x44, 0x00, 0x39, 0x31, 0x10, 0x80, 0x38, 0xB4, 0xC0, 0x4F, 0x53, 0x4E, 0x32 };
            Check(MachineFingerprint.FindUuid(Smbios(uuid)).SequenceEqual(uuid), "tìm UUID sau cấu trúc BIOS có chuỗi");
            Check(MachineFingerprint.FindUuid(Smbios(null)) == null, "không có cấu trúc loại 1 -> null");
            Check(MachineFingerprint.FindUuid(new byte[4]) == null, "dữ liệu ngắn -> null");
            Check(MachineFingerprint.IsUsableUuid(uuid), "UUID thật dùng được");
            Check(!MachineFingerprint.IsUsableUuid(new byte[16]), "UUID toàn 0 -> bỏ");
            Check(!MachineFingerprint.IsUsableUuid(Enumerable.Repeat((byte)0xFF, 16).ToArray()), "UUID toàn FF -> bỏ");
            Check(!MachineFingerprint.IsUsableUuid(new byte[] { 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 9 }), "UUID rác 03000200-0400-... -> bỏ");
            Check(!MachineFingerprint.IsUsableUuid(Enumerable.Range(1, 16).Select(x => (byte)x).ToArray()), "UUID dãy 01..10 -> bỏ");
            byte[] fp = MachineFingerprint.Compute(out string src);
            Check(fp.Length == 10, $"Compute chạy được trên máy này (nguồn {src})");

            Section("File khoá ký");
            var plain = KeyFile.Parse(key.ToText(null), null);
            Check(plain.PublicKeyBase64 == key.PublicKeyBase64 && !plain.Encrypted, "file khoá không mật khẩu: đọc lại đúng");
            string encText = key.ToText("mat-khau-dai-123");
            Check(!encText.Contains("\nd="), "file có mật khẩu không chứa khoá bí mật dạng rõ");
            var enc = KeyFile.Parse(encText, () => "mat-khau-dai-123");
            Check(enc.Encrypted && enc.PublicKeyBase64 == key.PublicKeyBase64, "file có mật khẩu: đọc lại đúng");
            Expect(Issue(enc, LicenseKind.Review, null, "Từ file có mật khẩu", null), other, today, true, "ký bằng khoá đọc từ file có mật khẩu");
            Throws(() => KeyFile.Parse(encText, () => "sai-mat-khau-00"), "sai mật khẩu -> báo lỗi");
            Throws(() => KeyFile.Parse(encText.Replace("iter=600000", "iter=600001"), () => "mat-khau-dai-123"), "sửa file khoá -> báo lỗi");
            var pubOnly = KeyFile.Parse(encText, null, publicOnly: true);
            Check(pubOnly.PublicKeyBase64 == key.PublicKeyBase64, "đọc khoá công khai không cần mật khẩu");

            Console.WriteLine();
            Console.WriteLine($"KẾT QUẢ: {_pass} đạt, {_fail} lỗi");
            return _fail == 0 ? 0 : 1;
        }

        private static string Issue(KeyFile key, LicenseKind kind, byte[] machine, string label, DateTime? expiry, DateTime? issued = null)
        {
            var d = new LicenseData
            {
                KeyId = key.KeyId,
                Kind = kind,
                Serial = (uint)new Random().Next(1, int.MaxValue),
                Issued = issued ?? new DateTime(2026, 9, 30),
                Expiry = expiry,
                Machine = machine ?? new byte[LicenseCodec.MachineLength],
                Label = label
            };
            using (var ec = key.CreateSigner()) return LicenseCodec.Sign(d, ec);
        }

        private static void Expect(string key, byte[] machine, DateTime today, bool valid, string name, string errorPart = null)
        {
            var r = LicensePolicy.Validate(key, machine, today);
            bool ok = r.Valid == valid && (valid || errorPart == null || (r.Error ?? "").IndexOf(errorPart, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(ok, $"{name}: {(r.Valid ? "hợp lệ" : r.Error)}");
        }

        /// <summary>Key bị dán lẫn xuống dòng, dấu cách, chữ thường.</summary>
        private static string Mess(string k)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < k.Length; i++)
            {
                sb.Append(i % 3 == 0 ? char.ToLowerInvariant(k[i]) : k[i]);
                if (i % 23 == 22) sb.Append("\r\n  ");
            }
            return " " + sb + " ";
        }

        /// <summary>Bảng RawSMBIOSData giả: cấu trúc loại 0 (BIOS, có chuỗi) + loại 1 (UUID) + loại 127.</summary>
        private static byte[] Smbios(byte[] uuid)
        {
            var t = new List<byte>();
            t.AddRange(new byte[] { 0, 0x18, 0, 0 });
            t.AddRange(new byte[0x18 - 4]);
            t.AddRange(Encoding.ASCII.GetBytes("American Megatrends\0v1.0\0\0"));
            if (uuid != null)
            {
                t.AddRange(new byte[] { 1, 0x1B, 1, 0, 1, 2, 3, 4 });
                t.AddRange(uuid);
                t.AddRange(new byte[] { 6, 5, 6 });
                t.AddRange(Encoding.ASCII.GetBytes("Dell Inc.\0OptiPlex\0\0"));
            }
            t.AddRange(new byte[] { 127, 4, 2, 0, 0, 0 });
            var buf = new List<byte> { 0, 3, 4, 0 };
            buf.AddRange(BitConverter.GetBytes(t.Count));
            buf.AddRange(t);
            return buf.ToArray();
        }

        private static void Section(string name) => Console.WriteLine("\n== " + name);

        private static void Check(bool ok, string name)
        {
            if (ok) Pass(name); else Fail(name);
        }

        private static void Pass(string name)
        {
            _pass++;
            Console.WriteLine("  ĐẠT  " + name);
        }

        private static void Fail(string name)
        {
            _fail++;
            Console.WriteLine("  LỖI  " + name);
        }

        private static void Throws(Action a, string name)
        {
            try
            {
                a();
                Fail(name + " (không báo lỗi)");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is CryptographicException || ex is FormatException)
            {
                Pass($"{name}: {ex.Message}");
            }
        }
    }
}

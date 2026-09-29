using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace LHBKeyGen
{
    /// <summary>
    /// Tạo mã kích hoạt LHB Premium cho 1 máy.
    ///   LHBKeyGen <MÃ MÁY> [số ngày dùng, 0 = vĩnh viễn] [đường dẫn khoá bí mật]
    /// Khoá bí mật mặc định: LHB_private_key.xml cạnh file exe. KHÔNG đưa khoá này lên GitHub / cho khách.
    /// Định dạng mã phải khớp Core/LicenseManager.cs của add-in.
    /// </summary>
    internal static class Program
    {
        private const string B32 = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly DateTime Epoch = new DateTime(2020, 1, 1);

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Cách dùng: LHBKeyGen <MÃ MÁY> [số ngày, 0 = vĩnh viễn] [file khoá bí mật]");
                Console.WriteLine("Ví dụ   : LHBKeyGen ABCD-EFGH-JKLM-NPQR 365");
                return 1;
            }
            string code = args[0].Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            int days = args.Length > 1 ? int.Parse(args[1]) : 0;
            string keyPath = args.Length > 2 ? args[2] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LHB_private_key.xml");
            if (!File.Exists(keyPath))
            {
                Console.WriteLine("Không thấy file khoá bí mật: " + keyPath);
                return 2;
            }

            byte[] machine = FromBase32(code);
            if (machine.Length < 10)
            {
                Console.WriteLine("Mã máy không hợp lệ (cần 16 ký tự, dạng XXXX-XXXX-XXXX-XXXX)");
                return 3;
            }
            int expiryDays = days <= 0 ? 0 : (int)(DateTime.Today.AddDays(days) - Epoch).TotalDays;
            var payload = machine.Take(10).Concat(new[] { (byte)(expiryDays & 0xFF), (byte)(expiryDays >> 8), (byte)1 }).ToArray();

            byte[] sig;
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(File.ReadAllText(keyPath));
                sig = rsa.SignData(payload, CryptoConfig.MapNameToOID("SHA256"));
            }
            string key = "LHB1." + Convert.ToBase64String(payload.Concat(sig).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Console.WriteLine(days <= 0 ? "Hạn dùng: vĩnh viễn" : "Hạn dùng: đến " + DateTime.Today.AddDays(days).ToString("dd/MM/yyyy"));
            Console.WriteLine("Mã kích hoạt:");
            Console.WriteLine(key);
            return 0;
        }

        private static byte[] FromBase32(string s)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            int buffer = 0, bits = 0;
            foreach (char ch in s)
            {
                int v = B32.IndexOf(ch);
                if (v < 0) throw new FormatException("Ký tự không hợp lệ trong mã máy: " + ch);
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }
            return bytes.ToArray();
        }
    }
}

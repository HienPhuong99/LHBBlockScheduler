using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LHBBlockScheduler.Core;

namespace LHBKeyGen
{
    /// <summary>
    /// Khoá KÝ (bí mật) ECDSA P-256 lưu dạng text - chạy giống nhau trên .NET Framework 4.8 và .NET 8:
    /// <code>
    ///   LHB-SIGNING-KEY v1
    ///   kid=1
    ///   curve=P-256
    ///   created=2026-09-30
    ///   x=&lt;base64&gt;  y=&lt;base64&gt;       (khoá công khai)
    ///   d=&lt;base64&gt;                       (khoá bí mật, file KHÔNG đặt mật khẩu)
    ///   hoặc có mật khẩu: enc=PBKDF2-SHA256/AES-256-CBC/HMAC-SHA256, iter, salt, iv, d_enc, mac
    /// </code>
    /// Ai có file này (và mật khẩu nếu có) là tạo được key: không đưa lên GitHub (trừ ô Secrets), không gửi ai.
    /// </summary>
    internal sealed class KeyFile
    {
        private const string Header = "LHB-SIGNING-KEY v1";
        private const string EncName = "PBKDF2-SHA256/AES-256-CBC/HMAC-SHA256";
        private const int Iterations = 600000;

        public byte KeyId { get; private set; }
        public DateTime Created { get; private set; }
        public byte[] X { get; private set; }
        public byte[] Y { get; private set; }
        private byte[] _d;
        public bool Encrypted { get; private set; }

        public byte[] PublicKey
        {
            get
            {
                var r = new byte[64];
                Buffer.BlockCopy(X, 0, r, 0, 32);
                Buffer.BlockCopy(Y, 0, r, 32, 32);
                return r;
            }
        }

        public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);

        /// <summary>Dòng C# dán vào Core/LicensePolicy.cs (TrustedKeys) để add-in nhận key ký bằng khoá này.</summary>
        public string PolicyLine => $"{{ {KeyId}, \"{PublicKeyBase64}\" }},";

        public static KeyFile Generate(byte kid)
        {
            using (var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var p = ec.ExportParameters(true);
                return new KeyFile { KeyId = kid, Created = DateTime.Today, X = Pad32(p.Q.X), Y = Pad32(p.Q.Y), _d = Pad32(p.D) };
            }
        }

        public ECDsa CreateSigner()
        {
            var ec = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = (byte[])_d.Clone(),
                Q = new ECPoint { X = (byte[])X.Clone(), Y = (byte[])Y.Clone() }
            });
            // Tự kiểm: ký thử + kiểm bằng khoá công khai -> file khoá hỏng / X,Y không khớp D thì báo ngay
            byte[] probe = Encoding.ASCII.GetBytes("LHB-SELFTEST");
            byte[] sig = ec.SignData(probe, HashAlgorithmName.SHA256);
            using (var pub = LicenseCodec.CreatePublicKey(PublicKey))
                if (!pub.VerifyData(probe, sig, HashAlgorithmName.SHA256))
                    throw new CryptographicException("File khoá ký hỏng: khoá bí mật không khớp khoá công khai");
            return ec;
        }

        // ============================== GHI / ĐỌC FILE ==============================

        public string ToText(string password)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine("# Khoá ký mã kích hoạt LHB Premium. BÍ MẬT: không gửi ai, không đưa lên GitHub (trừ ô Secrets).");
            sb.AppendLine("# Mất file này = không cấp được key mới cho các bản add-in đang nhận kid này. Sao lưu 2 nơi.");
            sb.AppendLine($"kid={KeyId}");
            sb.AppendLine("curve=P-256");
            sb.AppendLine("created=" + Created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.AppendLine("x=" + Convert.ToBase64String(X));
            sb.AppendLine("y=" + Convert.ToBase64String(Y));
            if (string.IsNullOrEmpty(password))
            {
                sb.AppendLine("d=" + Convert.ToBase64String(_d));
                return sb.ToString();
            }

            byte[] salt = Random(16), iv = Random(16);
            DeriveKeys(password, salt, Iterations, out byte[] encKey, out byte[] macKey);
            byte[] dEnc;
            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var t = aes.CreateEncryptor()) dEnc = t.TransformFinalBlock(_d, 0, _d.Length);
            }
            byte[] mac = Mac(macKey, salt, iv, dEnc);
            sb.AppendLine("enc=" + EncName);
            sb.AppendLine("iter=" + Iterations.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("salt=" + Convert.ToBase64String(salt));
            sb.AppendLine("iv=" + Convert.ToBase64String(iv));
            sb.AppendLine("d_enc=" + Convert.ToBase64String(dEnc));
            sb.AppendLine("mac=" + Convert.ToBase64String(mac));
            return sb.ToString();
        }

        /// <summary>
        /// Đọc file khoá. File có mật khẩu -> gọi getPassword() (biến môi trường / hỏi trên màn hình).
        /// publicOnly: chỉ đọc kid + khoá công khai, không cần mật khẩu (hiện menu, in dòng cho add-in).
        /// </summary>
        public static KeyFile Parse(string text, Func<string> getPassword, bool publicOnly = false)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith(Header, StringComparison.Ordinal))
                throw new FormatException("Không phải file khoá ký LHB (thiếu dòng '" + Header + "')");
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line == Header) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string Get(string k) => kv.TryGetValue(k, out var v) ? v : throw new FormatException("File khoá thiếu dòng '" + k + "='");

            if (!string.Equals(Get("curve"), "P-256", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Chỉ hỗ trợ khoá P-256");
            var f = new KeyFile
            {
                KeyId = byte.Parse(Get("kid"), CultureInfo.InvariantCulture),
                Created = DateTime.ParseExact(Get("created"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                X = Convert.FromBase64String(Get("x")),
                Y = Convert.FromBase64String(Get("y"))
            };
            if (f.X.Length != 32 || f.Y.Length != 32) throw new FormatException("Khoá công khai P-256 phải 32 byte mỗi phần");
            if (publicOnly) return f;
            if (kv.ContainsKey("d"))
            {
                f._d = Convert.FromBase64String(kv["d"]);
            }
            else
            {
                if (!string.Equals(Get("enc"), EncName, StringComparison.Ordinal)) throw new FormatException("Kiểu mã hoá khoá không hỗ trợ: " + Get("enc"));
                string password = getPassword?.Invoke();
                if (string.IsNullOrEmpty(password)) throw new CryptographicException("File khoá có mật khẩu - chưa nhập mật khẩu");
                int iter = int.Parse(Get("iter"), CultureInfo.InvariantCulture);
                byte[] salt = Convert.FromBase64String(Get("salt")), iv = Convert.FromBase64String(Get("iv"));
                byte[] dEnc = Convert.FromBase64String(Get("d_enc")), mac = Convert.FromBase64String(Get("mac"));
                DeriveKeys(password, salt, iter, out byte[] encKey, out byte[] macKey);
                if (!FixedEquals(mac, f.Mac(macKey, salt, iv, dEnc))) throw new CryptographicException("Sai mật khẩu khoá ký (hoặc file khoá bị sửa)");
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var t = aes.CreateDecryptor()) f._d = t.TransformFinalBlock(dEnc, 0, dEnc.Length);
                }
                f.Encrypted = true;
            }
            if (f.X.Length != 32 || f.Y.Length != 32 || f._d.Length != 32) throw new FormatException("Khoá P-256 phải 32 byte mỗi phần");
            return f;
        }

        public static KeyFile Load(string path, Func<string> getPassword) => Parse(File.ReadAllText(path), getPassword);

        /// <summary>Chỉ đọc kid + khoá công khai (không hỏi mật khẩu).</summary>
        public static KeyFile LoadPublic(string path) => Parse(File.ReadAllText(path), null, publicOnly: true);

        // ============================== TIỆN ÍCH ==============================

        private static void DeriveKeys(string password, byte[] salt, int iterations, out byte[] encKey, out byte[] macKey)
        {
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] k = kdf.GetBytes(64);
                encKey = new byte[32];
                macKey = new byte[32];
                Buffer.BlockCopy(k, 0, encKey, 0, 32);
                Buffer.BlockCopy(k, 32, macKey, 0, 32);
            }
        }

        private byte[] Mac(byte[] macKey, byte[] salt, byte[] iv, byte[] dEnc)
        {
            using (var h = new HMACSHA256(macKey))
            {
                var ms = new MemoryStream();
                ms.WriteByte(KeyId);
                foreach (var part in new[] { X, Y, salt, iv, dEnc }) ms.Write(part, 0, part.Length);
                return h.ComputeHash(ms.ToArray());
            }
        }

        private static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] Random(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        /// <summary>ExportParameters có thể bỏ byte 0 đầu -> đệm đủ 32 byte.</summary>
        private static byte[] Pad32(byte[] v)
        {
            if (v.Length == 32) return v;
            if (v.Length > 32) throw new CryptographicException("Giá trị khoá dài hơn 32 byte");
            var r = new byte[32];
            Buffer.BlockCopy(v, 0, r, 32 - v.Length, v.Length);
            return r;
        }
    }
}

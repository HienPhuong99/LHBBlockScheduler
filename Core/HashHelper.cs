using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Băm dùng chung (v9.4). Máy bật chính sách FIPS của Windows (hay gặp ở tập đoàn): MD5.Create() ném
    /// InvalidOperationException -> xuất bảng lỗi (tên block ký hiệu), LHBVERSION / LISP không đọc được MD5 của DLL.
    /// Nay: MD5 của hệ thống, bị chặn thì tự tính MD5 (RFC 1321, chỉ dùng để nhận diện file / đặt tên, không dùng bảo mật).
    /// </summary>
    public static class HashHelper
    {
        private static bool? _systemMd5Ok;

        public static byte[] Md5(byte[] data)
        {
            if (_systemMd5Ok != false)
            {
                try
                {
                    using (var md5 = MD5.Create())
                    {
                        var h = md5.ComputeHash(data);
                        _systemMd5Ok = true;
                        return h;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.Reflection.TargetInvocationException ||
                                           ex is CryptographicException)
                {
                    _systemMd5Ok = false;
                    Logger.Warn($"[HashHelper] MD5 của Windows bị chặn (chính sách FIPS?): {ex.Message} -> tự tính MD5");
                }
            }
            return ManagedMd5(data);
        }

        public static string Md5HexOfFile(string path)
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            return ToHex(Md5(bytes));
        }

        /// <summary>8 ký tự hex đầu MD5 của chuỗi (UTF-8) - đặt tên block ký hiệu, giữ đúng tên như các bản trước.</summary>
        public static string ShortHash(string s) => ToHex(Md5(Encoding.UTF8.GetBytes(s ?? ""))).Substring(0, 8);

        public static string ToHex(byte[] h) => BitConverter.ToString(h).Replace("-", "").ToUpperInvariant();

        // ============================== MD5 tự tính (RFC 1321) ==============================

        private static readonly int[] S =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
        };

        private static readonly uint[] K = BuildK();

        private static uint[] BuildK()
        {
            var k = new uint[64];
            for (int i = 0; i < 64; i++) k[i] = (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0);
            return k;
        }

        internal static byte[] ManagedMd5(byte[] message)
        {
            uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;

            // Đệm: 0x80, các byte 0, rồi độ dài (bit) 64-bit little-endian -> bội số 64 byte
            long bitLen = (long)message.Length * 8;
            int padLen = (int)((56 - (message.Length + 1) % 64 + 64) % 64);
            var data = new byte[message.Length + 1 + padLen + 8];
            Buffer.BlockCopy(message, 0, data, 0, message.Length);
            data[message.Length] = 0x80;
            for (int i = 0; i < 8; i++) data[data.Length - 8 + i] = (byte)(bitLen >> (8 * i));

            var m = new uint[16];
            for (int chunk = 0; chunk < data.Length; chunk += 64)
            {
                for (int i = 0; i < 16; i++) m[i] = BitConverter.ToUInt32(data, chunk + i * 4);
                uint a = a0, b = b0, c = c0, d = d0;
                for (int i = 0; i < 64; i++)
                {
                    uint f;
                    int g;
                    if (i < 16) { f = (b & c) | (~b & d); g = i; }
                    else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                    else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                    else { f = c ^ (b | ~d); g = (7 * i) % 16; }
                    f = f + a + K[i] + m[g];
                    a = d;
                    d = c;
                    c = b;
                    b += (f << S[i]) | (f >> (32 - S[i]));
                }
                a0 += a; b0 += b; c0 += c; d0 += d;
            }

            var result = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(a0), 0, result, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b0), 0, result, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(c0), 0, result, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(d0), 0, result, 12, 4);
            return result;
        }
    }
}

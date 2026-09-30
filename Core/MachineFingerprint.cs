using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Mã máy v2 (từ v9.5) - DÙNG CHUNG cho add-in và tools\LHBKeyGen (lệnh "machine").
    ///
    /// Nguồn ưu tiên: UUID bo mạch chủ trong SMBIOS (đọc bằng GetSystemFirmwareTable, không cần quyền admin, không cần
    /// WMI). UUID gắn với phần cứng: cài lại Windows KHÔNG đổi mã máy; sao chép (Ghost / clone) ổ đĩa sang máy khác
    /// thì mã máy KHÁC (v9 dùng MachineGuid của Windows: Ghost là trùng mã, cài lại Windows là đổi mã).
    /// Bo mạch báo UUID rác (toàn 0 / toàn F / dãy 00 02 00 03...) -> dùng MachineGuid; không đọc được nữa -> tên máy + user.
    /// Mã máy = 10 byte đầu SHA-256("LHB-HW-V2|nguồn|giá trị"), hiện dạng XXXX-XXXX-XXXX-XXXX.
    /// </summary>
    public static class MachineFingerprint
    {
        /// <summary>10 byte mã máy của máy đang chạy. source = "SMBIOS-UUID" / "MachineGuid" / "MachineName".</summary>
        public static byte[] Compute(out string source)
        {
            string material;
            byte[] uuid = ReadSmbiosUuid();
            if (uuid != null && IsUsableUuid(uuid))
            {
                source = "SMBIOS-UUID";
                material = "UUID|" + BitConverter.ToString(uuid).Replace("-", "");
            }
            else
            {
                string guid = ReadMachineGuid();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    source = uuid == null ? "MachineGuid" : "MachineGuid (UUID bo mạch không dùng được)";
                    material = "GUID|" + guid.Trim().ToUpperInvariant();
                }
                else
                {
                    source = "MachineName";
                    material = "NAME|" + Environment.MachineName.ToUpperInvariant() + "|" + Environment.UserName.ToUpperInvariant();
                }
            }
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("LHB-HW-V2|" + material));
                var r = new byte[LicenseCodec.MachineLength];
                Buffer.BlockCopy(h, 0, r, 0, r.Length);
                return r;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId,
                                                          [Out] byte[] firmwareTableBuffer, uint bufferSize);

        /// <summary>UUID trong bảng SMBIOS loại 1 (System Information), 16 byte thô; null nếu không đọc được.</summary>
        public static byte[] ReadSmbiosUuid()
        {
            try
            {
                const uint rsmb = 0x52534D42; // 'RSMB'
                uint size = GetSystemFirmwareTable(rsmb, 0, null, 0);
                if (size < 8 || size > (1 << 22)) return null;
                var buf = new byte[size];
                if (GetSystemFirmwareTable(rsmb, 0, buf, size) != size) return null;
                return FindUuid(buf);
            }
            catch (Exception)
            {
                // Không phải Windows / không có bảng SMBIOS (máy ảo lạ) -> dùng nguồn khác
                return null;
            }
        }

        /// <summary>
        /// Tìm UUID trong dữ liệu RawSMBIOSData (4 byte phiên bản + 4 byte độ dài bảng + các cấu trúc SMBIOS): cấu trúc loại 1,
        /// UUID ở byte 8..23. Tách riêng để kiểm thử được không cần Windows.
        /// </summary>
        public static byte[] FindUuid(byte[] buf)
        {
            if (buf == null || buf.Length < 8) return null;
            int tableLength = BitConverter.ToInt32(buf, 4);
            int end = (int)Math.Min((long)buf.Length, 8L + Math.Max(0, tableLength));
            int i = 8;
            while (i + 4 <= end)
            {
                byte type = buf[i];
                byte len = buf[i + 1];
                if (len < 4) break;
                if (type == 1 && len >= 0x19 && i + 0x18 <= end)
                {
                    var u = new byte[16];
                    Buffer.BlockCopy(buf, i + 8, u, 0, 16);
                    return u;
                }
                if (type == 127) break; // hết bảng
                // Sau vùng cố định là các chuỗi, kết thúc bằng 2 byte 0
                int j = i + len;
                while (j + 1 < end && !(buf[j] == 0 && buf[j + 1] == 0)) j++;
                i = j + 2;
            }
            return null;
        }

        /// <summary>UUID rác của bo mạch rẻ / máy ảo: toàn 1 giá trị, dãy tăng dần, quá ít byte khác nhau.</summary>
        public static bool IsUsableUuid(byte[] u)
        {
            if (u == null || u.Length != 16) return false;
            if (u.Distinct().Count() < 4) return false; // toàn 00 / toàn FF / gần như rỗng
            byte[] bogus1 = { 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09 }; // 03000200-0400-0500-0006-000700080009
            byte[] bogus2 = { 0x03, 0x00, 0x02, 0x00, 0x04, 0x00, 0x05, 0x00, 0x00, 0x06, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09 };
            if (u.SequenceEqual(bogus1) || u.SequenceEqual(bogus2)) return false;
            bool sequential = true; // 01 02 03 ... 10
            for (int k = 1; k < 16 && sequential; k++) sequential = u[k] == (byte)(u[0] + k);
            return !sequential;
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return k?.GetValue("MachineGuid") as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

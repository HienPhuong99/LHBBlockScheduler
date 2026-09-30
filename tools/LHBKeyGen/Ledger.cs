using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LHBKeyGen
{
    /// <summary>1 dòng sổ key (JSON Lines). Chứa thông tin khách -> sổ chỉ nằm ở máy người bán / nhánh riêng repo private.</summary>
    [DataContract]
    internal sealed class LedgerEntry
    {
        [DataMember(Order = 1)] public string Serial { get; set; }
        [DataMember(Order = 2)] public string IssuedAt { get; set; }
        [DataMember(Order = 3)] public string Kind { get; set; }
        [DataMember(Order = 4)] public string Label { get; set; }
        [DataMember(Order = 5)] public string Machine { get; set; }
        [DataMember(Order = 6)] public string Expiry { get; set; }
        [DataMember(Order = 7)] public int Kid { get; set; }
        [DataMember(Order = 8)] public string Features { get; set; }
        [DataMember(Order = 9)] public int Seats { get; set; }
        [DataMember(Order = 10)] public string Note { get; set; }
        [DataMember(Order = 11)] public string IssuedBy { get; set; }
        [DataMember(Order = 12)] public string Status { get; set; }
        [DataMember(Order = 13)] public string RevokedAt { get; set; }
        [DataMember(Order = 14)] public string RevokeReason { get; set; }
        [DataMember(Order = 15)] public string Key { get; set; }

        public bool IsRevoked => string.Equals(Status, "revoked", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sổ key: mỗi key đã cấp 1 dòng JSON (lhb-ledger.jsonl). Tra cứu khi khách hỏi lại key, thu hồi key lộ / hoàn tiền,
    /// xuất Excel. Ghi file tạm rồi thay để không hỏng sổ khi mất điện.
    /// </summary>
    internal sealed class Ledger
    {
        public string Path { get; }
        public List<LedgerEntry> Entries { get; } = new List<LedgerEntry>();

        private Ledger(string path) { Path = path; }

        public static Ledger Load(string path)
        {
            var l = new Ledger(path);
            if (!File.Exists(path)) return l;
            int lineNo = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                lineNo++;
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;
                try
                {
                    l.Entries.Add(FromJson(line));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Sổ key '{path}' dòng {lineNo} hỏng: {ex.Message}");
                }
            }
            return l;
        }

        public bool HasSerial(string serial) => Entries.Any(e => string.Equals(e.Serial, serial, StringComparison.OrdinalIgnoreCase));

        public LedgerEntry Find(string serial) => Entries.FirstOrDefault(e => string.Equals(e.Serial, serial, StringComparison.OrdinalIgnoreCase));

        public void Add(LedgerEntry e)
        {
            Entries.Add(e);
            Save();
        }

        public void Save()
        {
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var e in Entries) sb.Append(ToJson(e)).Append('\n');
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(tmp, Path, Path + ".bak", true);
            else File.Move(tmp, Path);
        }

        /// <summary>Tìm theo serial / tên cấp cho / mã máy / ghi chú (không phân biệt hoa thường).</summary>
        public IEnumerable<LedgerEntry> Search(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Entries;
            string t = text.Trim();
            string compact = t.Replace("-", "");
            return Entries.Where(e =>
                Contains(e.Serial, t) || Contains(e.Label, t) || Contains(e.Note, t) ||
                Contains((e.Machine ?? "").Replace("-", ""), compact) || Contains(e.Kind, t));
        }

        private static bool Contains(string s, string t) => (s ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>CSV UTF-8 có BOM để Excel mở đúng tiếng Việt.</summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.Append('﻿');
            sb.AppendLine("Serial,Ngay cap,Loai,Cap cho,Ma may,Han,Kid,Tinh nang,So may,Ghi chu,Nguoi cap,Trang thai,Ngay thu hoi,Ly do thu hoi,Key");
            foreach (var e in Entries)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    e.Serial, e.IssuedAt, e.Kind, e.Label, e.Machine, e.Expiry, e.Kid.ToString(), e.Features, e.Seats.ToString(),
                    e.Note, e.IssuedBy, e.Status, e.RevokedAt, e.RevokeReason, e.Key
                }.Select(Csv)));
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(LedgerEntry));

        private static string ToJson(LedgerEntry e)
        {
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, e);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static LedgerEntry FromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (LedgerEntry)Serializer.ReadObject(ms);
        }
    }
}

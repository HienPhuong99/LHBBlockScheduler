using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LHBBlockScheduler.Core;

namespace LHBKeyGen
{
    /// <summary>
    /// LHBKeyGen v2 - cấp mã kích hoạt LHB Premium (bản quyền v2, add-in từ v9.5). Double-click = menu tiếng Việt.
    /// Lệnh (chạy "LHBKeyGen help" để xem đủ):
    ///   init / protect / pubkey      tạo khoá ký, đặt mật khẩu, in khoá công khai cho add-in
    ///   issue / verify               cấp key, kiểm tra key
    ///   list / revoke / export-csv   sổ key: tra cứu, thu hồi, xuất Excel
    ///   machine                      mã máy của máy đang chạy
    /// </summary>
    internal static class Program
    {
        private const string KeyFilePattern = "LHB_SIGNING_KEY*.txt";
        private const string LedgerFileName = "LHB_KEY_LEDGER.jsonl";
        private static bool _interactive;

        private static string ToolDir => AppDomain.CurrentDomain.BaseDirectory;
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static int Main(string[] args)
        {
            SetupConsole();
            try
            {
                if (args.Length == 0)
                {
                    _interactive = true;
                    return Interactive();
                }
                return Run(Args.Parse(args));
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                return 1;
            }
        }

        private static int Run(Args a)
        {
            switch (a.Command)
            {
                case "init": return Init(a);
                case "protect": return Protect(a);
                case "pubkey": return PubKey(a);
                case "issue": return Issue(a);
                case "verify": return Verify(a);
                case "list": return List(a);
                case "revoke": return Revoke(a);
                case "export-csv": return ExportCsv(a);
                case "machine": return Machine();
                case "help":
                case "-h":
                case "--help":
                case "/?":
                    Help();
                    return 0;
                default:
                    Error($"Lệnh không có: '{a.Command}'. Gõ: LHBKeyGen help");
                    return 1;
            }
        }

        // ============================== LỆNH ==============================

        /// <summary>Tạo cặp khoá ký mới (kid). Không ghi đè file đã có (mất khoá cũ = không cấp được key cho bản cũ).</summary>
        private static int Init(Args a)
        {
            byte kid = byte.Parse(a.Get("kid", "1"), CultureInfo.InvariantCulture);
            string path = a.Get("out") ?? Path.Combine(ToolDir, $"LHB_SIGNING_KEY_kid{kid}.txt");
            if (File.Exists(path) && !a.Has("force"))
                throw new IOException($"Đã có file '{path}' - không ghi đè để khỏi mất khoá cũ. Chọn --kid khác hoặc --out khác.");
            string password = null;
            if (!a.Has("no-password"))
            {
                password = Environment.GetEnvironmentVariable("LHB_KEY_PASSPHRASE");
                if (string.IsNullOrEmpty(password) && (_interactive || a.Has("password"))) password = PromptNewPassword();
            }
            var key = KeyFile.Generate(kid);
            using (key.CreateSigner()) { } // tự kiểm
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, key.ToText(password), new UTF8Encoding(false));

            Ok($"Đã tạo khoá ký kid {kid}: {path}" + (string.IsNullOrEmpty(password) ? " (CHƯA đặt mật khẩu - chạy 'protect' để đặt)" : " (có mật khẩu)"));
            Console.WriteLine();
            Console.WriteLine("Dán dòng sau vào Core/LicensePolicy.cs (TrustedKeys) rồi build add-in bản mới để add-in nhận key của khoá này:");
            Console.WriteLine("    " + key.PolicyLine);
            Console.WriteLine();
            Console.WriteLine("Sao lưu file khoá ở 2 nơi (USB + trình quản lý mật khẩu). Mất file = không cấp được key cho kid này.");
            return 0;
        }

        /// <summary>Đặt / đổi mật khẩu cho file khoá ký.</summary>
        private static int Protect(Args a)
        {
            string path = a.Get("key") ?? FindKeyFile() ?? throw new FileNotFoundException("Không thấy file khoá ký (--key <file>)");
            var key = KeyFile.Load(path, PasswordProvider());
            string password = PromptNewPassword();
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Chưa nhập mật khẩu");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, key.ToText(password), new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
            Ok($"Đã đặt mật khẩu cho '{path}'. Lần sau cấp key cần nhập mật khẩu (hoặc đặt biến môi trường LHB_KEY_PASSPHRASE).");
            return 0;
        }

        private static int PubKey(Args a)
        {
            string path = a.Get("key") ?? FindKeyFile() ?? throw new FileNotFoundException("Không thấy file khoá ký (--key <file>)");
            var key = KeyFile.Load(path, PasswordProvider());
            Console.WriteLine($"kid {key.KeyId}, tạo {key.Created:dd/MM/yyyy}. Dòng cho Core/LicensePolicy.cs (TrustedKeys):");
            Console.WriteLine("    " + key.PolicyLine);
            bool trusted = LicensePolicy.TrustedKeys.TryGetValue(key.KeyId, out var b64) && b64 == key.PublicKeyBase64;
            Console.WriteLine(trusted ? "=> Add-in build từ mã nguồn này ĐÃ nhận khoá này." : "=> Add-in build từ mã nguồn này CHƯA nhận khoá này.");
            return 0;
        }

        /// <summary>Cấp 1 key. Ghi sổ key (nếu có), chép vào clipboard (Windows), ghi tóm tắt cho GitHub Actions.</summary>
        private static int Issue(Args a)
        {
            var kind = ParseKind(a.Get("kind", "machine"));
            byte[] machine = new byte[LicenseCodec.MachineLength];
            string machineText = (a.Get("machine") ?? "").Trim();
            if (kind == LicenseKind.Machine)
            {
                machine = LicenseCodec.ParseMachineCode(machineText)
                          ?? throw new ArgumentException($"Mã máy '{machineText}' không đúng (16 ký tự dạng XXXX-XXXX-XXXX-XXXX, khách xem trong LHBBANQUYEN)");
            }
            else if (machineText.Length > 0)
            {
                Warn("Key dùng chung / review không gắn máy -> bỏ qua mã máy đã nhập.");
            }

            string label = (a.Get("label") ?? "").Trim();
            if (label.Length == 0)
            {
                if (kind == LicenseKind.Machine) throw new ArgumentException("Cần --label: cấp cho ai (tên khách / công ty)");
                label = kind == LicenseKind.Review ? "Review" : "Dùng chung";
            }
            DateTime? expiry = ParseExpiry(a.Get("expires", "never"));
            int seats = int.Parse(a.Get("seats", "0"), CultureInfo.InvariantCulture);
            if (seats < 0 || seats > 255) throw new ArgumentException("--seats từ 0 đến 255");

            var key = LoadSigningKey(a, out string keyPath);
            var ledger = OpenLedger(a, keyPath);

            var d = new LicenseData
            {
                KeyId = key.KeyId,
                Kind = kind,
                Features = LicenseFeatures.Premium,
                Serial = NewSerial(ledger),
                Issued = DateTime.Today,
                Expiry = expiry,
                Machine = machine,
                Seats = (byte)seats,
                Label = label
            };
            string text;
            using (var signer = key.CreateSigner()) text = LicenseCodec.Sign(d, signer);

            // Tự kiểm: đọc lại key, kiểm chữ ký bằng khoá công khai -> không bao giờ gửi khách key hỏng
            var back = LicenseCodec.Parse(text, out string err);
            if (back == null || !LicenseCodec.VerifySignature(back, key.PublicKey) || back.Serial != d.Serial)
                throw new CryptographicException("Tự kiểm key vừa tạo thất bại: " + (err ?? "chữ ký sai"));
            bool trusted = LicensePolicy.TrustedKeys.TryGetValue(key.KeyId, out var b64) && b64 == key.PublicKeyBase64;

            ledger?.Add(new LedgerEntry
            {
                Serial = d.SerialText,
                IssuedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Kind = kind.ToString().ToLowerInvariant(),
                Label = label,
                Machine = kind == LicenseKind.Machine ? LicenseCodec.FormatMachineCode(machine) : "",
                Expiry = expiry.HasValue ? expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "never",
                Kid = key.KeyId,
                Features = d.Features.ToString(),
                Seats = seats,
                Note = (a.Get("note") ?? "").Trim(),
                IssuedBy = a.Get("by") ?? Environment.UserName,
                Status = "active",
                Key = text
            });

            Console.WriteLine();
            Ok("ĐÃ CẤP KEY");
            PrintDetails(back, key.KeyId, trusted);
            Console.WriteLine();
            Console.WriteLine("Key (gửi nguyên chuỗi, khách dán vào LHBBANQUYEN > Kích hoạt):");
            Console.WriteLine(text);
            Console.WriteLine();
            if (!trusted) Warn($"Add-in build từ mã nguồn này CHƯA nhận khoá kid {key.KeyId} -> key chỉ dùng được sau khi thêm khoá công khai (lệnh pubkey) và build bản mới.");
            if (ledger != null) Console.WriteLine($"Đã ghi sổ key: {ledger.Path} ({ledger.Entries.Count} key)");
            else Warn("Không ghi sổ key (không có --ledger). Tự lưu lại key + thông tin khách.");
            if (!a.Has("no-clip") && CopyToClipboard(text)) Console.WriteLine("Đã chép key vào clipboard.");

            WriteSummary(a, sb =>
            {
                sb.AppendLine("## Đã cấp key LHB Premium");
                sb.AppendLine();
                AppendDetailsTable(sb, back, trusted);
                sb.AppendLine();
                sb.AppendLine("Key (khách dán vào `LHBBANQUYEN` > Kích hoạt):");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(text);
                sb.AppendLine("```");
            });
            return 0;
        }

        /// <summary>Kiểm tra key: chữ ký theo khoá công khai của add-in (+ file khoá nếu có), thu hồi, hạn, mã máy.</summary>
        private static int Verify(Args a)
        {
            string text = a.Positional.FirstOrDefault() ?? a.Get("text");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Cần chuỗi key: LHBKeyGen verify LHB2-...");
            var d = LicenseCodec.Parse(text, out string err);
            if (d == null)
            {
                Error("Key không đọc được: " + err);
                return 2;
            }

            bool sigOk = false;
            string via = "";
            if (LicensePolicy.TrustedKeys.TryGetValue(d.KeyId, out var b64) && !b64.StartsWith("@@"))
            {
                sigOk = LicenseCodec.VerifySignature(d, Convert.FromBase64String(b64));
                via = "khoá công khai trong add-in";
            }
            if (!sigOk && (a.Get("key") != null || a.Get("key-env") != null))
            {
                var key = LoadSigningKey(a, out _);
                if (key.KeyId == d.KeyId && LicenseCodec.VerifySignature(d, key.PublicKey))
                {
                    sigOk = true;
                    via = "file khoá ký (add-in CHƯA nhận kid này)";
                }
            }
            bool revoked = LicensePolicy.RevokedSerials.Contains(d.Serial);
            var ledger = OpenLedger(a, a.Get("key"), required: false);
            var entry = ledger?.Find(d.SerialText);
            bool revokedInLedger = entry?.IsRevoked == true;
            bool expired = d.Expiry.HasValue && DateTime.Today > d.Expiry.Value;

            PrintDetails(d, d.KeyId, sigOk && via.StartsWith("khoá công khai"));
            Console.WriteLine($"Chữ ký            : {(sigOk ? "ĐÚNG (" + via + ")" : "SAI / kid chưa có trong add-in")}");
            Console.WriteLine($"Thu hồi           : {(revoked ? "CÓ (trong add-in)" : revokedInLedger ? "có trong sổ (add-in bản này chưa thu hồi)" : "không")}");
            if (entry != null) Console.WriteLine($"Sổ key            : cấp {entry.IssuedAt} bởi {entry.IssuedBy}, ghi chú '{entry.Note}'");
            string m = a.Get("machine");
            if (!string.IsNullOrWhiteSpace(m) && d.Kind == LicenseKind.Machine)
            {
                var mb = LicenseCodec.ParseMachineCode(m);
                Console.WriteLine($"Khớp mã máy {m,-19}: {(mb != null && mb.SequenceEqual(d.Machine) ? "CÓ" : "KHÔNG")}");
            }
            bool ok = sigOk && !revoked && !expired && (d.Kind != LicenseKind.Review || LicensePolicy.AcceptReviewKeys);
            // Kết luận theo đúng hàm kiểm của add-in (cùng file Core/LicensePolicy.cs) -> trả lời khách "vì sao key không chạy"
            byte[] mcheck = LicenseCodec.ParseMachineCode(a.Get("machine"));
            var addin = LicensePolicy.Validate(text, mcheck, DateTime.Today);
            Console.WriteLine($"Add-in bản này    : {(addin.Valid ? "NHẬN key" : "TỪ CHỐI - " + addin.Error)}" +
                              (d.Kind == LicenseKind.Machine && mcheck == null ? " (chưa kiểm mã máy: thêm --machine <mã máy khách>)" : ""));
            Console.WriteLine();
            if (ok) Ok("KEY HỢP LỆ"); else Error("KEY KHÔNG DÙNG ĐƯỢC");

            WriteSummary(a, sb =>
            {
                sb.AppendLine(ok ? "## Key hợp lệ" : "## Key KHÔNG dùng được");
                sb.AppendLine();
                AppendDetailsTable(sb, d, sigOk);
                sb.AppendLine($"| Chữ ký | {(sigOk ? "đúng (" + via + ")" : "SAI")} |");
                sb.AppendLine($"| Thu hồi | {(revoked ? "có" : revokedInLedger ? "có trong sổ" : "không")} |");
            });
            return ok ? 0 : 3;
        }

        private static int List(Args a)
        {
            var ledger = OpenLedger(a, a.Get("key"), required: true);
            var rows = ledger.Search(a.Get("find")).ToList();
            Console.WriteLine($"Sổ key: {ledger.Path} - {ledger.Entries.Count} key" + (rows.Count != ledger.Entries.Count ? $", khớp '{a.Get("find")}': {rows.Count}" : ""));
            Console.WriteLine();
            Console.WriteLine($"{"Serial",-9} {"Ngày cấp",-10} {"Loại",-9} {"Hạn",-10} {"Trạng thái",-10} {"Mã máy",-19} Cấp cho / ghi chú");
            foreach (var e in rows)
            {
                Console.WriteLine($"{e.Serial,-9} {Short(e.IssuedAt, 10),-10} {e.Kind,-9} {(e.Expiry == "never" ? "trọn đời" : e.Expiry),-10} " +
                                  $"{(e.IsRevoked ? "THU HỒI" : "đang dùng"),-10} {e.Machine,-19} {e.Label}" + (string.IsNullOrEmpty(e.Note) ? "" : " | " + e.Note));
            }
            WriteSummary(a, sb =>
            {
                sb.AppendLine($"## Sổ key ({rows.Count}/{ledger.Entries.Count})");
                sb.AppendLine();
                sb.AppendLine("| Serial | Ngày cấp | Loại | Hạn | Trạng thái | Mã máy | Cấp cho | Ghi chú |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var e in rows)
                    sb.AppendLine($"| {e.Serial} | {Short(e.IssuedAt, 10)} | {e.Kind} | {e.Expiry} | {(e.IsRevoked ? "thu hồi" : "đang dùng")} | {e.Machine} | {Md(e.Label)} | {Md(e.Note)} |");
            });
            return 0;
        }

        /// <summary>Thu hồi key trong sổ + in danh sách serial thu hồi để dán vào Core/LicensePolicy.cs.</summary>
        private static int Revoke(Args a)
        {
            string serialText = a.Positional.FirstOrDefault() ?? a.Get("serial");
            if (!LicenseCodec.TryParseSerial(serialText, out uint serial)) throw new ArgumentException($"Serial '{serialText}' không đúng (8 ký tự hex, xem bằng lệnh list)");
            var ledger = OpenLedger(a, a.Get("key"), required: true);
            var e = ledger.Find(LicenseCodec.FormatSerial(serial));
            if (e == null) Warn($"Serial {LicenseCodec.FormatSerial(serial)} không có trong sổ - vẫn in dòng thu hồi.");
            else if (!e.IsRevoked)
            {
                e.Status = "revoked";
                e.RevokedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                e.RevokeReason = (a.Get("reason") ?? "").Trim();
                ledger.Save();
                Ok($"Đã đánh dấu thu hồi {e.Serial} ({e.Label}) trong sổ.");
            }

            var lines = ledger.Entries.Where(x => x.IsRevoked)
                              .Select(x => $"0x{x.Serial}, // {x.Label} - {x.RevokeReason}".TrimEnd(' ', '-'))
                              .ToList();
            if (e == null) lines.Add($"0x{LicenseCodec.FormatSerial(serial)},");
            Console.WriteLine();
            Console.WriteLine("Dán vào Core/LicensePolicy.cs (RevokedSerials) rồi build + phát hành bản mới:");
            foreach (var l in lines) Console.WriteLine("    " + l);
            Console.WriteLine("Lưu ý: bản add-in cũ chạy offline vẫn nhận key đã thu hồi (giới hạn của bản quyền offline).");
            WriteSummary(a, sb =>
            {
                sb.AppendLine($"## Thu hồi key {LicenseCodec.FormatSerial(serial)}");
                sb.AppendLine();
                sb.AppendLine("Dán vào `Core/LicensePolicy.cs` (`RevokedSerials`) rồi build bản mới:");
                sb.AppendLine("```csharp");
                foreach (var l in lines) sb.AppendLine(l);
                sb.AppendLine("```");
            });
            return 0;
        }

        private static int ExportCsv(Args a)
        {
            var ledger = OpenLedger(a, a.Get("key"), required: true);
            string outPath = a.Get("out") ?? Path.ChangeExtension(ledger.Path, ".csv");
            File.WriteAllText(outPath, ledger.ToCsv(), new UTF8Encoding(false));
            Ok($"Đã xuất {ledger.Entries.Count} key ra '{outPath}' (mở bằng Excel).");
            return 0;
        }

        private static int Machine()
        {
            byte[] m = MachineFingerprint.Compute(out string source);
            Console.WriteLine($"Mã máy của máy này: {LicenseCodec.FormatMachineCode(m)}  (nguồn: {source})");
            if (!IsWindows) Warn("Không phải Windows: mã máy chỉ để thử, không khớp mã máy trong AutoCAD.");
            return 0;
        }

        // ============================== MENU TƯƠNG TÁC ==============================

        private static int Interactive()
        {
            try { Console.Title = "LHB KeyGen v2 - cấp mã kích hoạt LHB Premium"; } catch { }
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("================ LHB KeyGen v2 - cấp mã kích hoạt LHB Premium ================");
                string keyPath = FindKeyFile();
                if (keyPath == null) Console.WriteLine("Khoá ký   : CHƯA CÓ (chọn 8 để tạo, hoặc chép file LHB_SIGNING_KEY_kid*.txt vào cạnh LHBKeyGen.exe)");
                else
                {
                    string info;
                    try
                    {
                        var pub = KeyFile.LoadPublic(keyPath);
                        bool trusted = LicensePolicy.TrustedKeys.TryGetValue(pub.KeyId, out var b64) && b64 == pub.PublicKeyBase64;
                        info = $"kid {pub.KeyId}" + (trusted ? ", add-in ĐÃ nhận" : ", add-in CHƯA nhận (xem mục 8)");
                    }
                    catch (Exception ex) { info = "LỖI: " + ex.Message; }
                    Console.WriteLine($"Khoá ký   : {keyPath} ({info})");
                    string ledgerPath = Path.Combine(Path.GetDirectoryName(keyPath), LedgerFileName);
                    int n = 0;
                    try { n = Ledger.Load(ledgerPath).Entries.Count; } catch { }
                    Console.WriteLine($"Sổ key    : {ledgerPath} ({n} key)");
                }
                Console.WriteLine();
                Console.WriteLine("  1. Cấp key THEO MÁY (khách gửi mã máy trong LHBBANQUYEN)");
                Console.WriteLine("  2. Cấp key DÙNG CHUNG / REVIEW (không gắn máy - chỉ gửi người tin cậy)");
                Console.WriteLine("  3. Kiểm tra 1 key");
                Console.WriteLine("  4. Xem / tìm sổ key");
                Console.WriteLine("  5. Thu hồi 1 key");
                Console.WriteLine("  6. Xuất sổ key ra Excel (CSV)");
                Console.WriteLine("  7. Mã máy của máy này");
                Console.WriteLine("  8. Tạo khoá ký mới (kid mới)");
                Console.WriteLine("  9. Đặt / đổi mật khẩu file khoá ký");
                Console.WriteLine("  0. Thoát");
                Console.Write("Chọn: ");
                string c = (Console.ReadLine() ?? "0").Trim();
                if (c == "0" || c.Length == 0 && Console.IsInputRedirected) return 0;
                try
                {
                    switch (c)
                    {
                        case "1":
                            Run(new Args("issue")
                                .With("kind", "machine")
                                .With("machine", Ask("Mã máy khách gửi (XXXX-XXXX-XXXX-XXXX)"))
                                .With("label", Ask("Cấp cho (tên khách / công ty)"))
                                .With("expires", Ask("Hạn dùng: Enter = trọn đời | +365 = 365 ngày | 31/12/2027", "never"))
                                .With("note", Ask("Ghi chú nội bộ (SĐT, số đơn... - không nằm trong key)", "")));
                            break;
                        case "2":
                            string k = Ask("Loại: 1 = dùng chung (công ty / nhóm), 2 = review", "2");
                            Run(new Args("issue")
                                .With("kind", k == "1" ? "floating" : "review")
                                .With("label", Ask("Cấp cho (vd: Nhóm review, Công ty ABC)", k == "1" ? "" : "Review"))
                                .With("expires", Ask("Hạn dùng: Enter = trọn đời | +90 = 90 ngày | 31/12/2027", "never"))
                                .With("seats", Ask("Số máy dự kiến (chỉ để ghi nhớ, 0 = không ghi)", "0"))
                                .With("note", Ask("Ghi chú nội bộ", "")));
                            break;
                        case "3":
                            Run(new Args("verify").With("text", Ask("Dán key LHB2-...")));
                            break;
                        case "4":
                            Run(new Args("list").With("find", Ask("Tìm (tên / mã máy / serial / ghi chú, Enter = tất cả)", "")));
                            break;
                        case "5":
                            Run(new Args("revoke").With("serial", Ask("Serial cần thu hồi (8 ký tự, xem mục 4)")).With("reason", Ask("Lý do", "")));
                            break;
                        case "6":
                            Run(new Args("export-csv"));
                            break;
                        case "7":
                            Machine();
                            break;
                        case "8":
                            Run(new Args("init").With("kid", Ask("Số hiệu khoá mới (kid, 1-255)", "2")));
                            break;
                        case "9":
                            Run(new Args("protect"));
                            break;
                        default:
                            Warn("Chọn số từ 0 đến 9.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Error(ex.Message);
                }
                if (Console.IsInputRedirected) continue;
                Console.Write("Nhấn Enter để về menu...");
                Console.ReadLine();
            }
        }

        private static string Ask(string question, string defaultValue = null)
        {
            Console.Write(question + (defaultValue != null && defaultValue.Length > 0 ? $" [{defaultValue}]" : "") + ": ");
            string s = Console.ReadLine();
            if (s == null) return defaultValue ?? "";
            s = s.Trim();
            return s.Length == 0 && defaultValue != null ? defaultValue : s;
        }

        // ============================== KHOÁ + SỔ ==============================

        /// <summary>File khoá ký mặc định: biến LHB_SIGNING_KEY_FILE, rồi LHB_SIGNING_KEY*.txt cạnh exe / thư mục đang đứng (kid lớn nhất).</summary>
        private static string FindKeyFile()
        {
            string env = Environment.GetEnvironmentVariable("LHB_SIGNING_KEY_FILE");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var dir in new[] { ToolDir, Directory.GetCurrentDirectory() }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var files = Directory.GetFiles(dir, KeyFilePattern);
                if (files.Length == 0) continue;
                return files.Select(f => new { f, kid = TryKid(f) }).OrderByDescending(x => x.kid).First().f;
            }
            return null;
        }

        private static int TryKid(string path)
        {
            try { return KeyFile.LoadPublic(path).KeyId; }
            catch { return -1; }
        }

        private static KeyFile LoadSigningKey(Args a, out string keyPath)
        {
            string env = a.Get("key-env");
            if (!string.IsNullOrEmpty(env))
            {
                keyPath = null;
                string text = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"Biến môi trường {env} trống (GitHub: chưa thêm secret?)");
                return KeyFile.Parse(text, PasswordProvider());
            }
            keyPath = a.Get("key") ?? FindKeyFile()
                      ?? throw new FileNotFoundException("Không thấy file khoá ký. Tạo mới: LHBKeyGen init | hoặc chỉ ra: --key <file>");
            return KeyFile.Load(keyPath, PasswordProvider());
        }

        /// <summary>Sổ key: --ledger, biến LHB_KEY_LEDGER, hoặc LHB_KEY_LEDGER.jsonl cạnh file khoá ký.</summary>
        private static Ledger OpenLedger(Args a, string keyPath, bool required = false)
        {
            string path = a.Get("ledger") ?? Environment.GetEnvironmentVariable("LHB_KEY_LEDGER");
            if (string.IsNullOrEmpty(path))
            {
                string kp = keyPath ?? FindKeyFile();
                if (kp != null) path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(kp)), LedgerFileName);
            }
            if (string.IsNullOrEmpty(path))
            {
                if (required) throw new FileNotFoundException("Không biết sổ key ở đâu (--ledger <file>)");
                return null;
            }
            return Ledger.Load(path);
        }

        private static uint NewSerial(Ledger ledger)
        {
            var b = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    rng.GetBytes(b);
                    uint s = BitConverter.ToUInt32(b, 0);
                    if (s == 0 || LicensePolicy.RevokedSerials.Contains(s)) continue;
                    if (ledger != null && ledger.HasSerial(LicenseCodec.FormatSerial(s))) continue;
                    return s;
                }
            }
        }

        private static Func<string> PasswordProvider() => () =>
        {
            string p = Environment.GetEnvironmentVariable("LHB_KEY_PASSPHRASE");
            if (!string.IsNullOrEmpty(p)) return p;
            if (Console.IsInputRedirected) return null;
            return ReadPassword("Mật khẩu file khoá ký: ");
        };

        private static string PromptNewPassword()
        {
            if (Console.IsInputRedirected) return null;
            Console.WriteLine("Đặt mật khẩu cho file khoá ký (Enter bỏ trống = không đặt, không khuyến khích):");
            string p1 = ReadPassword("  Mật khẩu: ");
            if (string.IsNullOrEmpty(p1)) return null;
            if (p1.Length < 10) throw new ArgumentException("Mật khẩu tối thiểu 10 ký tự");
            string p2 = ReadPassword("  Nhập lại: ");
            if (p1 != p2) throw new ArgumentException("2 lần nhập mật khẩu không khớp");
            return p1;
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
            }
            Console.WriteLine();
            return sb.ToString();
        }

        // ============================== TIỆN ÍCH ==============================

        private static LicenseKind ParseKind(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "machine": case "may": case "m": case "1": return LicenseKind.Machine;
                case "floating": case "chung": case "f": case "2": return LicenseKind.Floating;
                case "review": case "r": case "3": return LicenseKind.Review;
                default: throw new ArgumentException($"Loại key '{s}' không có (machine | floating | review)");
            }
        }

        /// <summary>"never" / trống / "0" = trọn đời; "+365" = sau 365 ngày; "yyyy-MM-dd" hoặc "dd/MM/yyyy".</summary>
        private static DateTime? ParseExpiry(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0 || s == "never" || s == "0" || s == "vinh-vien" || s == "tron-doi" || s == "trọn đời") return null;
            if (s.StartsWith("+") && int.TryParse(s.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) && days > 0)
                return DateTime.Today.AddDays(days);
            foreach (var fmt in new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" })
                if (DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    if (d.Date <= DateTime.Today) throw new ArgumentException("Ngày hết hạn phải sau hôm nay");
                    return d.Date;
                }
            throw new ArgumentException($"Hạn '{s}' không hiểu (never | +365 | 2027-12-31 | 31/12/2027)");
        }

        private static void PrintDetails(LicenseData d, byte kid, bool trustedByAddin)
        {
            Console.WriteLine($"Serial            : {d.SerialText}");
            Console.WriteLine($"Loại              : {d.KindText}");
            Console.WriteLine($"Cấp cho           : {d.Label}");
            if (d.Kind == LicenseKind.Machine) Console.WriteLine($"Mã máy            : {d.MachineCode}");
            Console.WriteLine($"Ngày cấp          : {d.Issued:dd/MM/yyyy}");
            Console.WriteLine($"Hạn               : {(d.Expiry.HasValue ? d.Expiry.Value.ToString("dd/MM/yyyy") : "trọn đời")}");
            Console.WriteLine($"Tính năng         : {d.Features}" + (d.Seats > 0 ? $", {d.Seats} máy (ghi nhớ)" : ""));
            Console.WriteLine($"Khoá ký           : kid {kid}" + (trustedByAddin ? " (add-in đã nhận)" : ""));
        }

        private static void AppendDetailsTable(StringBuilder sb, LicenseData d, bool trusted)
        {
            sb.AppendLine("| | |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Serial | `{d.SerialText}` |");
            sb.AppendLine($"| Loại | {d.KindText} |");
            sb.AppendLine($"| Cấp cho | {Md(d.Label)} |");
            if (d.Kind == LicenseKind.Machine) sb.AppendLine($"| Mã máy | `{d.MachineCode}` |");
            sb.AppendLine($"| Ngày cấp | {d.Issued:dd/MM/yyyy} |");
            sb.AppendLine($"| Hạn | {(d.Expiry.HasValue ? d.Expiry.Value.ToString("dd/MM/yyyy") : "trọn đời")} |");
            sb.AppendLine($"| Khoá ký | kid {d.KeyId}{(trusted ? " (add-in đã nhận)" : " (add-in CHƯA nhận)")} |");
        }

        /// <summary>Tóm tắt markdown cho GitHub Actions (--summary $GITHUB_STEP_SUMMARY).</summary>
        private static void WriteSummary(Args a, Action<StringBuilder> build)
        {
            string path = a.Get("summary");
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder();
            build(sb);
            File.AppendAllText(path, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
        }

        private static string Md(string s) => (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        private static string Short(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n);

        /// <summary>Chép key vào clipboard bằng clip.exe có sẵn của Windows (không cần thư viện giao diện).</summary>
        private static bool CopyToClipboard(string text)
        {
            if (!IsWindows || Console.IsOutputRedirected) return false;
            try
            {
                var psi = new ProcessStartInfo("clip") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.Write(text);
                    p.StandardInput.Close();
                    p.WaitForExit(3000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetupConsole()
        {
            try
            {
                // Windows, không chuyển hướng: UTF-16 -> Console dùng ReadConsoleW / WriteConsoleW, gõ + hiện đúng tiếng Việt
                // (kể cả .NET Framework 4.8). Còn lại (Linux, GitHub Actions, ghi ra file): UTF-8.
                if (IsWindows && !Console.IsOutputRedirected) Console.OutputEncoding = Encoding.Unicode;
                else Console.OutputEncoding = new UTF8Encoding(false);
                if (IsWindows && !Console.IsInputRedirected) Console.InputEncoding = Encoding.Unicode;
            }
            catch
            {
                // Console cũ không đổi được bảng mã -> vẫn chạy, chỉ hiện dấu sai
            }
        }

        private static void Help()
        {
            Console.WriteLine(@"LHBKeyGen v2 - cấp mã kích hoạt LHB Premium (add-in từ v9.5). Chạy không tham số = menu.

  init       [--kid 1] [--out file] [--no-password]      Tạo khoá ký mới (in dòng khoá công khai cho add-in)
  protect    [--key file]                               Đặt / đổi mật khẩu file khoá ký
  pubkey     [--key file]                               In dòng khoá công khai cho Core/LicensePolicy.cs
  issue      --kind machine|floating|review --label ""Tên"" [--machine XXXX-XXXX-XXXX-XXXX]
             [--expires never|+365|2027-12-31] [--seats N] [--note ""..""] [--key file | --key-env BIEN]
             [--ledger file] [--summary file] [--by ten] [--no-clip]
  verify     LHB2-... [--machine XXXX-...] [--key file] [--ledger file]
  list       [--find text] [--ledger file]
  revoke     SERIAL [--reason ""..""] [--ledger file]     Thu hồi + in danh sách cho LicensePolicy.RevokedSerials
  export-csv [--ledger file] [--out file.csv]
  machine                                               Mã máy của máy này

File khoá ký mặc định: LHB_SIGNING_KEY_kid*.txt cạnh LHBKeyGen.exe (hoặc biến LHB_SIGNING_KEY_FILE).
Sổ key mặc định: LHB_KEY_LEDGER.jsonl cạnh file khoá ký. Mật khẩu: hỏi trên màn hình hoặc biến LHB_KEY_PASSPHRASE.");
        }

        private static void Ok(string s) => WriteColor(s, ConsoleColor.Green);
        private static void Warn(string s) => WriteColor("Lưu ý: " + s, ConsoleColor.Yellow);
        private static void Error(string s) => WriteColor("LỖI: " + s, ConsoleColor.Red);

        private static void WriteColor(string s, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(s);
            }
            finally
            {
                Console.ForegroundColor = old;
            }
        }
    }

    /// <summary>Tham số dòng lệnh: lệnh, tham số vị trí, --tên giá_trị / --tên=giá_trị, cờ không giá trị.</summary>
    internal sealed class Args
    {
        private static readonly HashSet<string> FlagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "force", "no-password", "password", "no-clip" };

        public string Command { get; private set; }
        public List<string> Positional { get; } = new List<string>();
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Args(string command) { Command = command; }

        public static Args Parse(string[] args)
        {
            var a = new Args((args[0] ?? "").Trim().ToLowerInvariant());
            for (int i = 1; i < args.Length; i++)
            {
                string s = args[i];
                if (s.StartsWith("--"))
                {
                    string name = s.Substring(2), value = null;
                    int eq = name.IndexOf('=');
                    if (eq >= 0) { value = name.Substring(eq + 1); name = name.Substring(0, eq); }
                    if (FlagNames.Contains(name)) { a._flags.Add(name); continue; }
                    if (value == null)
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException($"Thiếu giá trị cho --{name}");
                        value = args[++i];
                    }
                    a._options[name] = value;
                }
                else a.Positional.Add(s);
            }
            return a;
        }

        public Args With(string name, string value)
        {
            _options[name] = value;
            return this;
        }

        /// <summary>Giá trị tuỳ chọn; không có -> def. Truyền rỗng ("") vẫn là có (vd --machine "" với key dùng chung).</summary>
        public string Get(string name, string def = null) => _options.TryGetValue(name, out var v) ? v : def;

        public bool Has(string flag) => _flags.Contains(flag);
    }
}

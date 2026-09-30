using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Internal;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>1 phím tắt: gõ Alias = chạy lệnh Command.</summary>
    public class CommandAlias
    {
        public string Command { get; set; }
        public string Alias { get; set; }
    }

    /// <summary>1 lệnh của add-in (đọc từ [CommandMethod] trong Commands.cs) kèm mô tả tiếng Việt.</summary>
    public class LhbCommandInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>Lệnh kiểm tra / chẩn đoán (hiện chữ xám cuối bảng).</summary>
        public bool IsDebug { get; set; }
        public CommandFlags Flags { get; set; }
        public MethodInfo Method { get; set; }
    }

    /// <summary>
    /// Phím tắt lệnh (bảng lệnh LHBLENH, yêu cầu 29/09/2026). Mỗi phím tắt đăng ký thành 1 lệnh AutoCAD thật
    /// bằng Autodesk.AutoCAD.Internal.Utils.AddCommand (nhóm lệnh LHB_PHIMTAT), gọi thẳng hàm của lệnh gốc.
    /// Không sửa acad.pgp của máy. Phím tắt lưu trong settings.json, đăng ký lại mỗi lần NETLOAD.
    /// Không đăng ký phím tắt trùng lệnh có sẵn (AutoCAD / LISP / add-in khác) để không che mất lệnh đó.
    /// </summary>
    public static class CommandAliasManager
    {
        public const string GroupName = "LHB_PHIMTAT";

        /// <summary>Thứ tự + mô tả lệnh trong bảng. Lệnh có [CommandMethod] mà thiếu ở đây vẫn hiện cuối bảng.</summary>
        private static readonly (string Name, string Description, bool IsDebug)[] Catalog =
        {
            ("LHBSCAN", "Quét chọn block, mở bảng thống kê", false),
            ("LHBMAU", "Thư viện block mẫu: thêm / xoá / sắp xếp / chèn block mẫu", false),
            ("LHBLEGEND", "Quét bảng chú thích (Table) có sẵn vào bộ block mẫu", false),
            ("LHBDUPCLEAR", "Xoá vòng đỏ / đường dẫn đánh dấu block trùng", false),
            ("LHBKHOPCOT", "Khớp độ rộng cột bảng đã xuất theo chữ (như double-click mép cột Excel)", false),
            ("LHBCAPNHAT", "[Premium] Cập nhật bảng đã xuất sau khi sửa bản vẽ (ô đổi tô đỏ)", false),
            ("LHBKHUVUC", "[Premium] Khai báo tầng / khu vực: bảng có cột SL từng khu", false),
            ("LHBNHIEUBV", "[Premium] Thống kê nhiều bản vẽ DWG cùng lúc", false),
            ("LHBCHIEUDAI", "[Premium] Chiều dài ống / dây theo layer (m, hao hụt, theo khu vực)", false),
            ("LHBSOATLOI", "[Premium] Soát lỗi đếm: explode, đổi tên, layer tắt, trùng, tỉ lệ lạ", false),
            ("LHBDANHSO", "[Premium] Đánh số thiết bị tự động (SP-01, SP-02...)", false),
            ("LHBVUNGBV", "[Premium] Vẽ vùng bảo vệ đầu báo / đầu phun, soát khoảng hở", false),
            ("LHBTHAYBLOCK", "[Premium] Thay block hàng loạt, giữ vị trí / góc / thuộc tính", false),
            ("LHBMAUBANG", "[Premium] Mẫu bảng xuất: tiêu đề, dòng tổng, font, màu", false),
            ("LHBPALETTE", "Bật / tắt bảng công cụ LHB dock cạnh màn hình", false),
            ("LHBRIBBON", "Bật / tắt tab Ribbon LHB Premium", false),
            ("LHBBANQUYEN", "Mã máy, kích hoạt bản quyền Premium", false),
            ("LHBLENH", "Bảng danh sách lệnh này: chạy lệnh, đổi phím tắt", false),
            ("LHBVERSION", "Xem phiên bản, MD5 add-in đang chạy", true),
            ("LHBDIAG", "Tạo file chẩn đoán LHBDIAG_*.txt để gửi về khi lỗi", true),
            ("LHBLOG", "Mở file nhật ký log.txt", true),
            ("LHBLOGPATH", "In đường dẫn file log.txt", true),
            ("LHBCLEARCACHE", "Xoá bộ nhớ đệm ảnh ký hiệu", true),
        };

        /// <summary>Phím tắt mặc định khi settings.json chưa có CommandAliases.</summary>
        public static readonly CommandAlias[] Defaults =
        {
            new CommandAlias { Command = "LHBLENH", Alias = "LHB" },
            new CommandAlias { Command = "LHBSCAN", Alias = "TKB" },
            new CommandAlias { Command = "LHBMAU", Alias = "BLM" },
        };

        private static List<LhbCommandInfo> _commands;
        private static Dictionary<string, string> _pgp;
        // Phím tắt đã đăng ký với AutoCAD: phím tắt -> lệnh
        private static readonly Dictionary<string, string> _registered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Giữ delegate: AutoCAD giữ con trỏ tới callback, GC thu hồi delegate thì gõ phím tắt sẽ crash
        // Giữ delegate CommandCallback cho AutoCAD gọi (không để GC thu). Kiểu object: v9.7 (D4) không để kiểu của API nội bộ
        // nằm trong khai báo field (thiếu kiểu -> lỗi nạp cả lớp này, hỏng luôn bảng lệnh LHBLENH)
        private static readonly Dictionary<string, object> _callbacks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Kết quả đăng ký lần gần nhất: phím tắt -> trạng thái (cột Trạng thái của bảng lệnh, LHBDIAG).</summary>
        public static readonly Dictionary<string, string> Status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static List<LhbCommandInfo> AllCommands
        {
            get
            {
                if (_commands != null) return _commands;
                var order = Catalog.Select((c, i) => new { c.Name, i }).ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);
                _commands = typeof(LHBBlockScheduler.Commands)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Select(m => new { m, attr = m.GetCustomAttribute<CommandMethodAttribute>() })
                    .Where(x => x.attr != null && !string.IsNullOrEmpty(x.attr.GlobalName))
                    .Select(x =>
                    {
                        string name = x.attr.GlobalName.ToUpperInvariant();
                        var cat = Catalog.FirstOrDefault(c => c.Name == name);
                        return new LhbCommandInfo
                        {
                            Name = name,
                            Description = cat.Description ?? "",
                            IsDebug = cat.Name != null && cat.IsDebug,
                            Flags = x.attr.Flags,
                            Method = x.m
                        };
                    })
                    .OrderBy(c => order.TryGetValue(c.Name, out int i) ? i : int.MaxValue)
                    .ThenBy(c => c.Name)
                    .ToList();
                return _commands;
            }
        }

        public static LhbCommandInfo FindCommand(string name) =>
            AllCommands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Phím tắt đang cấu hình (settings.json, chưa có thì mặc định), đã chuẩn hoá chữ hoa.</summary>
        public static List<CommandAlias> Current =>
            (SettingsManager.Current.CommandAliases ?? Defaults.ToList())
            .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Command) && !string.IsNullOrWhiteSpace(a.Alias))
            .Select(a => new CommandAlias { Command = Normalize(a.Command), Alias = Normalize(a.Alias) })
            .ToList();

        public static string Normalize(string name) => (name ?? "").Trim().ToUpperInvariant();

        /// <summary>Tên hợp lệ: bắt đầu bằng chữ A-Z, sau đó chữ / số / _ / -, tối đa 31 ký tự.</summary>
        public static bool IsValidName(string alias) => Regex.IsMatch(alias ?? "", @"^[A-Z][A-Z0-9_\-]{0,30}$");

        /// <summary>
        /// Tên đã là lệnh khác (AutoCAD, LISP, add-in khác, biến hệ thống) hoặc là tên lệnh của add-in -> lý do;
        /// trống -> null. Phím tắt của chính add-in đang đăng ký không tính là trùng.
        /// </summary>
        public static string FindCommandConflict(string alias)
        {
            if (FindCommand(alias) != null) return $"trùng tên lệnh {alias} của add-in";
            if (_registered.ContainsKey(alias)) return null;
            try
            {
                string kinds = InternalCommandKinds(alias);
                return kinds == null ? null : $"trùng {kinds} {alias}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PhimTat] IsCommandNameInUse('{alias}') lỗi: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // ===== v9.7 (D4): API nội bộ Autodesk.AutoCAD.Internal.Utils =====
        // Autodesk không cam kết giữ API này qua các phiên bản (đã có ở AutoCAD 2021 / 2025 / 2027: build 3 bản không lỗi).
        // Mọi chỗ dùng kiểu / hàm của nó nằm trong các hàm nhỏ KHÔNG inline dưới đây: AutoCAD sau này bỏ / đổi hàm thì
        // MissingMethodException / TypeLoadException ném ra đúng tại lời gọi, rơi vào try/catch của nơi gọi (ghi log, phím
        // tắt báo lỗi) thay vì làm hỏng cả hàm gọi nó lúc JIT biên dịch.

        /// <summary>Tên đang là lệnh / biến hệ thống -> mô tả loại ("lệnh AutoCAD"...), trống -> null.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string InternalCommandKinds(string alias)
        {
            var flags = Utils.IsCommandNameInUse(alias);
            if (flags == CommandTypeFlags.NoneCmd) return null;
            var kinds = new List<string>();
            if ((flags & CommandTypeFlags.CoreCmd) != 0) kinds.Add("lệnh AutoCAD");
            if ((flags & CommandTypeFlags.ARXCmd) != 0) kinds.Add("lệnh của add-in khác");
            if ((flags & CommandTypeFlags.LispCmd) != 0) kinds.Add("lệnh LISP");
            if ((flags & CommandTypeFlags.SetvarCmd) != 0) kinds.Add("biến hệ thống");
            if ((flags & CommandTypeFlags.ActionMacroCmd) != 0) kinds.Add("action macro");
            return kinds.Count > 0 ? string.Join(" / ", kinds) : flags.ToString();
        }

        /// <summary>Đăng ký lệnh thật tên alias trong nhóm LHB_PHIMTAT, gõ lệnh -> run(). Trả delegate phải giữ lại.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object InternalAddCommand(string alias, CommandFlags flags, Action run)
        {
            CommandCallback cb = () => run();
            Utils.AddCommand(GroupName, alias, alias, flags, cb);
            return cb;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InternalRemoveCommand(string alias) => Utils.RemoveCommand(GroupName, alias);

        /// <summary>Phím tắt trùng lệnh tắt trong acad.pgp (vd "C" -> CIRCLE) -> tên lệnh đó, không trùng -> null.</summary>
        public static string FindPgpCommand(string alias)
        {
            if (_pgp == null) _pgp = ReadPgp();
            return _pgp.TryGetValue(alias, out var cmd) ? cmd : null;
        }

        private static Dictionary<string, string> ReadPgp()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = HostApplicationServices.Current.FindFile("acad.pgp", HostApplicationServices.WorkingDatabase, FindFileHint.Default);
                foreach (var raw in File.ReadAllLines(path, Encoding.Default))
                {
                    // Dòng lệnh tắt: "C,        *CIRCLE" (bỏ dòng ghi chú ";" và dòng lệnh ngoài không có "*")
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";")) continue;
                    int comma = line.IndexOf(',');
                    if (comma <= 0) continue;
                    string rest = line.Substring(comma + 1).Trim();
                    if (!rest.StartsWith("*")) continue;
                    string cmd = rest.Substring(1).Split(';')[0].Trim();
                    map[line.Substring(0, comma).Trim()] = cmd;
                }
                Logger.Log($"[PhimTat] Đọc acad.pgp '{path}': {map.Count} lệnh tắt");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PhimTat] Không đọc được acad.pgp: {ex.Message}");
            }
            return map;
        }

        /// <summary>Gỡ rồi đăng ký lại toàn bộ phím tắt (lúc NETLOAD và sau khi lưu bảng lệnh). Trả số phím tắt dùng được.</summary>
        public static int RegisterAll()
        {
            UnregisterAll();
            Status.Clear();
            var list = Current;
            int ok = 0;
            foreach (var a in list)
            {
                if (_registered.ContainsKey(a.Alias))
                {
                    // Giữ trạng thái của lệnh đã nhận phím tắt này trước (không ghi đè "Đang dùng")
                    Logger.Warn($"[PhimTat] Bỏ phím tắt '{a.Alias}' -> {a.Command}: đã dùng cho lệnh {_registered[a.Alias]}");
                    continue;
                }
                var info = FindCommand(a.Command);
                string conflict;
                if (info == null) conflict = $"không có lệnh {a.Command}";
                else if (!IsValidName(a.Alias)) conflict = "tên không hợp lệ";
                else conflict = FindCommandConflict(a.Alias);

                if (conflict != null)
                {
                    Status[a.Alias] = "CHƯA dùng được: " + conflict;
                    Logger.Warn($"[PhimTat] Bỏ phím tắt '{a.Alias}' -> {a.Command}: {conflict}");
                    continue;
                }
                try
                {
                    string alias = a.Alias;
                    object cb = InternalAddCommand(alias, info.Flags, () => Run(info, alias));
                    _registered[alias] = info.Name;
                    _callbacks[alias] = cb;
                    Status[alias] = "Đang dùng";
                    ok++;
                }
                catch (Exception ex)
                {
                    // MissingMethodException / TypeLoadException = phiên bản AutoCAD này không còn API nội bộ (D4)
                    Status[a.Alias] = "Lỗi đăng ký: " + ex.Message;
                    Logger.Error(ex, $"[PhimTat] AddCommand '{a.Alias}' -> {a.Command}");
                }
            }
            Logger.Log($"[PhimTat] Đăng ký {ok}/{list.Count} phím tắt: " +
                       string.Join(", ", list.Select(a => $"{a.Alias}={a.Command} ({(Status.TryGetValue(a.Alias, out var s) ? s : "?")})")));
            return ok;
        }

        private static void UnregisterAll()
        {
            foreach (var alias in _registered.Keys.ToList())
            {
                try
                {
                    InternalRemoveCommand(alias);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[PhimTat] RemoveCommand '{alias}' lỗi: {ex.Message}");
                }
            }
            _registered.Clear();
            _callbacks.Clear();
        }

        /// <summary>Gõ phím tắt -> gọi thẳng hàm của lệnh gốc (lớp Commands không giữ trạng thái, tạo mới được).</summary>
        private static void Run(LhbCommandInfo info, string alias)
        {
            Logger.Log($"[PhimTat] Gõ '{alias}' -> chạy {info.Name}");
            try
            {
                object target = info.Method.IsStatic ? null : Activator.CreateInstance(info.Method.DeclaringType);
                info.Method.Invoke(target, null);
            }
            catch (TargetInvocationException ex)
            {
                Logger.Error(ex.InnerException ?? ex, $"[PhimTat] {alias} -> {info.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[PhimTat] {alias} -> {info.Name}");
            }
        }

        /// <summary>1 dòng tóm tắt phím tắt đang dùng, in ra dòng lệnh lúc nạp add-in.</summary>
        public static string Summary()
        {
            var used = Current.Where(a => Status.TryGetValue(a.Alias, out var s) && s == "Đang dùng").ToList();
            return used.Count == 0
                ? "Chưa có phím tắt. Gõ LHBLENH để đặt phím tắt cho các lệnh."
                : "Phím tắt: " + string.Join(", ", used.Select(a => $"{a.Alias} = {a.Command}")) + ". Gõ LHBLENH để đổi.";
        }

        /// <summary>Phần phím tắt trong file LHBDIAG.</summary>
        public static void AppendDiag(StringBuilder sb)
        {
            sb.AppendLine($"Nguồn: {(SettingsManager.Current.CommandAliases == null ? "mặc định (settings.json chưa có CommandAliases)" : "settings.json")}");
            foreach (var a in Current)
                sb.AppendLine($"  {a.Alias,-12} -> {a.Command,-14} : {(Status.TryGetValue(a.Alias, out var s) ? s : "(chưa đăng ký)")}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace LHBBlockScheduler.Core
{
    public class AppSettings
    {
        public double TableScale { get; set; } = 100.0;
        /// <summary>Số bit khác nhau tối đa của ShapeHash để coi 2 block giống hình (Gợi ý gộp, Soát lỗi đếm).</summary>
        public int HashThreshold { get; set; } = 5;
        public int ScanDepth { get; set; } = 2;
        public bool CountParentBlocks { get; set; } = false;
        public bool SplitByVisibility { get; set; } = true;
        public bool SplitByLayer { get; set; } = false;
        /// <summary>true = đã chuyển thư viện thiết bị cũ (Libraries\*.json) sang bộ block mẫu "TV cu ..." (v9.1).</summary>
        public bool DeviceLibrariesMigrated { get; set; }
        /// <summary>Kiểu bảng xuất: "AutoCAD Table" (mặc định) hoặc "Line + Text (cũ)".</summary>
        public string TableKind { get; set; } = "AutoCAD Table";
        public List<string> Units { get; set; } = new List<string> { "Cái", "Bộ", "Mét", "Cuộn", "Hộp" };
        public Dictionary<string, bool> ColumnVisibility { get; set; } = new Dictionary<string, bool>();
        /// <summary>Tên cột người dùng tự đặt (double-click tiêu đề cột), key = tên nội bộ cột (colDisplayName...).</summary>
        public Dictionary<string, string> ColumnHeaders { get; set; } = new Dictionary<string, string>();
        /// <summary>Sai số vị trí (đơn vị bản vẽ): 2 block cùng tên + chủng loại có điểm chèn cách nhau không quá mức này = trùng.</summary>
        public double DuplicateTolerance { get; set; } = 1.0;
        /// <summary>2 block cùng tên có khung bao che lấp nhau từ mức này trở lên (% diện tích block nhỏ hơn) = trùng.</summary>
        public double DuplicateOverlapPercent { get; set; } = 10.0;
        /// <summary>
        /// true = vẫn đếm block trùng vào SL. Đặt tên ngược (mặc định false = KHÔNG đếm trùng) vì
        /// DataContractJsonSerializer không chạy property initializer: settings.json cũ thiếu field sẽ ra false.
        /// </summary>
        public bool CountDuplicateBlocks { get; set; } = false;
        /// <summary>
        /// true = quét chọn mọi block. Mặc định false = CHỈ quét block mẫu (khi bộ mẫu có block). Đặt tên ngược
        /// vì DataContractJsonSerializer không chạy property initializer (settings.json cũ thiếu field -> false).
        /// </summary>
        public bool ScanAllBlocks { get; set; } = false;
        /// <summary>Bộ block mẫu đang dùng (tên file trong thư mục ThuVienMau).</summary>
        public string CurrentTemplateSet { get; set; } = "Data1";
        /// <summary>
        /// Phím tắt lệnh (bảng LHBLENH). null = chưa từng đặt -> dùng phím tắt mặc định
        /// (CommandAliasManager.Defaults); danh sách rỗng = user đã xoá hết phím tắt.
        /// </summary>
        public List<CommandAlias> CommandAliases { get; set; }

        // ============================== PREMIUM (v9) ==============================
        /// <summary>1 đơn vị bản vẽ = ? mm (chiều dài ống, bán kính bảo vệ). 0 = tự theo biến INSUNITS của bản vẽ.</summary>
        public double MmPerDrawingUnit { get; set; }
        /// <summary>% hao hụt cộng thêm vào chiều dài ống / dây.</summary>
        public double LengthWastePercent { get; set; }
        /// <summary>Tên thống kê chiều dài theo layer (key = tên layer).</summary>
        public Dictionary<string, string> LayerDisplayNames { get; set; } = new Dictionary<string, string>();
        /// <summary>Bán kính bảo vệ (m) theo loại thiết bị (key = tên block||chủng loại).</summary>
        public Dictionary<string, double> CoverageRadii { get; set; } = new Dictionary<string, double>();
        /// <summary>Tiền tố đánh số theo loại thiết bị (key = tên block||chủng loại).</summary>
        public Dictionary<string, string> NumberingPrefixes { get; set; } = new Dictionary<string, string>();
        /// <summary>Cột thuộc tính đang hiện (khoá "A:TAG" / "D:Tên").</summary>
        public List<string> AttributeColumns { get; set; } = new List<string>();
        /// <summary>Thuộc tính dùng để tách dòng.</summary>
        public List<string> SplitAttributeKeys { get; set; } = new List<string>();
        /// <summary>true = ẩn cột SL theo khu vực dù bản vẽ có khu vực. Mặc định false = hiện.</summary>
        public bool HideZoneColumns { get; set; }
        /// <summary>Mẫu bảng xuất đã lưu.</summary>
        public List<TableTemplate> TableTemplates { get; set; } = new List<TableTemplate>();
        public string CurrentTableTemplate { get; set; }
        /// <summary>Mã kích hoạt Premium.</summary>
        public string LicenseKey { get; set; }
        /// <summary>true = không tạo tab Ribbon LHB (mặc định false = có tab).</summary>
        public bool DisableRibbon { get; set; }
    }

    public static class SettingsManager
    {
        public static string SettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "LHBBlockScheduler", "settings.json");

        private static AppSettings _current;

        public static AppSettings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = LoadSettings();
                }
                return _current;
            }
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonHelper.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        // DataContractJsonSerializer không chạy property initializer -> bù giá trị mặc định
                        var def = new AppSettings();
                        if (settings.Units == null || settings.Units.Count == 0) settings.Units = def.Units;
                        if (settings.ColumnVisibility == null) settings.ColumnVisibility = def.ColumnVisibility;
                        if (settings.ColumnHeaders == null) settings.ColumnHeaders = def.ColumnHeaders;
                        if (settings.TableScale <= 0) settings.TableScale = def.TableScale;
                        if (settings.HashThreshold <= 0) settings.HashThreshold = def.HashThreshold;
                        if (settings.ScanDepth <= 0) settings.ScanDepth = def.ScanDepth;
                        if (settings.DuplicateTolerance <= 0) settings.DuplicateTolerance = def.DuplicateTolerance;
                        if (settings.DuplicateOverlapPercent <= 0) settings.DuplicateOverlapPercent = def.DuplicateOverlapPercent;
                        if (string.IsNullOrWhiteSpace(settings.CurrentTemplateSet)) settings.CurrentTemplateSet = def.CurrentTemplateSet;
                        settings.LayerDisplayNames ??= new Dictionary<string, string>();
                        settings.CoverageRadii ??= new Dictionary<string, double>();
                        settings.NumberingPrefixes ??= new Dictionary<string, string>();
                        settings.AttributeColumns ??= new List<string>();
                        settings.SplitAttributeKeys ??= new List<string>();
                        settings.TableTemplates ??= new List<TableTemplate>();
                        if (settings.MmPerDrawingUnit < 0) settings.MmPerDrawingUnit = 0;
                        if (settings.LengthWastePercent < 0) settings.LengthWastePercent = 0;
                        Logger.Log($"SettingsManager: đã đọc settings.json (Units={settings.Units.Count}, bộ mẫu='{settings.CurrentTemplateSet}', ScanDepth={settings.ScanDepth})");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SettingsManager.LoadSettings");
            }
            return new AppSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                string path = SettingsFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonHelper.Serialize(Current);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                Logger.Log($"SettingsManager: đã lưu cấu hình vào '{path}' ({json.Length} ký tự)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SettingsManager.SaveSettings");
            }
        }
    }
}

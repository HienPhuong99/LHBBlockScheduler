using System;
using System.Linq;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Mẫu bảng xuất (Premium P10): tiêu đề, dòng phụ, dòng tổng, font, cỡ chữ, màu tiêu đề / header.
    /// Lưu trong settings.json (TableTemplates). DataContractJsonSerializer không chạy property initializer ->
    /// số = 0 / chuỗi rỗng nghĩa là "mặc định", bool đặt tên sao cho mặc định = false.
    /// </summary>
    public class TableTemplate
    {
        public const string DefaultTitle = "BẢNG THỐNG KÊ SỐ LƯỢNG BLOCK";
        public const string DefaultName = "Mặc định";

        public string Name { get; set; }
        /// <summary>Tiêu đề bảng ("" = BẢNG THỐNG KÊ SỐ LƯỢNG BLOCK).</summary>
        public string Title { get; set; }
        /// <summary>Dòng phụ dưới tiêu đề (tên công trình, hạng mục...). "" = không có.</summary>
        public string Subtitle { get; set; }
        public bool HideTitle { get; set; }
        /// <summary>Thêm dòng TỔNG CỘNG cuối bảng (cộng cột SL và SL theo khu vực).</summary>
        public bool AddTotalRow { get; set; }
        public string TotalLabel { get; set; }
        public bool UppercaseHeader { get; set; }
        /// <summary>File font của kiểu chữ bảng ("" = arial.ttf).</summary>
        public string FontFile { get; set; }
        /// <summary>Hệ số cỡ chữ / chiều cao dòng (0 = 1).</summary>
        public double TextHeightFactor { get; set; }
        public double RowHeightFactor { get; set; }
        /// <summary>Màu nền hàng header / tiêu đề (mã màu AutoCAD 1-255, 0 = không tô).</summary>
        public short HeaderColorIndex { get; set; }
        public short TitleColorIndex { get; set; }

        public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title.Trim();
        public string EffectiveTotalLabel => string.IsNullOrWhiteSpace(TotalLabel) ? "TỔNG CỘNG" : TotalLabel.Trim();
        public string EffectiveFont => string.IsNullOrWhiteSpace(FontFile) ? "arial.ttf" : FontFile.Trim();
        public double EffectiveTextFactor => TextHeightFactor > 0 ? TextHeightFactor : 1.0;
        public double EffectiveRowFactor => RowHeightFactor > 0 ? RowHeightFactor : 1.0;

        public TableTemplate Clone() => (TableTemplate)MemberwiseClone();

        /// <summary>Mẫu đang chọn trong settings; chưa có thì mẫu mặc định (giống bảng các bản trước).</summary>
        public static TableTemplate Current
        {
            get
            {
                var s = SettingsManager.Current;
                var t = s.TableTemplates?.FirstOrDefault(x => string.Equals(x.Name, s.CurrentTableTemplate, StringComparison.OrdinalIgnoreCase));
                return t ?? new TableTemplate { Name = DefaultName };
            }
        }

        /// <summary>Tên kiểu chữ AutoCAD cho font này (mỗi font 1 kiểu chữ riêng, arial giữ tên cũ LHB_TABLE).</summary>
        public string TextStyleName
        {
            get
            {
                string f = EffectiveFont;
                if (string.Equals(f, "arial.ttf", StringComparison.OrdinalIgnoreCase)) return "LHB_TABLE";
                var clean = new string(System.IO.Path.GetFileNameWithoutExtension(f).Where(char.IsLetterOrDigit).ToArray());
                return "LHB_TABLE_" + (clean.Length > 0 ? clean.ToUpperInvariant() : "FONT");
            }
        }
    }
}

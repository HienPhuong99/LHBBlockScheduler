# LHB Block Scheduler Premium

Add-in AutoCAD (.NET, C#) thống kê block thiết bị (PCCC, điện, nước...): quét chọn, gộp nhóm, thư viện block mẫu, tìm block trùng, xuất bảng AutoCAD Table / Excel.

- Hướng dẫn sử dụng: [HUONG_DAN_SU_DUNG.md](HUONG_DAN_SU_DUNG.md)
- Tính năng Premium (từ v9): [TINH_NANG_PREMIUM.md](TINH_NANG_PREMIUM.md)
- Hướng dẫn test từng bản: thư mục [Dist](Dist) (bản mới nhất: v9.3 Premium — Premium miễn phí, chưa bật bản quyền)
- Bản đóng gói (zip): mục **Releases** của repo
- Review chức năng, đề xuất tính năng, kế hoạch thương mại hoá (29/09/2026): [docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md](docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md)

## Tính năng chính

| Nhóm | Tính năng |
|---|---|
| Thống kê | Quét chọn block (kể cả block lồng, dynamic block theo chủng loại), gộp nhóm, gợi ý gộp theo hình, quét thêm, thư viện block mẫu (chỉ quét block mẫu), tìm block trùng / che lấp |
| Xuất bảng | AutoCAD Table có ký hiệu block trong ô (đều cỡ), bảng Line + Text, căn lề từng ô, độ rộng cột theo form |
| Premium | Theo tầng / khu vực, bảng tự cập nhật, nhiều bản vẽ, chiều dài ống / dây, cột thuộc tính, soát lỗi đếm, đánh số thiết bị, vùng bảo vệ PCCC, Excel có ảnh, mẫu bảng, Ribbon + palette, thay block hàng loạt, bản quyền |
| Tiện ích | Bảng lệnh + đổi phím tắt (`LHBLENH`), LISP nạp đúng bản theo MD5, chẩn đoán `LHBDIAG` |

## Cài đặt (người dùng)

1. Tải zip ở mục Releases, giải nén vào 1 thư mục.
2. Mở AutoCAD (2021 trở lên), kéo thả file `LHB.lsp` vào bản vẽ.
3. Gõ `LHBSCAN` hoặc dùng tab Ribbon **LHB Premium**.

## Build (lập trình viên)

- .NET SDK + .NET Framework 4.8 Developer Pack. Target `net48`, `x64`. Không dùng NuGet / thư viện ngoài trong DLL add-in:
  mọi thứ (JSON, Excel, Ribbon) viết bằng thư viện có sẵn của .NET Framework để không xung đột DLL với AutoCAD.
- Chép 3 file `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll` từ thư mục cài AutoCAD 2021 vào `libs\` (không đưa lên repo vì là file của Autodesk). Giữ `<Private>false</Private>`.
- `dotnet build -c Release`, đóng gói: `powershell -ExecutionPolicy Bypass -File build.ps1` → `Dist\LHBBlockScheduler\`.
- `tools\LHBKeyGen`: công cụ tạo mã kích hoạt Premium. Khoá bí mật `LHB_private_key.xml` **không** nằm trong repo.
- Quy tắc code, ghi chú kỹ thuật, trạng thái từng bản: [CLAUDE.md](CLAUDE.md).

## Cấu trúc

```
Commands.cs              Lệnh AutoCAD (LHBSCAN, LHBMAU, LHBKHUVUC...)
Core/                    Quét block, xuất bảng, thư viện mẫu, Premium (ZoneManager, TableUpdater, XlsxWriter, ...)
Models/                  BlockItem, BlockInstanceRef, TemplateLibrary...
UI/                      Form thống kê, hộp thoại, Ribbon, palette
LHB.lsp                  LISP nạp add-in đúng bản (kéo thả vào AutoCAD)
tools/LHBKeyGen/         Tạo mã kích hoạt (chỉ người bán)
```

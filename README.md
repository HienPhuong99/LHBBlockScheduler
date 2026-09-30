# LHB Block Scheduler Premium

Add-in AutoCAD (.NET, C#) thống kê block thiết bị (PCCC, điện, nước...): quét chọn, gộp nhóm, thư viện block mẫu, tìm block trùng, xuất bảng AutoCAD Table / Excel.

- Hướng dẫn sử dụng: [HUONG_DAN_SU_DUNG.md](HUONG_DAN_SU_DUNG.md)
- Tính năng Premium (từ v9): [TINH_NANG_PREMIUM.md](TINH_NANG_PREMIUM.md)
- Hướng dẫn test từng bản: thư mục [Dist](Dist) (bản mới nhất: v9.5 Premium — bản quyền v2: bật bản quyền, dùng thử Premium 30 ngày, mã `LHB2-...`, key review trọn đời; v9.4 "ổn định" sửa đếm ARRAY / MINSERT / XREF)
- Bản đóng gói (zip): mục **Releases** của repo
- Review chức năng, đề xuất tính năng, kế hoạch thương mại hoá (29/09/2026): [docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md](docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md)
- Bản quyền v2, mức an toàn, cấp và quản lý key (30/09/2026): [docs/BAN_QUYEN_VA_CAP_KEY.md](docs/BAN_QUYEN_VA_CAP_KEY.md)

## Tính năng chính

| Nhóm | Tính năng |
|---|---|
| Thống kê | Quét chọn block (kể cả block lồng, dynamic block theo chủng loại), gộp nhóm, gợi ý gộp theo hình, quét thêm, thư viện block mẫu (chỉ quét block mẫu), tìm block trùng / che lấp |
| Xuất bảng | AutoCAD Table có ký hiệu block trong ô (đều cỡ), bảng Line + Text, căn lề từng ô, độ rộng cột theo form |
| Premium | Theo tầng / khu vực, bảng tự cập nhật, nhiều bản vẽ, chiều dài ống / dây, cột thuộc tính, soát lỗi đếm, đánh số thiết bị, vùng bảo vệ PCCC, Excel có ảnh, mẫu bảng, Ribbon + palette, thay block hàng loạt, bản quyền |
| Tiện ích | Bảng lệnh + đổi phím tắt (`LHBLENH`), LISP nạp đúng bản theo MD5, chẩn đoán `LHBDIAG` |

## Cài đặt (người dùng)

1. Tải zip ở mục Releases, giải nén vào 1 thư mục.
2. Mở AutoCAD 2021 – 2024, kéo thả file `LHB.lsp` vào bản vẽ (AutoCAD 2025+ chạy .NET 8/10, chưa hỗ trợ).
3. Gõ `LHBSCAN` hoặc dùng tab Ribbon **LHB Premium**.

## Build (lập trình viên)

- .NET SDK + .NET Framework 4.8 Developer Pack. Target `net48`, `x64`. Không dùng NuGet / thư viện ngoài trong DLL add-in:
  mọi thứ (JSON, Excel, Ribbon) viết bằng thư viện có sẵn của .NET Framework để không xung đột DLL với AutoCAD.
- Chép 3 file `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll` từ thư mục cài AutoCAD 2021 vào `libs\` (không đưa lên repo vì là file của Autodesk). Giữ `<Private>false</Private>`.
- `dotnet build -c Release`, đóng gói: `powershell -ExecutionPolicy Bypass -File build.ps1` → `Dist\LHBBlockScheduler\`.
- `tools\LHBKeyGen`: LHBKeyGen v2 cấp mã kích hoạt Premium `LHB2-...` (menu + lệnh, sổ key, thu hồi), chạy cả trên GitHub Actions
  (`.github/workflows/lhb-cap-key.yml`). Khoá ký `LHB_SIGNING_KEY_kid*.txt` và sổ key **không** nằm trong repo.
- `tests\LHBLicenseTests`: kiểm thử bản quyền ngoài AutoCAD (`dotnet run --project tests/LHBLicenseTests`).
- Quy tắc code, ghi chú kỹ thuật, trạng thái từng bản: [CLAUDE.md](CLAUDE.md).

## Cấu trúc

```
Commands.cs              Lệnh AutoCAD (LHBSCAN, LHBMAU, LHBKHUVUC...)
Core/                    Quét block, xuất bảng, thư viện mẫu, Premium (ZoneManager, TableUpdater, XlsxWriter, ...)
Models/                  BlockItem, BlockInstanceRef, TemplateLibrary...
UI/                      Form thống kê, hộp thoại, Ribbon, palette
LHB.lsp                  LISP nạp add-in đúng bản (kéo thả vào AutoCAD)
tools/LHBKeyGen/         Cấp mã kích hoạt LHB2 (chỉ người bán), xem README trong thư mục
tests/LHBLicenseTests/   Kiểm thử bản quyền (không cần AutoCAD)
.github/workflows/       Cấp key trên GitHub Actions
docs/                    Review + kế hoạch thương mại hoá, bản quyền + cấp key
```

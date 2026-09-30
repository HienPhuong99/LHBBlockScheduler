# LHB Block Scheduler Premium

Add-in AutoCAD (.NET, C#) thống kê block thiết bị (PCCC, điện, nước...): quét chọn, gộp nhóm, thư viện block mẫu, tìm block trùng, xuất bảng AutoCAD Table / Excel.

- Hướng dẫn sử dụng: [HUONG_DAN_SU_DUNG.md](HUONG_DAN_SU_DUNG.md)
- Tính năng Premium (từ v9): [TINH_NANG_PREMIUM.md](TINH_NANG_PREMIUM.md)
- Hướng dẫn test từng bản: thư mục [Dist](Dist) (bản mới nhất: v9.7 Premium — chạy AutoCAD 2021 – 2027: 3 bản DLL, `LHB.lsp` tự chọn theo phiên bản AutoCAD; v9.6 chủng loại block động không còn "Distance1=...", tách theo kích thước, `LHBKHOPCOT`; v9.5 bản quyền v2; v9.4 "ổn định" sửa đếm ARRAY / MINSERT / XREF)
- Bản đóng gói (zip, có sẵn hướng dẫn test bên trong): thư mục [Dist](Dist) (từ v9.5); v9 – v9.4: mục **Releases**
- Review chức năng, đề xuất tính năng, kế hoạch thương mại hoá (29/09/2026): [docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md](docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md)
- Bản quyền v2, mức an toàn, cấp và quản lý key (30/09/2026): [docs/BAN_QUYEN_VA_CAP_KEY.md](docs/BAN_QUYEN_VA_CAP_KEY.md)

## Tính năng chính

| Nhóm | Tính năng |
|---|---|
| Thống kê | Quét chọn block (kể cả block lồng, dynamic block theo chủng loại), tách theo kích thước (tham số độ dài block động), gộp nhóm, gợi ý gộp theo hình, quét thêm, thư viện block mẫu (chỉ quét block mẫu), tìm block trùng / che lấp |
| Xuất bảng | AutoCAD Table có ký hiệu block trong ô (đều cỡ), bảng Line + Text, căn lề từng ô, độ rộng cột theo chữ / theo form, khớp lại cột như Excel (`LHBKHOPCOT`) |
| Premium | Theo tầng / khu vực, bảng tự cập nhật, nhiều bản vẽ, chiều dài ống / dây, cột thuộc tính, soát lỗi đếm, đánh số thiết bị, vùng bảo vệ PCCC, Excel có ảnh, mẫu bảng, Ribbon + palette, thay block hàng loạt, bản quyền |
| Tiện ích | Bảng lệnh + đổi phím tắt (`LHBLENH`), LISP nạp đúng bản theo MD5, chẩn đoán `LHBDIAG` |

## Cài đặt (người dùng)

1. Tải zip bản mới nhất trong thư mục `Dist`, giải nén vào 1 thư mục.
2. Mở AutoCAD 2021 – 2027, kéo thả file `LHB.lsp` vào bản vẽ (tự nạp đúng bản: thư mục gốc = 2021 – 2024, `net8` = 2025 – 2026, `net10` = 2027).
3. Gõ `LHBSCAN` hoặc dùng tab Ribbon **LHB Premium**.

## Build (lập trình viên)

- .NET SDK + .NET Framework 4.8 Developer Pack. `x64`. Không dùng thư viện ngoài trong DLL add-in: mọi thứ (JSON, Excel,
  Ribbon) viết bằng thư viện có sẵn của .NET để không xung đột DLL với AutoCAD.
- 3 đích (v9.7): `net48` (AutoCAD 2021 – 2024) tham chiếu 3 file `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll` chép từ thư mục
  cài AutoCAD 2021 vào `libs\` (không đưa lên repo vì là file của Autodesk, giữ `<Private>false</Private>`); `net8.0-windows`
  (2025 – 2026) và `net10.0-windows` (2027) tham chiếu gói NuGet chính thức `AutoCAD.NET` 25.0.1 / 26.0.0 **chỉ để biên dịch**
  (`ExcludeAssets="runtime"`, không chép DLL AutoCAD nào ra thư mục add-in). Cần .NET 10 SDK.
- `dotnet build -c Release` chỉ build `net48`; `dotnet build -c Release -p:LhbAllTargets=true` build cả 3 → `bin\Release\net48|net8|net10`.
  Đóng gói: `powershell -ExecutionPolicy Bypass -File build.ps1` (tự build cả 3 khi máy có .NET 10 SDK) → `Dist\LHBBlockScheduler\`;
  máy không có Windows: `python3 tools/pack_cloud.py stage` rồi `zip <yyyyMMdd>`.
- `tools\LHBKeyGen`: LHBKeyGen v2 cấp mã kích hoạt Premium `LHB2-...` (menu + lệnh, sổ key, thu hồi), chạy cả trên GitHub Actions
  (`.github/workflows/lhb-cap-key.yml`). Khoá ký `LHB_SIGNING_KEY_kid*.txt` và sổ key **không** nằm trong repo.
- `tests\LHBLicenseTests`: kiểm thử bản quyền ngoài AutoCAD (`dotnet run --project tests/LHBLicenseTests`);
  `tests\LHBCoreTests`: hàm thuần (kích thước block động, khớp block mẫu, thư mục gốc add-in).
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
tests/LHBCoreTests/      Kiểm thử hàm thuần của add-in (không cần AutoCAD)
tools/pack_cloud.py      Đóng gói trên máy không có Windows (như build.ps1)
.github/workflows/       Cấp key trên GitHub Actions
docs/                    Review + kế hoạch thương mại hoá, bản quyền + cấp key
```

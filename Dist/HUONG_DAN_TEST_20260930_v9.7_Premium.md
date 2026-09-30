# Hướng dẫn test LHBBlockScheduler — bản 30/09/2026 (v9.7 Premium — chạy AutoCAD 2021 – 2027)

**File cần nhận:** `LHBBlockScheduler_20260930_v9.7_Premium.zip` (trong zip có sẵn file hướng dẫn này; zip cũng nằm ở thư mục `Dist` trên GitHub)
**Máy test:** AutoCAD 2021 (máy chính, mục A). Đồng nghiệp có AutoCAD 2025 / 2026 / 2027: nhờ làm thêm mục B.

**MD5 DLL đúng** (mỗi dòng AutoCAD 1 bản, `LHB.lsp` tự chọn):

| AutoCAD | File trong thư mục `LHBBlockScheduler` | MD5 |
|---|---|---|
| 2021 – 2024 | `LHBBlockScheduler.dll` | `@@MD5_NET48@@` |
| 2025 – 2026 | `net8\LHBBlockScheduler.dll` | `@@MD5_NET8@@` |
| 2027 | `net10\LHBBlockScheduler.dll` | `@@MD5_NET10@@` |

v9.7 = v9.6 (chủng loại block động, tách theo kích thước, `LHBKHOPCOT`) + chạy được AutoCAD 2025 – 2027. Tính năng và bản
quyền không đổi: máy đã kích hoạt key review vẫn đang kích hoạt; cùng 1 máy thì AutoCAD 2021 và 2025 dùng chung key,
dùng thử, cài đặt.

> Bản này build trên máy build tự động, tham chiếu gói chính thức `AutoCAD.NET` của Autodesk: 24.0 (AutoCAD 2021) cho bản
> 2021 – 2024, 25.0.1 (AutoCAD 2025) cho bản 2025 – 2026, 26.0.0 (AutoCAD 2027) cho bản 2027. Nếu NETLOAD / kéo thả
> `LHB.lsp` báo lỗi nạp: gửi ảnh dòng lệnh + file `LHBDIAG_*.txt`.

## Bản này sửa gì

| # | Vấn đề | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| 1 | AutoCAD 2025 trở lên **không chạy được** add-in (ghi trong hướng dẫn từ v9.4) | AutoCAD 2025 – 2026 chạy .NET 8, AutoCAD 2027 chạy .NET 10; add-in chỉ build cho .NET Framework 4.8 (AutoCAD 2021 – 2024) | Build **3 bản DLL** từ cùng mã nguồn. Zip có thêm thư mục `net8`, `net10`. `LHB.lsp` đọc phiên bản AutoCAD (ACADVER), nạp đúng bản, kiểm MD5 từng bản |
| 2 | Trên AutoCAD 2025+ chữ trên form to hơn, dễ tràn nút | .NET 8 đổi font mặc định của form sang Segoe UI 9 | 5 form không đặt font riêng (form thống kê, block mẫu, bảng lệnh, block trùng, gợi ý gộp) dùng lại đúng font mặc định như bản 2021 |
| 3 | Bản 2025+ nằm thư mục con → thư viện block mẫu, log, LHBDIAG sẽ tách riêng | DLL ở `net8` / `net10` | Luôn ở **thư mục gốc** add-in (cạnh `LHB.lsp`), dùng chung cho mọi phiên bản AutoCAD |
| 4 | NETLOAD tay nhầm bản (vd bản 2021 vào AutoCAD 2025) chạy sai từng phần, khó hiểu | — | Add-in tự báo `[LHB CẢNH BÁO] DLL này là bản cho ... nhưng AutoCAD đang chạy là R25` |
| 5 | Phím tắt `LHBLENH` dùng API nội bộ của AutoCAD (review D4) | Autodesk không cam kết giữ API này qua các phiên bản | Tách riêng lời gọi: AutoCAD sau này bỏ API thì chỉ phím tắt báo lỗi, các lệnh khác vẫn chạy |
| 6 | `LHBVERSION`, `LHBDIAG` không cho biết đang chạy bản nào | — | Thêm dòng "bản DLL cho AutoCAD ...", phiên bản AutoCAD, .NET đang chạy, thư mục gốc add-in |

Cấu trúc thư mục sau khi giải nén:

```
LHBBlockScheduler\
  LHB.lsp                    <- kéo thả file này (mọi phiên bản AutoCAD)
  LHBBlockScheduler.dll      <- AutoCAD 2021 - 2024
  build-info.txt
  net8\LHBBlockScheduler.dll <- AutoCAD 2025 - 2026
  net10\LHBBlockScheduler.dll<- AutoCAD 2027
  ThuVienMau\, log.txt, LHBDIAG_*.txt  (tạo khi chạy, dùng chung)
```

## A. Máy AutoCAD 2021 (máy test chính)

| # | Thao tác | Kết quả đúng |
|---|---|---|
| A1 | Tắt hẳn AutoCAD, giải nén zip vào **thư mục mới** | Thư mục `LHBBlockScheduler` có `LHB.lsp`, `LHBBlockScheduler.dll`, 2 thư mục `net8`, `net10` |
| A2 | Mở AutoCAD 2021 + bản vẽ, kéo thả `LHB.lsp` | Dòng lệnh: `[LHB] Đã nạp bản cho AutoCAD 2021 - 2024: ...\LHBBlockScheduler\LHBBlockScheduler.dll`, `MD5: ... (đúng bản)`, đầu danh sách lệnh có `(bản cho AutoCAD 2021 - 2024)` |
| A3 | `LHBVERSION` | `Phiên bản : v9.7 Premium, bản DLL cho AutoCAD 2021 - 2024 (.NET Framework 4.8)`; `AutoCAD / .NET : 24.0... / .NET Framework 4.8...`; MD5 = dòng 2021 – 2024 ở bảng trên |
| A4 | `LHBSCAN` quét như thường ngày, xuất bảng, mở **Block mẫu...** | Như v9.6: giao diện, cỡ chữ không đổi, tiêu đề form `Thống kê Block v9.7 Premium`. Thư viện block mẫu đủ như cũ (tự chép từ bản dự phòng sang thư mục mới) |
| A5 | `LHBDIAG` | File `LHBDIAG_*.txt` ngay trong thư mục `LHBBlockScheduler`; mục 1 có `Bản DLL cho : AutoCAD 2021 - 2024 (.NET Framework 4.8)`, `Thư mục gốc add-in : ...\LHBBlockScheduler` |

Chưa test v9.6 / v9.5 / v9.4: làm thêm các bước trong `HUONG_DAN_TEST_20260930_v9.6_Premium.md` (đầu báo tia chiếu,
tách theo kích thước, `LHBKHOPCOT`), `HUONG_DAN_TEST_20260930_v9.5_Premium.md` (bản quyền, key review) và
`HUONG_DAN_TEST_20260929_v9.4_Premium.md` (ARRAY, MINSERT, XREF). Các bước đó dùng luôn bản v9.7 này.

## B. Máy AutoCAD 2025 / 2026 / 2027 (nếu đồng nghiệp có)

| # | Thao tác | Kết quả đúng |
|---|---|---|
| B1 | Chép cả thư mục `LHBBlockScheduler` (hoặc giải nén zip) sang máy đó. Mở AutoCAD + bản vẽ, kéo thả `LHB.lsp`, bấm **Load** nếu hỏi bảo mật | AutoCAD 2025 / 2026: `[LHB] Đã nạp bản cho AutoCAD 2025 - 2026: ...\net8\LHBBlockScheduler.dll`; AutoCAD 2027: `...\net10\...`; `MD5: ... (đúng bản)` |
| B2 | `LHBVERSION` | `bản DLL cho AutoCAD 2025 - 2026 (.NET 8)` (2027: `AutoCAD 2027 (.NET 10)`); dòng `.NET` là `.NET 8.0...` hoặc `.NET 10.0...` (AutoCAD 2025 / 2026 đã cập nhật lên .NET 10 vẫn dùng bản net8 — đúng); MD5 khớp bảng trên |
| B3 | `LHBBANQUYEN` | Hiện mã máy dạng `XXXX-XXXX-XXXX-XXXX`, trạng thái dùng thử; dán key review → kích hoạt được |
| B4 | `LHBSCAN` quét 1 vùng; thử Tách theo chủng loại, Tách theo kích thước, Gộp, Block mẫu..., Tìm trùng | Form hiện đủ, chữ không tràn nút, ảnh ký hiệu hiện, số lượng giống khi đếm cùng vùng trên AutoCAD 2021 |
| B5 | **Xuất bảng** (AutoCAD Table), **Xuất Excel** | Bảng có ký hiệu đều cỡ trong ô; Excel tự mở file |
| B6 | Tab Ribbon **LHB Premium**, `LHBPALETTE`, `LHBLENH` rồi gõ phím tắt `LHB` | Ribbon hiện nút, palette dock được, phím tắt mở bảng lệnh |
| B7 | `LHBDIAG` | File nằm ở thư mục gốc `LHBBlockScheduler` (không nằm trong `net8`); mục 1 có `Bản DLL cho : AutoCAD 2025 - 2026 (.NET 8)` |
| B8 | *(Tuỳ chọn)* Tắt hẳn AutoCAD, mở lại, gõ `NETLOAD` chọn `LHBBlockScheduler.dll` ở thư mục **gốc** (bản 2021 – 2024) | Hoặc không nạp được, hoặc dòng lệnh báo `[LHB CẢNH BÁO] DLL này là bản cho AutoCAD 2021 - 2024 (.NET Framework 4.8) nhưng AutoCAD đang chạy là R25`. Tắt hẳn AutoCAD sau bước này |

## Cần gửi về

1. AutoCAD 2021: ảnh dòng lệnh bước A2 + A3, file `LHBDIAG_*.txt` bước A5.
2. AutoCAD 2025 / 2026 / 2027 (nếu có): ảnh dòng lệnh B1 + B2, ảnh form B4, file `LHBDIAG_*.txt` bước B7, ghi rõ phiên bản AutoCAD.
3. Kéo thả `LHB.lsp` báo `[LHB LỖI] ...` hoặc lệnh nào lỗi / chữ tràn: ảnh + `LHBDIAG`.

# Hướng dẫn test LHBBlockScheduler — bản 03/11/2026 (v9.8 Premium — tạm tắt bản quyền, nhận khoá ký kid 2)

**File cần nhận:** `LHBBlockScheduler_20261103_v9.8_Premium.zip` (trong zip có sẵn file hướng dẫn này; zip cũng nằm ở thư mục `Dist` trên GitHub)
và (không bắt buộc) `KEY_PREMIUM_LHB_2166E9F8.txt` — key Premium trọn đời của bạn, **không** nằm trong zip, không có trên GitHub.
Cả 2 file ở `C:\Users\vsp\Downloads\tool\`.
**Máy test:** AutoCAD 2021 (máy chính, mục A). Đồng nghiệp có AutoCAD 2025 / 2026: mục B.

**MD5 DLL đúng** (`LHB.lsp` tự chọn bản theo phiên bản AutoCAD):

| AutoCAD | File trong thư mục `LHBBlockScheduler` | MD5 |
|---|---|---|
| 2021 – 2024 | `LHBBlockScheduler.dll` | `5998A1821C4A05B2EA2B4AB0F1C6FB8C` |
| 2025 – 2026 | `net8\LHBBlockScheduler.dll` | `D39AD5D70420E13814985245F32F0EDC` |
| 2027 | — (gói này **chưa có**, xem mục C) | — |

v9.8 = v9.7 + **tạm tắt bản quyền** (Premium miễn phí cho mọi máy, như v9.3 – v9.4) + add-in nhận **khoá ký kid 2**.
Tính năng không đổi. Chưa có bản cho AutoCAD 2027.

> Bản này build trên máy dev tại commit `e307737` (bản 2021 – 2024 tham chiếu DLL AutoCAD 2021 trong `libs\`, bản 2025 – 2026
> tham chiếu gói `AutoCAD.NET` 25.0.1 của Autodesk). Máy dev chưa có .NET 10 SDK nên chưa build được bản AutoCAD 2027.

## Bản này sửa gì

| # | Vấn đề | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| 1 | Đồng nghiệp phát triển / test thêm bị khoá tính năng Premium khi hết 30 ngày dùng thử | v9.5 – v9.7 bắt bản quyền (`LicenseManager.Enforced = true`) | **Tạm tắt** (`Enforced = false`, yêu cầu 03/11/2026): mọi máy dùng đủ Premium, không cần mã, không đếm ngày dùng thử. Mã kích hoạt vẫn nhập / lưu được: bản sau bật lại bản quyền thì máy đã lưu key dùng tiếp |
| 2 | Chưa cấp được key Premium cho chủ sản phẩm: workflow GitHub báo `Chưa có secret LHB_SIGNING_KEY`, máy dev không có file khoá ký kid 1 | Khoá kid 1 tạo trong phiên cloud 30/09, file khoá không nằm trên máy dev | Tạo **khoá ký kid 2** trên máy dev, thêm khoá công khai vào add-in. Cấp **key dùng chung, trọn đời**, serial `2166E9F8`, cho "Chủ sản phẩm LHB" (không gắn máy: máy dev không có AutoCAD, máy AutoCAD 2021 chưa gửi mã máy bản quyền v2) |
| 3 | Workflow cấp key trên GitHub sẽ công khai chuỗi key + sổ khách | Repo đang để public | Workflow tự dừng khi repo public; `.gitignore` chặn thêm file key đã cấp (`KEY_*.txt`), `*.pfx`, `*.p12` |
| 4 | `build.ps1` trên máy chỉ có .NET 8 SDK chỉ build bản AutoCAD 2021 – 2024 | Bản 2025 – 2026 / 2027 chỉ build khi có .NET 10 SDK | Có .NET 8 SDK → build thêm bản 2025 – 2026; bản 2027 vẫn cần .NET 10 SDK |

Key `2166E9F8` chỉ nhận từ v9.8: v9.5 – v9.7 báo `Mã ký bằng khoá số 2 mà bản add-in này chưa nhận - cập nhật add-in bản mới`.

## A. Máy AutoCAD 2021 (máy test chính)

| # | Thao tác | Kết quả đúng |
|---|---|---|
| A1 | Tắt hẳn AutoCAD, giải nén zip vào **thư mục mới** | Thư mục `LHBBlockScheduler` có `LHB.lsp`, `LHBBlockScheduler.dll`, thư mục `net8` (không có `net10`) |
| A2 | Mở AutoCAD 2021 + bản vẽ, kéo thả `LHB.lsp` | Dòng lệnh có `[LHB] Đã nạp bản cho AutoCAD 2021 - 2024: ...\LHBBlockScheduler\LHBBlockScheduler.dll`, `MD5: ... (đúng bản)` và `[LHB] v9.8 Premium - Premium miễn phí (chưa bật bản quyền, không cần mã)` (máy đã kích hoạt key từ trước: `[LHB] v9.8 Premium - Đã kích hoạt Premium (...)`) |
| A3 | `LHBVERSION` | Dòng Phiên bản: `v9.8 Premium, bản DLL cho AutoCAD 2021 - 2024 (.NET Framework 4.8)`; MD5 = dòng 2021 – 2024 ở bảng trên |
| A4 | `LHBBANQUYEN` | Dòng trạng thái màu xanh: `Premium miễn phí (chưa bật bản quyền, không cần mã)`; ô mã hiện chữ `(Bản hiện tại miễn phí: không cần mã kích hoạt, mọi tính năng Premium đã mở)` (máy đã lưu key: trạng thái `Đã kích hoạt Premium (...)`) |
| A5 | Chạy vài lệnh Premium: `LHBKHUVUC`, **Xuất Excel** trong form `LHBSCAN`, `LHBNHIEUBV` | Chạy luôn, không hỏi mã, không có dòng `[LHB Premium] Đang dùng thử...` |
| A6 | *(Nên làm — để sẵn key cho khi bật lại bản quyền)* `LHBBANQUYEN` → bấm vào ô mã, **Ctrl+A rồi Delete** xoá chữ mẫu → dán nguyên dòng `LHB2-...` trong `KEY_PREMIUM_LHB_2166E9F8.txt` → **Kích hoạt** | Hộp thoại `Đã kích hoạt Premium (dùng chung, trọn đời) - cấp cho Chủ sản phẩm LHB`; dòng chi tiết: `Serial 2166E9F8 · cấp ngày 03/11/2026 · key dùng chung - đừng gửi người ngoài nhóm`. Nếu báo `Bản hiện tại miễn phí, không cần mã kích hoạt...` là ô mã còn chữ mẫu ở đầu → key chưa lưu, làm lại bước này |
| A7 | Tắt hẳn AutoCAD, mở lại, kéo thả `LHB.lsp` | `[LHB] v9.8 Premium - Đã kích hoạt Premium (dùng chung, trọn đời) - cấp cho Chủ sản phẩm LHB` (key đã lưu, không phải dán lại) |
| A8 | `LHBDIAG` | Mục **3a. BẢN QUYỀN**: dòng `Bắt bản quyền (Enforced)` = `False`; `Trạng thái` = `Đã kích hoạt Premium (dùng chung, trọn đời) - cấp cho Chủ sản phẩm LHB` (bỏ qua A6: `Premium miễn phí (...)`); `Key đang lưu` = `serial 2166E9F8, kid 2, dùng chung, cấp cho 'Chủ sản phẩm LHB', cấp 03/11/2026, hạn trọn đời`. `log.txt` có `[License] Kiểm mã serial 2166E9F8, kid 2, dùng chung, cấp cho 'Chủ sản phẩm LHB', hạn trọn đời: HỢP LỆ` |

Chưa test v9.4 – v9.7: các bước trong `HUONG_DAN_TEST_20260930_v9.7_Premium.md`, `..._v9.6_Premium.md`,
`HUONG_DAN_TEST_20260929_v9.4_Premium.md` dùng luôn bản v9.8 này (bỏ qua các bước dùng thử / key review của v9.5: bản này không bắt bản quyền).

## B. Máy AutoCAD 2025 / 2026 (nếu đồng nghiệp có)

| # | Thao tác | Kết quả đúng |
|---|---|---|
| B1 | Chép cả thư mục `LHBBlockScheduler` sang máy đó, mở AutoCAD + bản vẽ, kéo thả `LHB.lsp`, bấm **Load** nếu hỏi bảo mật | `[LHB] Đã nạp bản cho AutoCAD 2025 - 2026: ...\net8\LHBBlockScheduler.dll`, `MD5: ... (đúng bản)` |
| B2 | `LHBVERSION` | Dòng Phiên bản: `v9.8 Premium, bản DLL cho AutoCAD 2025 - 2026 (.NET 8)`; MD5 khớp dòng 2025 – 2026 |
| B3 | `LHBBANQUYEN`, chạy 1 lệnh Premium; dán key như A6 | Như A4, A5 (Premium miễn phí). Kích hoạt key được, log `[License] Kiểm mã serial 2166E9F8 ... HỢP LỆ` (chữ ký ECDSA chạy trên .NET 8) |

> Bản 2025 – 2026 lần này build bằng .NET 8 SDK (v9.7 build bằng .NET 10 SDK): file `net8\LHBBlockScheduler.deps.json` có thêm
> dòng gói `AutoCAD.NET` (chỉ để biên dịch, không kèm DLL AutoCAD nào). Nạp lỗi ở B1: gửi ảnh dòng lệnh + `LHBDIAG`.

## C. AutoCAD 2027

Gói này chưa có bản 2027: kéo thả `LHB.lsp` báo `[LHB LỖI] Gói add-in này không có bản cho AutoCAD 2027` (đúng thiết kế).
Tạm dùng v9.7 (bản đó còn bắt bản quyền, không nhận key `2166E9F8`). Cần bản 2027 thì báo để build thêm (máy dev cần cài .NET 10 SDK).

## Cần gửi về

1. Ảnh dòng lệnh bước A2 + A7, ảnh hộp thoại bước A4 + A6.
2. File `LHBDIAG_*.txt` bước A8 (trong thư mục `LHBBlockScheduler`).
3. Lệnh nào lỗi / kích hoạt báo lỗi: ảnh + `LHBDIAG`.

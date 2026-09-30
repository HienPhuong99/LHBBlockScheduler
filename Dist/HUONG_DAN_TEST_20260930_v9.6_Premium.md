# Hướng dẫn test LHBBlockScheduler — bản 30/09/2026 (v9.6 Premium — chủng loại block động, tách theo kích thước)

**File cần nhận:** `LHBBlockScheduler_20260930_v9.6_Premium.zip` (trong zip có sẵn file hướng dẫn này; zip cũng nằm ở thư mục `Dist` trên GitHub)
**MD5 DLL đúng:** `5FEEE00BD988C1B1EDE8E98E84A63C89`
**Máy test:** AutoCAD 2021 (chạy được AutoCAD 2021 – 2024; 2025 trở lên chưa hỗ trợ)

v9.6 = v9.5 (bản quyền v2, key review vẫn dùng được) + sửa theo 3 ảnh ngày 30/09: đầu báo tia chiếu ra chủng loại
`Distance1=12320.3286822983`. Bản quyền không đổi: máy đã kích hoạt key review ở v9.5 vẫn đang kích hoạt.

> Bản này build trên máy build tự động, tham chiếu AutoCAD 2021 bằng gói chính thức `AutoCAD.NET 24.0` của Autodesk.
> Nếu NETLOAD / kéo thả `LHB.lsp` báo lỗi nạp: gửi ảnh dòng lệnh + file `LHBDIAG_*.txt`.

## Bản này sửa gì

| # | Vấn đề | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| 1 | Hộp thoại **Thông tin block mẫu**: ĐẦU BÁO TIA CHIẾU có chủng loại `Distance1=47116.93...` (ảnh 1) | Block động **không có Visibility** thì add-in ghép **mọi** tham số đang hiện thành chủng loại, kể cả tham số độ dài `Distance1` (kéo dài tia) | Chủng loại chỉ lấy từ **Visibility**, không có thì từ tham số dạng **chữ** (Lookup...). Tham số **số** — độ dài / rộng / cao (Linear, Polar, XY), góc xoay, toạ độ, lật — **không bao giờ** làm chủng loại → để trống |
| 2 | Form thống kê + bảng xuất: mỗi độ dài tia chiếu thành 1 dòng riêng, cột Chủng loại rộng bè vì chữ `Distance1=...` dài (ảnh 2, 3) | Như trên | Tia chiếu mọi độ dài chung **1 dòng**, chủng loại trống. Thêm ô **Tách theo kích thước** (hàng 2): bật → mỗi độ dài 1 dòng + cột **Kích thước** (vd `12320`; tủ 2 tham số: `1200 x 600`), xuất cả ra bảng CAD / Excel; tắt → không tách, ẩn cột |
| 3 | Block mẫu đã lưu từ bản cũ có chủng loại `Distance1=47116.93` chỉ khớp đúng tia dài 47116.93 → "Chỉ quét block mẫu" bỏ sót tia độ dài khác | Chủng loại cũ chứa số đo | Lúc quét: bỏ phần tham số số khi so → khớp mọi độ dài. Mở hộp thoại block mẫu (bản vẽ có block đó) → chủng loại **tự để trống** và tự lưu. **Thêm từ bản vẽ** không thêm dòng trùng |
| 4 | "Bảng co giãn, tự fix như Excel được không?" | — | Bảng AutoCAD Table **vốn co giãn**: kéo grip trên đầu cột, chữ tự xuống dòng, ký hiệu tự co theo ô. Thêm lệnh **`LHBKHOPCOT`**: khớp lại độ rộng cột theo chữ (như double-click mép cột Excel) |
| 5 | Quét thêm cộng nhầm vào dòng khác kích thước / khác thuộc tính tách dòng | So dòng chỉ theo tên + chủng loại | So theo đúng khoá gom dòng (tên + chủng loại + layer + kích thước + thuộc tính) |
| 6 | `LHBCAPNHAT` bảng xuất từ bản cũ có tia chiếu | Khoá dòng cũ chứa `Distance1=...` | Bảng cũ vẫn tính chủng loại kiểu cũ khi cập nhật → không ra dòng SL 0 + dòng mới cho cùng thiết bị |

Nút **Căn lề Trái / Giữa / Phải** chuyển lên **hàng 1** (cạnh nút Premium) để hàng 2 có chỗ cho ô mới.

## Các bước test

Dùng bản vẽ có ĐẦU BÁO TIA CHIẾU (nhiều cái, kéo dài khác nhau) như trong ảnh.

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Tắt hẳn AutoCAD, giải nén zip vào **thư mục mới**, mở AutoCAD + bản vẽ, kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `5FEEE00BD988C1B1EDE8E98E84A63C89`. Tiêu đề form lúc mở: `Thống kê Block v9.6 Premium` |
| 2 | `LHBSCAN`, quét vùng có nhiều đầu báo tia chiếu dài khác nhau (ô **Tách theo kích thước** đang tắt) | ĐẦU BÁO TIA CHIẾU chỉ **1 dòng**, cột **Chủng loại trống**, SL = tổng số tia. Block có Visibility (EXIT 2 HƯỚNG, BỘT ABC 8KG, TỦ 2 CUỘN VÒI) vẫn đúng chủng loại như cũ |
| 3 | Tích **Tách theo kích thước** (hàng 2, sau "Tách theo chủng loại") | Hiện cột **Kích thước** sau cột Chủng loại. Tia chiếu tách mỗi độ dài 1 dòng (vd `12320`, `15000`), xếp tăng dần; SL cộng lại = SL bước 2. Block không có tham số độ dài: ô Kích thước trống |
| 4 | Bỏ tích **Tách theo kích thước** | Về như bước 2, cột Kích thước ẩn. Tích lại, tắt / mở form (LHBSCAN lại) → ô tick và cột được nhớ |
| 5 | Đang tích: **Xuất bảng** (AutoCAD Table) | Bảng có cột **KÍCH THƯỚC**; cột CHỦNG LOẠI hẹp bình thường (không còn rộng bè như ảnh 3). Nút **Xuất Excel** (Premium): cột Kích thước là số |
| 6 | Nút **Block mẫu...** (bộ Data1 có ĐẦU BÁO TIA CHIẾU cũ) | Dòng ĐẦU BÁO TIA CHIẾU: cột **Chủng loại trống** (trước là `Distance1=47116.93...`). Đóng / mở lại hộp thoại vẫn trống (đã tự lưu) |
| 7 | Trong hộp thoại: **Thêm từ bản vẽ**, chọn 1 đầu báo tia chiếu bất kỳ | Dòng lệnh báo `đã có sẵn 1` — **không** thêm dòng trùng |
| 8 | Tích **Chỉ quét block mẫu**, `LHBSCAN` lại vùng có tia chiếu nhiều độ dài | Mọi tia chiếu đều được đếm (trước chỉ tia đúng độ dài 47116.93), tên = tên thống kê trong bộ mẫu, chấm xanh |
| 9 | Đang tích Tách theo kích thước: **Quét thêm** 1 vùng có tia cùng độ dài với 1 dòng đang có + 1 tia độ dài mới | Tia cùng độ dài cộng vào đúng dòng đó; tia độ dài mới thành dòng mới |
| 10 | Xuất 1 bảng mới. Double-click 1 ô Tên thiết bị trong bảng, sửa thành tên thật dài → chữ xuống 2 dòng. Gõ `LHBKHOPCOT`, chọn bảng, Enter | Cột đó **rộng ra vừa chữ trên 1 dòng**, dòng thấp lại. Dòng lệnh: `Bảng Handle ...: khớp N cột theo chữ, giữ 1 cột ký hiệu` |
| 11 | Chọn bảng, kéo **grip ▲ trên đầu cột** Tên thiết bị cho hẹp lại, rồi `LHBKHOPCOT` + Enter (không chọn = mọi bảng LHB) | Kéo hẹp: chữ tự xuống dòng, ký hiệu vẫn nằm gọn trong ô. Sau LHBKHOPCOT: cột về vừa chữ |
| 12 | *(Premium, nếu có bảng xuất bằng bản v9.4 / v9.5 có tia chiếu như ảnh 3)* Không sửa tia chiếu, chỉ copy thêm 1 block khác (vd nút nhấn) vào vùng đã quét của bảng đó, gõ `LHBCAPNHAT` + Enter | Dòng tia chiếu của bảng cũ **giữ nguyên SL** (không đỏ), không thêm dòng tia chiếu mới; dòng nút nhấn tăng 1 (đỏ). Log: `[TableUpdater] Bảng Handle ... xuất từ bản trước v9.6 (Version 2) -> chủng loại block động tính kiểu cũ` |
| 13 | *(Premium)* Bảng xuất ở bước 5 (có cột Kích thước): kéo 1 tia sang độ dài mới, `LHBCAPNHAT` | Dòng độ dài cũ giảm SL (đỏ), thêm dòng độ dài mới (đỏ) |
| 14 | `LHBDIAG` | File `LHBDIAG_*.txt`, mục 5 (log) có dòng `BlockExtractor: N block động có tham số số (độ dài / góc / toạ độ / lật) KHÔNG tính làm chủng loại: ĐẦU BÁO TIA CHIẾU [Distance1] kích thước '...'` và `[TemplateLibrary] Bộ 'Data1': block mẫu 'ĐẦU BÁO TIA CHIẾU' chủng loại cũ 'Distance1=...' -> ''` |

Chưa test v9.5 / v9.4: làm thêm các bước trong `HUONG_DAN_TEST_20260930_v9.5_Premium.md` (bản quyền, key review) và
`HUONG_DAN_TEST_20260929_v9.4_Premium.md` (ARRAY, MINSERT, XREF).

## Cần gửi về

1. File `LHBDIAG_*.txt` ngay sau bước 8 (có log quét + block mẫu).
2. Ảnh form ở bước 2 và bước 3 (thấy cột Chủng loại trống, cột Kích thước).
3. Ảnh bảng xuất ở bước 5 và hộp thoại block mẫu ở bước 6.
4. Nếu có block động khác bị **mất chủng loại** mà lẽ ra phải có (vd chủng loại là số: `DN50` đặt bằng tham số số thay vì Visibility): ảnh + tên block + `LHBDIAG`.
5. Lỗi / câu báo lạ ở `LHBKHOPCOT`: ảnh bảng trước và sau + `LHBDIAG`.

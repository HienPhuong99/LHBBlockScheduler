# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v6)

**File cần nhận:** `LHBBlockScheduler_20260929_v6.zip`
**MD5 DLL đúng:** `A98634FE77862EC32437EA78B05D1E3C`
**Máy test:** AutoCAD 2021

**Bản v6 = v5 + sửa lỗi LISP:** kéo thả `LHB.lsp` v5 báo `; error: bad function: LHB-DLLINFO` và không nạp gì (LISP gọi hàm kiểm tra phiên bản khi DLL chưa nạp). DLL **giữ nguyên** như v5 (cùng MD5), chỉ thay `LHB.lsp`.

Bản v6 gồm toàn bộ v4 cộng 4 sửa đổi dưới đây. Mục 4 (thư viện block mẫu) **chưa có** trong bản này: đang chờ duyệt demo.

## Bản này sửa gì

| # | Lỗi / yêu cầu | Đã sửa |
|---|---|---|
| A | Kéo thả `LHB.lsp` bản mới nhưng AutoCAD vẫn NETLOAD **bản cũ**, tắt mở lại CAD vẫn vậy | Nguyên nhân: LISP nhớ thư mục bản cũ trong registry và nạp lại thư mục đó. Nay `LHB.lsp` mang MD5 của đúng DLL đi kèm, **bỏ qua thư mục có bản khác**, tự tìm thư mục vừa giải nén trong Downloads / Desktop / Documents. Sau khi nạp, LISP hỏi DLL xem AutoCAD thật sự đang chạy bản nào, sai bản thì báo cách xử lý |
| 1 | Kéo rộng cột Ký hiệu trên form nhưng bảng xuất ra vẫn hẹp | Độ rộng cột bảng xuất theo độ rộng cột trên form (1 dòng form = 1 dòng bảng). Áp dụng cả cột chữ: kéo rộng thì bảng rộng theo, kéo hẹp quá thì vẫn đủ chỗ cho chữ |
| 2 | Ký hiệu ĐÈN EXIT trong bảng khác block: bị xoay ngang, mũi tên thành nét đứt | Ký hiệu nay chép đúng hình của block (như cột Ký hiệu trên form). Tỉ lệ nét của linetype được giữ nên mũi tên nét liền như trên bản vẽ |
| 3 | Block cùng tên che lấp nhau cũng phải báo trùng | Trùng = **cùng tên block** (khác chủng loại vẫn tính) và: cùng điểm chèn, **hoặc** khung bao che lấp nhau từ **10%** diện tích block nhỏ hơn trở lên. Mức % chỉnh được trong hộp thoại Tìm trùng |

## Chuẩn bị

1. **Tắt hẳn AutoCAD.**
2. **Xoá thư mục v5** vừa giải nén (LISP lỗi). **Giữ nguyên** thư mục add-in bản v4 cũ (để kiểm tra LISP mới bỏ qua bản cũ).
3. Giải nén `LHBBlockScheduler_20260929_v6.zip` vào **thư mục mới trong Downloads** (hoặc Desktop / Documents).
4. **Dùng bản copy của bản vẽ để test.**
5. Tạo sẵn chỗ che lấp để test mục 3 (giống ảnh 4 lần trước):
   - Chọn 1 ĐÈN EXIT 1 HƯỚNG → `COPY` sang bên cạnh → `SCALE` bản copy 1.2 → `MOVE` bản copy đè lệch lên block gốc (chừng một nửa).
   - Chọn 1 ĐÈN EXIT 2 HƯỚNG → `COPY` đè lệch nhẹ lên 1 ĐÈN EXIT 1 HƯỚNG khác.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD. Kéo thả `LHB.lsp` của bản **v6** | **Không** còn `bad function: LHB-DLLINFO`. Command line có dòng `[LHB] Chưa có add-in trong phiên này...`, `[LHB] Thư mục đã nhớ là BẢN KHÁC (bỏ qua): ...v4...`, rồi `[LHB] Tìm thấy: ...v6...`, `[LHB] MD5: A98634FE... (đúng bản)`. Nếu giải nén ngoài Downloads / Desktop / Documents: hiện hộp thoại chọn DLL, chọn `LHBBlockScheduler.dll` trong thư mục v6 |
| 2 | Gõ `LHBVERSION` | MD5 = `A98634FE77862EC32437EA78B05D1E3C`, đường dẫn DLL là thư mục **v6** |
| 3 | Tắt hẳn AutoCAD, mở lại, kéo thả `LHB.lsp` v6 lần nữa | `[LHB] Dùng thư mục đã nhớ: ...v6...`, MD5 đúng bản. **Không** nạp bản v4 |
| 4 | Nếu bước 1 hoặc 3 hiện khung `AutoCAD ĐANG CHẠY BẢN CŨ` | Chụp ảnh command line. Làm theo hướng dẫn trong khung (APPLOAD > Contents, xoá LHB.lsp cũ), tắt mở lại, làm lại bước 1 |
| 5 | Gõ `LHBSCAN`, chọn vùng có ĐÈN EM và các loại ĐÈN EXIT | Form mở như trước |
| 6 | Trên form, kéo rộng cột **Ký hiệu** gấp khoảng 3 lần. Chọn `AutoCAD Table`, bấm **Xuất bảng** | Cột Ký hiệu trong bảng rộng gấp khoảng 3 lần chiều cao dòng (tỉ lệ ngang / cao của ô giống trên form) |
| 7 | Zoom sát ô Ký hiệu các dòng ĐÈN EXIT trong bảng | Ký hiệu **đứng**, giống cột Ký hiệu trên form và giống block trên bản vẽ. Mũi tên **nét liền** như block. Lưu ý: ký hiệu theo hướng của định nghĩa block, không theo góc xoay của từng block trên bản vẽ |
| 8 | `LHBSCAN` lại, chọn vùng có chỗ che lấp đã tạo | Cột **Trùng** của ĐÈN EXIT 1 HƯỚNG có số đỏ. SL đã trừ block thừa |
| 9 | Bấm **Tìm trùng** | Hộp thoại "Block trùng / che lấp nhau" có cột **Kiểu trùng** = `Che lấp xx%` cho 2 chỗ vừa tạo. Chỗ ĐÈN EXIT 2 HƯỚNG đè lên 1 HƯỚNG: cột Chủng loại ghi cả 2 chủng loại. Vòng đỏ bao hết các block chồng nhau |
| 10 | Kiểm tra các ĐÈN EXIT đặt sát cạnh nhau nhưng không đè lên nhau | **Không** bị báo trùng |
| 11 | Đổi **Che lấp từ (%)** thành 90, bấm **Tìm lại** | Chỗ che lấp ít hơn 90% biến mất khỏi danh sách. Đổi lại 10, bấm Tìm lại |
| 12 | Double-click 1 dòng `Che lấp` | CAD zoom tới chỗ đó, highlight cả 2 block |
| 13 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Còn cần xác nhận từ v4 (nếu chưa test)

1. **Xoá bản thừa** trong hộp thoại Tìm trùng, đóng form, gõ `U`: block vừa xoá hiện lại, ghi lại cần gõ mấy lần.
2. Nút **Highlight** với dòng có Nguồn = Lồng: block lồng có sáng lên không.

## Lưu ý

1. **Xoá bản thừa** giữ lại block vẽ trước (cũ nhất). Chỗ `Che lấp` có thể là 2 block khác cỡ / chủng loại: Zoom tới xem trước khi xoá.
2. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt`.
2. Ảnh command line sau bước 1 và bước 3.
3. Ảnh bảng xuất sau bước 6 và ảnh zoom ô ký hiệu ĐÈN EXIT ở bước 7.
4. Ảnh hộp thoại Tìm trùng ở bước 9.

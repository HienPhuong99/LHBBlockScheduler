# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v8)

**File cần nhận:** `LHBBlockScheduler_20260929_v8.zip`
**MD5 DLL đúng:** `776431E25DD53DE19E889ACA8FE33529`
**Máy test:** AutoCAD 2021

Bản v8 gồm toàn bộ v7 (ký hiệu đều cỡ, Quét thêm, thư viện block mẫu) cộng 3 mục mới dưới đây.

## Bản này sửa gì

| # | Yêu cầu | Đã làm |
|---|---|---|
| 1 | Xoá block mẫu hàng loạt: giữ Shift chọn nhiều dòng, bấm Delete | Hộp thoại **Thông tin block mẫu** (`LHBMAU`) chọn được nhiều dòng: **Shift + click** chọn liền mạch, **Ctrl + click** chọn từng dòng, **Ctrl + A** chọn hết. Bấm phím **Delete** hoặc nút **Xoá dòng chọn** để xoá, có hỏi xác nhận. Thêm nút **Xoá bộ...** để xoá hẳn cả bộ mẫu (vd Data2) |
| 2 | Bảng danh sách lệnh, đổi được phím tắt | Lệnh mới **`LHBLENH`** (phím tắt mặc định **`LHB`**): bảng mọi lệnh của add-in, cột **Phím tắt** sửa được, nút **Chạy** để chạy lệnh. Bấm **Lưu phím tắt** là gõ được ngay, lần sau mở AutoCAD vẫn còn |
| 3 | Cột Block Name trên form thống kê chưa cho ẩn | Chuột phải tiêu đề cột: **Block Name** nay tích bỏ được (chỉ còn SL không cho ẩn). Ẩn thì bảng xuất cũng bỏ cột này, lần sau mở form vẫn nhớ |

Phím tắt mặc định: **`LHB`** = bảng lệnh, **`TKB`** = LHBSCAN (thống kê), **`BLM`** = LHBMAU (block mẫu).

Cách sửa ô trong bảng block mẫu thay đổi: nay **double-click** ô (hoặc chọn ô rồi gõ luôn / bấm F2) mới sửa. Lý do: trước đây click vào ô là vào sửa ngay, nên Shift + click và phím Delete không chọn / xoá dòng được. Cột Đơn vị vẫn click 1 lần là xổ danh sách.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.** Giữ nguyên thư mục v7 (có `ThuVienMau\Data1` 7 block), không cần xoá.
2. Giải nén `LHBBlockScheduler_20260929_v8.zip` vào **thư mục mới trong Downloads**.
3. Dùng bản copy của bản vẽ để test.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD, kéo thả `LHB.lsp` v8. Gõ `LHBVERSION` | Không lỗi. MD5 = `776431E25DD53DE19E889ACA8FE33529`. Lúc nạp, dòng lệnh có `[LHB] Phím tắt: LHB = LHBLENH, TKB = LHBSCAN, BLM = LHBMAU` |
| 2 | Gõ `BLM` | Mở hộp thoại Thông tin block mẫu, bộ Data1 đủ **7 block** của v7 (tự chép từ bản dự phòng sang thư mục v8) |
| 3 | Click dòng 2, giữ **Shift** click dòng 4 | 3 dòng 2, 3, 4 cùng sáng xanh |
| 4 | Bấm phím **Delete** | Hỏi "Xoá 3 block mẫu khỏi bộ 'Data1'?" kèm tên 3 block. Bấm Yes: còn 4 dòng, tiêu đề ghi "(chưa lưu)" |
| 5 | Bấm **Thoát** > **No** (không lưu). Gõ `BLM` lại | Vẫn đủ 7 block (xoá chưa lưu thì lấy lại được) |
| 6 | **Ctrl + click** dòng 1 và dòng 5, bấm nút **Xoá dòng chọn** > Yes | Xoá đúng 2 dòng đó |
| 7 | Double-click ô **Tên thống kê** 1 dòng, sửa chữ, Enter. Bấm phím Delete khi **đang sửa** ô | Delete chỉ xoá chữ trong ô, không xoá dòng |
| 8 | Bấm **Thoát** > **No**. Gõ `BLM`, bấm **Bộ mới...** đặt tên `Test`, rồi bấm **Xoá bộ...** > Yes | Bộ Test bị xoá, ô Bộ mẫu quay về Data1 đủ 7 block |
| 9 | Gõ `LHB` | Mở bảng **Danh sách lệnh LHB**: cột Lệnh, Phím tắt (nền vàng), Chức năng, Trạng thái (`LHB: Đang dùng`...), nút Chạy |
| 10 | Bấm **Chạy** ở dòng LHBSCAN | Bảng đóng, AutoCAD chạy LHBSCAN (hỏi chọn đối tượng). Esc để thoát |
| 11 | Gõ `LHB`, sửa phím tắt LHBSCAN thành `TK, TKB`, bấm **Lưu phím tắt** | Dòng lệnh báo phím tắt mới. Gõ `TK` chạy LHBSCAN |
| 12 | Sửa phím tắt LHBDUPCLEAR thành `LINE`, bấm **Lưu phím tắt** | **Không lưu**, báo `'LINE'` trùng lệnh AutoCAD |
| 12b | Sửa thành `L`, bấm **Lưu phím tắt** | Hỏi "trùng lệnh tắt trong acad.pgp: L (đang là lệnh tắt của LINE)... Vẫn lưu?". Bấm **No**: không lưu |
| 13 | Xoá chữ vừa gõ ở LHBDUPCLEAR, **Lưu phím tắt**. Tắt hẳn AutoCAD, mở lại, kéo thả `LHB.lsp`, gõ `TK` | Vẫn chạy LHBSCAN (phím tắt nhớ qua lần mở sau) |
| 13b | Gõ `TK`, quét chọn vài block. Trên form, chuột phải tiêu đề cột, bỏ tích **Block Name** | Cột Block Name ẩn. Tiêu đề form ghi "Thống kê Block v8" |
| 13c | **Xuất bảng** | Bảng xuất không có cột Block Name |
| 13d | Đóng form, gõ `TK` quét lại | Cột Block Name vẫn ẩn. Chuột phải tiêu đề > **Hiện lại tất cả cột** là hiện lại |
| 14 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` (có mục 3b PHÍM TẮT LỆNH) |

Nếu có thời gian, làm tiếp các bước 7 - 11 của hướng dẫn v7 (quét chỉ block mẫu, Quét thêm, xuất bảng ký hiệu đều cỡ). Bản v7 chưa có kết quả các bước đó.

## Lưu ý

1. Phím tắt **không sửa file acad.pgp** của máy. Add-in đăng ký phím tắt mỗi lần nạp; chưa kéo thả `LHB.lsp` thì phím tắt chưa có.
2. Phím tắt trùng lệnh có sẵn (LINE, COPY...) hoặc lệnh LISP khác bị từ chối, để không mất lệnh đó. Trùng lệnh tắt trong acad.pgp (L, C, CO...) thì hỏi lại, mặc định No.
3. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt`.
2. Ảnh hộp thoại block mẫu ở bước 3 (đang chọn 3 dòng).
3. Ảnh bảng Danh sách lệnh LHB ở bước 9 và thông báo ở bước 12, 12b.
4. Ảnh form sau bước 13b và bảng xuất ở bước 13c.

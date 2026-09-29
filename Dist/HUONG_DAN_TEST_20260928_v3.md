# Hướng dẫn test LHBBlockScheduler — bản 28/09/2026 (v3)

**File cần nhận:** `LHBBlockScheduler_20260928_v3.zip`
**MD5 DLL đúng:** `0917175217ADCC4AB54F0C222DC88E0C`
**Máy test:** AutoCAD 2021

## Bản này sửa gì (so với v2)

1. **Ký hiệu trong bảng xuất co giãn theo ô.** Kéo grip đổi chiều cao dòng hoặc bề rộng cột thì ký hiệu tự to/nhỏ theo, giống ảnh trên form. Ký hiệu chiếm khoảng 85% ô.
2. **Cột "Ảnh" đổi tên thành "Ký hiệu"** (trên form và trong bảng xuất). Nút "Ảnh ▾" ở thanh dưới giữ nguyên.
3. **Căn lề như Excel.** Thêm 3 nút **Trái / Giữa / Phải** ở hàng tuỳ chọn thứ 2 của form. Quét chọn các ô, bấm nút. Bảng xuất CAD căn đúng như trên form.
   - Form nay chọn theo **ô** (như Excel), không còn tô cả dòng khi click. Các nút Gộp nhóm, Xoá, Lên/Xuống, Highlight, Ảnh… vẫn tính theo **dòng có ô đang chọn**.
   - Mặc định mọi ô căn **giữa** (trước đây form căn trái nhưng bảng xuất căn giữa, nay form hiển thị đúng như bảng xuất).

## Chuẩn bị

1. **Tắt hẳn AutoCAD.**
2. Xoá hoặc đổi tên thư mục add-in của lần test trước.
3. Giải nén `LHBBlockScheduler_20260928_v3.zip` vào **thư mục mới**.
4. **Dùng bản copy của bản vẽ để test.**

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD, mở bản vẽ, **kéo thả** `LHB.lsp` vào AutoCAD. Gõ `LHBVERSION` | MD5 = `0917175217ADCC4AB54F0C222DC88E0C` |
| 2 | Gõ `LHBSCAN`, chọn vùng có ĐÈN EM và ĐÈN EXIT | Tiêu đề cột thứ 2 là **Ký hiệu**. Chữ trong các ô căn giữa |
| 3 | Kéo chuột quét chọn cả cột "Tên thiết bị" (từ ô đầu xuống ô cuối), bấm **Trái** | Cả cột chữ dồn sang trái |
| 4 | Quét chọn vài ô cột "SL", bấm **Phải**. Chọn 1 ô vừa căn phải, bấm **Giữa** | Ô đó về giữa, các ô khác vẫn phải |
| 5 | Click 1 ô của 1 dòng, bấm **Lên** rồi **Lên** lần nữa | Dòng đi lên 2 bậc |
| 6 | Quét chọn ô ở 2 dòng khác nhau, bấm **Gộp nhóm** | Gộp đúng 2 dòng đó |
| 7 | Kiểu bảng `AutoCAD Table`, bấm **Xuất bảng**, pick điểm | Tiêu đề cột **Ký hiệu**. Chữ căn đúng như form (bước 3–4). Ký hiệu nằm giữa ô, cỡ đều nhau, EXIT đúng màu xanh như bản v2 |
| 8 | Click vào bảng, kéo grip **đổi chiều cao 1 dòng** (to lên gấp đôi rồi nhỏ lại) | Ký hiệu trong dòng đó to/nhỏ theo, vẫn nằm trong ô |
| 9 | Kéo grip **đổi bề rộng cột Ký hiệu** | Ký hiệu co giãn theo, không tràn ra ngoài ô |
| 10 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Căn lề chỉ nhớ trong lần mở form đó. Quét lại / đổi tuỳ chọn quét thì mất (giống các chỉnh sửa khác trên form). Bấm **Quy hoạch** thì vẫn giữ.
2. Block `LHB_SYM_...` của đèn EXIT (và mọi block có chủng loại) nay chứa hình đã tách rời (explode) thay vì 1 block copy. Nhìn giống hệt, nhưng cần như vậy để AutoCAD co giãn đúng cỡ. Không xoá / purge các block này khi bảng còn dùng.
3. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt` (trong thư mục add-in).
2. Ảnh form sau bước 4.
3. Ảnh bảng đã xuất (bước 7) và sau khi kéo đổi kích thước (bước 8–9).

# Hướng dẫn test LHBBlockScheduler — bản 28/09/2026 (v2)

**File cần nhận:** `LHBBlockScheduler_20260928_v2.zip`
**MD5 DLL đúng:** `5921ABA3E5EF3A156A486CE5637B8BAA`
**Máy test:** AutoCAD 2021

## Bản này sửa gì (so với lần test trước)

1. **Kéo thả `LHB.lsp` báo "Không thể xác định vị trí file LHB.lsp!"** — đã sửa. Lần đầu, nếu add-in chưa biết thư mục, sẽ hiện hộp thoại chọn file `LHBBlockScheduler.dll` **một lần duy nhất**; các lần sau tự nhớ. Lệnh mới `LHBSETPATH` để chọn lại thư mục.
2. **Lưu thư viện từ Legend nhưng quét không nhận (chỉ ĐÈN EM có chấm xanh)** — nguyên nhân: block ĐÈN EXIT lỗi tính kích thước (`eInvalidExtents`), nên thư viện lưu hình rỗng (ShapeHash = 0) và không lưu tên block. Đã sửa: tính kích thước bền hơn, lưu thêm tên block gốc để nhận diện kể cả khi hình lệch.
3. **Ký hiệu trong bảng xuất to nhỏ không đều, lệch ô** — đã sửa: mọi ký hiệu được chuẩn hoá (tâm hình nằm giữa ô, chiếm tối đa 80% bề rộng ô và 70% chiều cao dòng).
4. **Sửa trực tiếp trên bảng của form LHBSCAN** (tính năng mới):
   - Double-click vào ô để sửa (Block Name, Tên thiết bị, Chủng loại, Loại Block, Đơn vị, SL, Ghi chú).
   - Double-click vào **tiêu đề cột** để đổi tên cột (tên mới dùng luôn khi xuất bảng CAD, được nhớ cho lần sau).
   - Phím **Delete**: xoá dòng đang chọn. Phím **Insert** hoặc nút **Thêm dòng**: thêm dòng nhập tay. **F2**: sửa ô đang chọn.
   - Double-click vào ô **Ảnh** hoặc **TT**: zoom tới block trên bản vẽ (như cũ).
5. Zoom/Highlight với ĐÈN EXIT trước bị lỗi — đã sửa cùng nguyên nhân ở mục 2.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.**
2. Xoá hoặc đổi tên thư mục add-in của lần test trước.
3. Giải nén `LHBBlockScheduler_20260928_v2.zip` vào **thư mục mới**.
4. **Dùng bản copy của bản vẽ để test.**

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD, mở bản vẽ có bảng Legend. **Kéo thả** `LHB.lsp` vào AutoCAD | Hoặc nạp luôn, hoặc hiện hộp thoại chọn file → chọn `LHBBlockScheduler.dll` trong thư mục vừa giải nén. Command line in đường dẫn DLL đã nạp — **kiểm tra đúng thư mục mới** |
| 2 | Gõ `LHBVERSION` | MD5 = `5921ABA3E5EF3A156A486CE5637B8BAA` |
| 3 | Tắt AutoCAD, mở lại, kéo thả `LHB.lsp` lần nữa | Nạp luôn, **không** hỏi chọn file |
| 4 | Gõ `LHBLEGEND`, chọn bảng Legend, **Lưu lại vào thư viện** (bắt buộc lưu lại — thư viện cũ thiếu dữ liệu ĐÈN EXIT) | Báo lưu thành công |
| 5 | Gõ `LHBSCAN`, chọn đúng thư viện vừa lưu, bấm **Quy hoạch** | **Tất cả** dòng ĐÈN EM và ĐÈN EXIT (1 hướng / 2 hướng / chỉ lối) có **chấm xanh** |
| 6 | Double-click ô ký hiệu (Ảnh) của 1 đèn EXIT | CAD zoom tới đúng các đèn EXIT đó |
| 7 | Double-click ô "Tên thiết bị" → sửa → Enter | Tên đổi |
| 8 | Double-click ô "SL", gõ chữ (vd `abc`) → Enter | Báo "Giá trị không hợp lệ", không bị treo |
| 9 | Double-click tiêu đề cột "Ảnh" → đổi thành "Ký hiệu" → OK | Tiêu đề đổi, thứ tự dòng **không** bị sắp xếp lại |
| 10 | Bấm **Thêm dòng** (hoặc phím Insert), gõ tên. Chọn 1 dòng → phím **Delete** | Có dòng mới / dòng bị xoá |
| 11 | Chọn Kiểu bảng `AutoCAD Table`, ô Tỉ lệ đúng, bấm **Xuất bảng**, pick điểm | Ký hiệu **nằm giữa ô, kích thước đều nhau**, không tràn ra ngoài ô. Tiêu đề cột "Ký hiệu" (tên đã đổi ở bước 9). Dòng thêm tay có ô ký hiệu trống |
| 12 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Ở bảng xuất, block trong ô ký hiệu nay có tên dạng `LHB_SYM_<tên block>_<mã>` cho **mọi** thiết bị (kể cả block không có visibility). Đây là block bọc để căn giữa và chuẩn cỡ; bên trong vẫn là block gốc. Không xoá / purge các block này khi bảng còn dùng.
2. Muốn to/nhỏ ký hiệu khác đi thì báo lại tỉ lệ mong muốn (hiện tại: tối đa 80% bề rộng ô, 70% chiều cao dòng).
3. Dòng thêm tay chỉ tồn tại trong form lần đó; bấm quét lại / đổi tuỳ chọn quét thì dòng thêm tay mất.
4. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt` (trong thư mục add-in).
2. Ảnh form LHBSCAN sau khi bấm Quy hoạch (bước 5).
3. Ảnh bảng đã xuất (bước 11).

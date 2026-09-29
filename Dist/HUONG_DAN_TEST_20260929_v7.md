# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v7)

**File cần nhận:** `LHBBlockScheduler_20260929_v7.zip`
**MD5 DLL đúng:** `37C206C675B525C0F1E5F42922C10D8A`
**Máy test:** AutoCAD 2021

Bản v7 gồm toàn bộ v6 (nạp đúng bản, cột ký hiệu rộng theo form, EXIT nét liền, trùng do che lấp) cộng 3 mục mới dưới đây.

## Bản này sửa gì

| # | Yêu cầu | Đã làm |
|---|---|---|
| 1 | Ký hiệu trong bảng cái to cái nhỏ (12 và 13 khác cỡ, 7 / 9 / 10 quá to) | Mọi ký hiệu nay có **cạnh dài bằng nhau** (bằng chiều cao dòng trừ lề). Ký hiệu dẹt (tủ chữa cháy, EXIT, điện trở cuối nguồn) không phình theo cột Ký hiệu rộng nữa. Áp dụng cả 2 kiểu bảng |
| 2 | Quét tiếp để thống kê thêm | Nút **Quét thêm** (hàng 3 trên form): chọn thêm vùng, SL cộng dồn vào bảng đang có, giữ tên / đơn vị / căn lề đã sửa. Vùng đã chọn trước **không đếm lại** |
| 3 | Thư viện block mẫu (như hộp thoại trong video) | Lệnh **`LHBMAU`** hoặc nút **Block mẫu...** trên form: hộp thoại **Thông tin block mẫu** (Xoá / ▲ / ▼, Ký hiệu, Tên block, Chủng loại, Tên thống kê, Kiểu block, Đơn vị, Ghi chú), chọn bộ mẫu Data1 / Data2..., **Thêm từ bản vẽ**, **Chèn vào bản vẽ**, **Lưu thông tin**. Ô **Chỉ quét block mẫu** trên form: lúc quét chỉ dính block mẫu |

Thư viện lưu **cạnh add-in**: `LHBBlockScheduler\ThuVienMau\<bộ>.json` + `<bộ>.dwg` (hình block). Mang cả thư mục add-in sang máy khác là có đủ block mẫu. Bản dự phòng ở `%APPDATA%\LHBBlockScheduler\ThuVienMau`: giải nén bản add-in mới sang thư mục khác trên cùng máy thì thư viện tự chép sang.

Thư viện cũ (nút Quy hoạch / Thêm vào TV / Chỉ đếm block có trong TV) vẫn giữ nguyên, chưa gộp.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.** Xoá thư mục v5 / v6 đã giải nén.
2. Giải nén `LHBBlockScheduler_20260929_v7.zip` vào **thư mục mới trong Downloads**.
3. **Dùng bản copy của bản vẽ để test** (bản vẽ có ĐÈN EXIT, ĐÈN EM, tủ chữa cháy, sprinkler... như ảnh bảng lần trước).

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD, kéo thả `LHB.lsp` v7. Gõ `LHBVERSION` | Không lỗi. MD5 = `37C206C675B525C0F1E5F42922C10D8A` |
| 2 | Gõ `LHBMAU` | Hộp thoại **Thông tin block mẫu**, bộ `Data1`, danh sách trống. Dòng dưới cùng ghi thư mục lưu `...\LHBBlockScheduler\ThuVienMau` |
| 3 | Bấm **Thêm từ bản vẽ**, quét chọn 1 ĐÈN EM, 1 ĐÈN EXIT mỗi chủng loại, 1 tủ chữa cháy, Enter | Hộp thoại hiện lại, mỗi block (mỗi chủng loại) 1 dòng, có ảnh ký hiệu. Tiêu đề xanh ghi "(chưa lưu)" |
| 4 | Sửa **Tên thống kê** vài dòng (vd "Đèn exit 1 hướng"), đổi **Đơn vị** thành Bộ, bấm **▲ ▼** đổi thứ tự, bấm **X** xoá 1 dòng | Bảng đổi theo |
| 5 | Bấm **Lưu thông tin** | Báo đã lưu. Mở thư mục add-in thấy `ThuVienMau\Data1.json` và `Data1.dwg` |
| 6 | Bấm **Chèn vào bản vẽ** với 1 dòng ĐÈN EXIT, pick điểm | Chèn đúng block, đúng chủng loại, cùng cỡ với block trên bản vẽ |
| 7 | Bấm **Thoát**. Gõ `LHBSCAN`, quét chọn **toàn bộ** bản vẽ (kể cả chữ, tường, block khác) | Command line báo `Chỉ quét block mẫu (bộ 'Data1', N block)`. Lúc quét, **chỉ block mẫu sáng lên**. Form chỉ có dòng block mẫu, tên / đơn vị / thứ tự đúng như bước 4, chấm xanh |
| 8 | Trên form bỏ tích **Chỉ quét block mẫu** | Form hiện lại mọi block trong vùng đã chọn, block mẫu lên đầu |
| 9 | Tích lại **Chỉ quét block mẫu**. Bấm **Quét thêm**, chọn 1 vùng mới có thêm block mẫu, và chọn chồng lại 1 phần vùng cũ | SL cộng thêm đúng số block của phần **mới**. Phần chọn lại không bị đếm 2 lần. Command line báo `Quét thêm: ... bỏ N đã chọn trước` |
| 10 | Sửa tay tên 1 dòng trên form, bấm **Quét thêm** lần nữa | Tên vừa sửa vẫn giữ |
| 11 | Kéo rộng cột Ký hiệu, **Xuất bảng** (AutoCAD Table) | Ký hiệu **đều cỡ**: cạnh dài mọi ký hiệu bằng nhau, tủ chữa cháy / EXIT không to hơn sprinkler, ĐÈN EM |
| 12 | Gõ `LHBMAU`, bấm **Bộ mới...**, đặt tên `Data2` | Có bộ Data2 trống. Chọn lại Data1 ở ô Bộ mẫu thấy đủ block cũ |
| 13 | (Nếu có máy thứ 2) Copy nguyên thư mục add-in sang máy khác, kéo thả `LHB.lsp`, gõ `LHBMAU` | Thấy đủ block mẫu kèm ảnh. **Chèn vào bản vẽ** trên bản vẽ trống vẫn chèn được |
| 14 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Block mẫu khớp theo **tên block + chủng loại**. Block copy rồi đổi tên thì không khớp, cần thêm vào thư viện.
2. Bộ mẫu **trống** thì ô "Chỉ quét block mẫu" không có tác dụng (quét tất cả như cũ).
3. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt`.
2. Ảnh hộp thoại Thông tin block mẫu sau bước 5.
3. Ảnh form sau bước 7 và bước 9.
4. Ảnh bảng xuất ở bước 11 (thấy cả cột Ký hiệu).

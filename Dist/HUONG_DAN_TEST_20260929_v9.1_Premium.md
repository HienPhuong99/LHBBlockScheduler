# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v9.1 Premium)

**File cần nhận:** `LHBBlockScheduler_20260929_v9.1_Premium.zip`
**MD5 DLL đúng:** `581F5A95D57EE036FED3F3DC01CF00DD`
**Máy test:** AutoCAD 2021

Bản v9.1 = v9 Premium tối ưu cho **mượt hơn** và **bỏ phần thừa**. Không thêm tính năng mới. Các bước test v9 (file
`HUONG_DAN_TEST_20260929_v9_Premium.md`) vẫn dùng được nếu chưa test v9.

## Bản này sửa gì

**Nhanh hơn**

1. **Xuất bảng AutoCAD Table**: trước đây mỗi lần ghi 1 ô, AutoCAD dựng lại cả bảng (bảng 100 dòng x 8 cột dựng lại hàng nghìn lần). Nay tắt dựng lại trong lúc điền ô, dựng 1 lần cuối. Áp dụng cả `LHBCAPNHAT`, bảng chiều dài ống, bảng nhiều bản vẽ. Nhiều dòng cùng 1 ký hiệu (tách theo layer / thuộc tính) chỉ tạo block ký hiệu 1 lần.
2. **Quét block**: định nghĩa block dùng chung (5000 đầu phun cùng 1 block) chỉ duyệt nội dung 1 lần thay vì 5000 lần; không mở nét / chữ không phải block; tham số dynamic block đọc 1 lần thay vì 2.
3. **Form thống kê**: ảnh ký hiệu đọc từ đĩa 1 lần rồi nhớ (trước đây mỗi lần sắp xếp / gộp / quét lại đọc lại toàn bộ ảnh). Lưới vẽ đệm đôi, cuộn không nháy. Đổi tuỳ chọn quét: lưới dựng lại 1 lần thay vì từng dòng.
4. **Ô tìm kiếm**: lọc sau khi ngừng gõ 0.25 giây, từ khoá bỏ dấu 1 lần.
5. **Ghi log**: giữ file log mở khi ghi liên tục (trước đây mỗi dòng mở / đóng 2 file). Log quá 5 MB tự đổi tên `log.old.txt`.
6. `LHBLEGEND`: đổi ánh xạ cột không render lại ký hiệu. `LHBMAU`: bấm ▲ ▼ / Xoá không giải mã lại ảnh mọi dòng.

**Lỗi sửa kèm**

7. **Ô tìm kiếm báo lỗi** khi dòng đang chọn không khớp từ khoá ("Row associated with the currency manager's position cannot be made invisible"). Đã sửa. Lọc cũng giữ nguyên sau khi quét lại / sắp xếp.
8. Dòng block nhanh `A$C...` tô vàng: sau khi sắp xếp, màu vàng dính sang dòng khác. Đã sửa.
9. `DIMSCALE` nhỏ hơn 1 hoặc lớn hơn 10000 làm form không mở được (ô Tỉ lệ ngoài giới hạn). Đã sửa.

**Bỏ phần thừa**

10. **Bỏ thư viện thiết bị cũ** (hàng nút Thư viện / Quy hoạch / Thêm vào TV / Thư viện ▾ / Chỉ đếm block có trong TV). Chỉ còn **thư viện block mẫu**. Form còn **2 hàng nút** (trước 3 hàng), lưới cao hơn.
    - Thư viện cũ có dữ liệu được **tự chuyển 1 lần** thành bộ mẫu `TV cu <tên>` (vd `TV cu default`). Thiết bị cũ chỉ có hình, không có tên block thì không chuyển được (block mẫu khớp theo tên).
    - `LHBLEGEND` nay lưu vào **bộ block mẫu** (mặc định bộ đang dùng), kèm hình block để chèn lại được.
    - Cột **TT**: chấm xanh = có trong bộ block mẫu đang chọn.
11. Bỏ lệnh kiểm tra cũ `LHBSCANTEST`, `LHBTHUMBTEST` và code không còn dùng.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.** Giữ thư mục v9 (thư viện block mẫu tự chép sang).
2. Giải nén zip vào **thư mục mới trong Downloads**.
3. Dùng **bản copy** của bản vẽ PCCC nhiều block (càng nhiều càng tốt, để thấy tốc độ), có bảng Legend nếu có.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `581F5A95D57EE036FED3F3DC01CF00DD`. Dòng lệnh có `[LHB] v9.1 Premium` |
| 2 | `LHBSCAN`, quét cả bản vẽ | Form tiêu đề **v9.1 Premium**, chỉ **2 hàng nút** phía trên (hàng 1: Tìm kiếm, Bộ block mẫu, Block mẫu..., Chỉ quét block mẫu, Quét thêm, Premium ▾; hàng 2: Độ sâu quét... Tìm trùng, Không đếm trùng, Căn lề). Số lượng giống v9 |
| 3 | Chọn 1 dòng giữa bảng, gõ vào ô tìm kiếm 1 chữ **không** có ở dòng đó (vd `den`) | Lưới chỉ còn dòng khớp, **không** báo lỗi. Xoá chữ → hiện lại đủ |
| 4 | Đang có chữ tìm kiếm, tích / bỏ **Tách theo layer** | Lưới quét lại, vẫn chỉ hiện dòng khớp từ khoá |
| 5 | Bấm tiêu đề cột **Block Name** vài lần, kéo thanh cuộn lên xuống nhanh | Sắp xếp ngay, ảnh ký hiệu không nháy, dòng `A$C` (nếu có) tô vàng đúng dòng |
| 6 | Đổi **Độ sâu quét** 2 → 3 → 2 | Mỗi lần quét lại nhanh hơn v9 (log ghi thời gian, xem bước 12) |
| 7 | **Xuất bảng** (AutoCAD Table) | Bảng ra **nhanh hơn rõ** so với v9, ký hiệu đều cỡ, nội dung như v9 |
| 8 | Sửa bản vẽ (copy thêm vài block), `LHBCAPNHAT`, Enter | Ô đổi tô đỏ như v9, nhanh hơn |
| 9 | Nếu trước đây đã dùng thư viện cũ (Quy hoạch): mở ô **Bộ block mẫu** | Có bộ `TV cu default` (hoặc `TV cu <tên thư viện cũ>`). Chọn bộ đó → dòng khớp chấm xanh, đặt tên như thư viện cũ |
| 10 | Gõ `LHBLEGEND`, chọn bảng Legend, đổi vài ô ánh xạ cột | Xem trước đổi ngay (không đợi render lại). Bấm **Lưu vào bộ mẫu** → báo số block mẫu thêm / cập nhật |
| 11 | `LHBSCAN` lại (bộ block mẫu = bộ vừa lưu ở bước 10) | Dòng thiết bị có trong Legend: chấm xanh, tên thiết bị theo Legend. `LHBMAU` thấy các block mẫu vừa thêm, **Chèn vào bản vẽ** được |
| 12 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt`. Mục 4 liệt kê các bộ block mẫu, mục 5 có dòng `BlockExtractor: quét ... ms`, `[TableExporterAcad] Điền bảng xong sau ... ms` |

## Lưu ý

1. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.
2. Thư viện cũ (thư mục `%APPDATA%\LHBBlockScheduler\Libraries`) **không bị xoá**, chỉ được chép sang bộ mẫu.
3. Bước 7 nếu thấy chậm như cũ: log có dòng `SuppressRegenerateTable(...) lỗi` → gửi LHBDIAG.

## Cần gửi về

1. File `LHBDIAG_*.txt` (bước 12) — có thời gian quét / xuất bảng để so với v9.
2. Ảnh form 2 hàng nút (bước 2) và form đang lọc tìm kiếm (bước 3).
3. Ảnh bảng xuất (bước 7). Cảm nhận nhanh / chậm so với v9.
4. Ảnh ô Bộ block mẫu có bộ `TV cu ...` (bước 9, nếu có thư viện cũ) và form sau LHBLEGEND (bước 11).

# Hướng dẫn test LHBBlockScheduler — bản 28/09/2026

**File cần nhận:** `LHBBlockScheduler_20260928.zip` (83 KB)
**MD5 DLL đúng:** `F6B67A9A713816CEAFCEBD1BB640D62E`
**Máy test:** AutoCAD 2021

## Bản này sửa gì

1. **Lưu thư viện từ bảng Legend (`LHBLEGEND`)** — trước đây bấm Lưu báo lỗi `System.Text.Json`, thư viện luôn trống. Đã sửa.
2. **Tiêu đề cột Legend** — trước hiện `Minh h\U+1ECDa`, nay hiện đúng "Minh họa". Cột có block tự nhận là cột Ký hiệu.
3. **Xuất bảng bằng AutoCAD Table** (mặc định) — cột ký hiệu chứa block trong ô (AutoFit, Middle Center), giống bảng mẫu.
   - Thiết bị có visibility (vd đèn EXIT 1 hướng / 2 hướng): tự tạo block nhanh `LHB_SYM_...` copy từ thiết bị trên bản vẽ (tương đương Ctrl+C → Ctrl+Shift+V), rồi chèn vào ô.
   - Thiết bị không có visibility: chèn block bình thường.
4. **Bảng Line + Text kiểu cũ** — sửa lỗi chữ lệch lên 1 dòng và thừa 1 dòng trống ở đáy bảng; cột tự rộng đủ chứa chữ.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.**
2. Xoá hoặc đổi tên thư mục add-in cũ (vd `LHBBlockScheduler (3)`).
3. Nếu `LHB.lsp` cũ nằm trong Startup Suite (lệnh `APPLOAD` → nút Contents), gỡ ra.
4. Giải nén `LHBBlockScheduler_20260928.zip` vào **thư mục mới**.
5. **Dùng bản copy của bản vẽ để test.** Xuất bảng sẽ thêm vào bản vẽ: 1 Table, các block `LHB_SYM_*`, text style `LHB_TABLE`.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Mở AutoCAD, mở bản vẽ (bản copy) có bảng Legend | — |
| 2 | Kéo thả `LHB.lsp` từ thư mục vừa giải nén vào AutoCAD | Command line báo đã nạp |
| 3 | Gõ `LHBVERSION` | MD5 = `F6B67A9A713816CEAFCEBD1BB640D62E`. Khác số này là đang nạp nhầm bản cũ → quay lại bước Chuẩn bị |
| 4 | Gõ `LHBLEGEND`, chọn bảng Legend | Tiêu đề cột hiện đúng tiếng Việt ("Minh họa", "Số lượng"). Cột Minh họa tự chọn "Ký hiệu (Block)", cột xem trước có hình |
| 5 | Bấm **Lưu vào Thư viện** | Báo "Đã lưu thành công N thiết bị", dialog tự đóng |
| 6 | Gõ `LHBSCAN`, quét bản vẽ, bấm **Quy hoạch** | Ghi lại dòng nào chấm xanh, dòng nào chấm đỏ |
| 7 | Kiểm tra ô **Tỉ lệ** ở đáy form (xem Lưu ý 1), chọn Kiểu bảng `AutoCAD Table`, bấm **Xuất bảng**, pick điểm | Bảng xuất đúng tại điểm pick. Bấm vào ô thấy dải cột A/B/C (Table thật). Cột ký hiệu có hình, căn giữa ô |
| 8 | Double-click ô ký hiệu của 1 đèn EXIT | Hộp thoại "Edit Block in a Table Cell": Name = `LHB_SYM_...`, AutoFit được tích |
| 9 | Kiểm tra các dòng EXIT khác chủng loại (1 hướng / 2 hướng / chỉ lối) | Mỗi dòng có hình đúng chủng loại của nó |
| 10 | (Tuỳ chọn) Mở lại `LHBSCAN`, chọn `Line + Text (cũ)`, xuất bảng | Chữ nằm đúng dòng, không dòng trống ở đáy, chữ không tràn sang cột bên |
| 11 | Gõ `LHBDIAG` | Command line báo đường dẫn file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý khi test

1. **Tỉ lệ bảng:** form tự lấy `DIMSCALE` của bản vẽ. Bản vẽ đang test có `DIMSCALE = 200` → chữ cao 500, gấp đôi bảng chuẩn 1:100 (chữ 250). Bản vẽ in 1:100 thì sửa ô Tỉ lệ thành **100** trước khi xuất.
2. **Ký hiệu trong bảng luôn thẳng đứng** (xoay 0°), kể cả khi thiết bị trên bản vẽ xoay 180°. Đây là chủ ý, không phải lỗi.
3. **Block không có visibility** hiện theo hình gốc của block. Nếu trên bản vẽ block bị kéo giãn (stretch) mà trong bảng hiện hình khác → chụp ảnh gửi về.
4. **Xuất lần 2** ghi đè block `LHB_SYM_*` cũ → bảng xuất lần trước cũng đổi hình theo. Đây là chủ ý.
5. **Chấm đỏ sau "Quy hoạch" là điểm cần chú ý nhất.** Đèn EXIT và các block có visibility có khả năng ra chấm đỏ dù đúng thiết bị. Gặp trường hợp này: chụp màn hình form, chạy `LHBDIAG`, gửi về.
6. Bước nào lỗi (có hộp thoại báo lỗi, CAD treo, bảng sai...): **vẫn chạy `LHBDIAG`** và gửi file về. Không cần mô tả lỗi bằng lời — log đã ghi đủ.

## Cần gửi về

1. File `LHBDIAG_*.txt` (nằm trong thư mục add-in vừa giải nén).
2. Ảnh chụp bảng đã xuất (bước 7).
3. Ảnh hộp thoại "Edit Block in a Table Cell" của 1 ô đèn EXIT (bước 8).
4. Ảnh form sau khi bấm Quy hoạch nếu có dòng chấm đỏ (bước 6).

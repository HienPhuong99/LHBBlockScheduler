# Hướng dẫn test LHBBlockScheduler — bản 28/09/2026 (v4)

**File cần nhận:** `LHBBlockScheduler_20260928_v4.zip`
**MD5 DLL đúng:** `5484C5A279400B0FBC78A885A2F981C6`
**Máy test:** AutoCAD 2021

Bản v4 gồm toàn bộ thay đổi của v3 (ký hiệu co giãn theo ô, cột "Ký hiệu", căn lề Trái/Giữa/Phải) cộng tính năng mới dưới đây. Nếu chưa test v3, làm luôn các bước của `HUONG_DAN_TEST_20260928_v3.md` với bản v4 này.

## Tính năng mới: block trùng vị trí

Trùng = cùng tên block + cùng chủng loại + điểm chèn cách nhau không quá **sai số** (mặc định 1 đơn vị bản vẽ). Góc xoay, tỉ lệ không xét.

- Cột **Trùng** trên form: số block thừa của mỗi thiết bị (đỏ). Trống = không trùng.
- Ô **Không đếm trùng** (bật sẵn): SL đã trừ block thừa. Bỏ tích thì SL đếm cả block trùng như trước.
- Nút **Tìm trùng**: khoanh đỏ mọi chỗ trùng trên bản vẽ và mở hộp thoại "Block trùng vị trí":
  - Double-click 1 dòng (hoặc nút **Zoom tới**): zoom tới chỗ trùng, highlight các block chồng nhau.
  - **Sai số vị trí** + **Tìm lại**: đổi sai số rồi tìm lại.
  - **Xoá bản thừa**: mỗi vị trí giữ 1 block, xoá phần thừa (có hỏi xác nhận). Block nằm trong block cha không xoá, ghi chú "xoá tay".
  - **Tắt khoanh đỏ**: xoá vòng đỏ.
- **Xuất bảng**: từ mép phải mỗi dòng thiết bị có trùng, kéo đường đỏ tới từng chỗ trùng của thiết bị đó.
- Vòng đỏ và đường dẫn nằm trên layer `LHB_BLOCK_TRUNG` (đỏ, **không in**). Còn lại sau khi lưu file. Xoá bằng lệnh `LHBDUPCLEAR`. Xuất bảng lại thì tự xoá dấu cũ và vẽ lại.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.**
2. Xoá hoặc đổi tên thư mục add-in của lần test trước.
3. Giải nén `LHBBlockScheduler_20260928_v4.zip` vào **thư mục mới**.
4. **Dùng bản copy của bản vẽ để test.**
5. Tạo sẵn lỗi trùng để test: chọn 2 block ĐÈN EM, `COPY` với base point và điểm đến **cùng 1 điểm** (copy đè tại chỗ). Làm tương tự với 1 ĐÈN EXIT.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Kéo thả `LHB.lsp` vào AutoCAD. Gõ `LHBVERSION` | MD5 = `5484C5A279400B0FBC78A885A2F981C6` |
| 2 | Gõ `LHBSCANTEST`, chọn vùng có các block vừa copy đè | Dòng ĐÈN EM có thêm "(trùng vị trí: thừa 2 tại 2 chỗ)". SL in ra là số **gốc** (chưa trừ) |
| 3 | Gõ `LHBSCAN`, chọn cùng vùng | Cột **Trùng** của ĐÈN EM = 2, ĐÈN EXIT = 1 (chữ đỏ). SL = số ở bước 2 trừ đi số trùng |
| 4 | Bỏ tích **Không đếm trùng**, rồi tích lại | SL tăng thêm đúng số trùng, rồi giảm lại. Bật/tắt nhiều lần SL không bị trừ dồn |
| 5 | Bấm **Tìm trùng** | Hộp thoại liệt kê 3 chỗ trùng, số bản = 2. Trên bản vẽ có vòng đỏ + chữ "x2" ở 3 chỗ đó |
| 6 | Double-click 1 dòng trong hộp thoại | CAD zoom tới chỗ đó, block trùng được highlight |
| 7 | Bấm **Tắt khoanh đỏ** | Vòng đỏ biến mất |
| 8 | Đóng hộp thoại. Chọn Kiểu bảng `AutoCAD Table`, bấm **Xuất bảng**, pick điểm | Bảng có SL đã trừ trùng, **không** có cột Trùng. Từ mép phải dòng ĐÈN EM và dòng ĐÈN EXIT có đường đỏ chạy tới từng chỗ trùng, mỗi chỗ có vòng đỏ + "x2" |
| 9 | Gõ `PLOT` → Preview | Không thấy đường đỏ / vòng đỏ trong bản in |
| 10 | Gõ `LHBDUPCLEAR` | Command line báo số nét đã xoá, đường đỏ biến mất |
| 11 | `LHBSCAN` lại, **Tìm trùng** → **Xoá bản thừa** → Yes | Báo đã xoá 3 block. Danh sách trống "Không có block trùng", cột Trùng trống, SL không đổi (vì đã trừ sẵn) |
| 12 | Đóng form, gõ `U` (hoặc Ctrl+Z) vài lần | 3 block vừa xoá hiện lại. **Ghi lại** cần gõ U mấy lần |
| 13 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Nếu copy đè có lệch nhẹ (vd do snap sai), tăng **Sai số vị trí** trong hộp thoại rồi bấm **Tìm lại**. Sai số được nhớ cho lần sau.
2. Block nằm trong block cha (Nguồn = Lồng) cũng được tìm trùng, nhưng **Xoá bản thừa** không xoá chúng.
3. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.

## Cần gửi về

1. File `LHBDIAG_*.txt`.
2. Ảnh form sau bước 3 và hộp thoại sau bước 5.
3. Ảnh bản vẽ sau bước 8 (thấy cả bảng và đường đỏ).
4. Kết quả bước 12 (Ctrl+Z có hoàn tác được không, gõ mấy lần).

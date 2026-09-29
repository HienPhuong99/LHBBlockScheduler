# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v9.2 Premium)

**File cần nhận:** `LHBBlockScheduler_20260929_v9.2_Premium.zip`
**MD5 DLL đúng:** `A841B41AFBE6346399F2478603D31F4C`
**Máy test:** AutoCAD 2021

v9.2 = v9.1 (tối ưu tốc độ, bỏ thư viện cũ — xem `HUONG_DAN_TEST_20260929_v9.1_Premium.md`) + sửa ô Ký hiệu to nhỏ không đều.

## Bản này sửa gì

**Ô Ký hiệu to nhỏ không đều** (ảnh test v8: dòng 3 ĐÈN EXIT "CHỈ LỐI THOÁT NẠN" to hơn hẳn dòng 1, 2, 4).

- Nguyên nhân: ô ký hiệu dùng AutoFit (ký hiệu co giãn theo ô). AutoFit co khung bao của block vào **cả ô**. Từ v7 add-in đặt lề ô để phần trong lề là hình vuông, nhưng AutoFit không theo lề đó. Cột Ký hiệu rộng (~2.3 lần chiều cao dòng) nên ký hiệu dẹt (EXIT chỉ lối, dài gấp ~2.4 lần cao) phóng theo bề ngang, gần kín ô. Ký hiệu vuông hơn (ĐÈN EM, EXIT có mũi tên) bị giới hạn theo chiều cao dòng nên nhỏ hơn.
- Sửa: mỗi block ký hiệu `LHB_SYM_...` có thêm **khung bao vuông 1 x 1** (2 điểm ở 2 góc, nằm trên layer `LHB_KY_HIEU_KHUNG` **tắt + không in**, không thấy trên màn hình và bản in). AutoFit co khung vuông theo chiều cao dòng → **mọi ký hiệu có cạnh dài bằng nhau** (~85% chiều cao dòng), cột Ký hiệu rộng hay hẹp đều vậy. Vẫn co giãn khi kéo đổi chiều cao dòng.
- Áp dụng cả ô ký hiệu khi `LHBCAPNHAT` thêm dòng và ảnh tuỳ chỉnh (Ảnh ▾ > Chọn file ảnh).
- Bảng đã xuất bằng bản cũ: xuất lại bảng mới là ký hiệu của bảng cũ (cùng block) cũng đều lại.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.** Giữ thư mục bản cũ (thư viện block mẫu tự chép sang).
2. Giải nén zip vào **thư mục mới trong Downloads**.
3. Dùng **bản copy** của đúng bản vẽ trong ảnh test (có ĐÈN EM, ĐÈN EXIT 1 hướng / 2 hướng / chỉ lối).

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `A841B41AFBE6346399F2478603D31F4C`. Dòng lệnh có `[LHB] v9.2 Premium` |
| 2 | `LHBSCAN`, quét vùng có ĐÈN EM + 3 loại ĐÈN EXIT. Kéo cột **Ký hiệu** trên form rộng ra (~gấp đôi) | Form tiêu đề **v9.2 Premium** |
| 3 | **Xuất bảng** (AutoCAD Table) | 4 ký hiệu **cao bằng nhau, cạnh dài bằng nhau**; EXIT chỉ lối (dòng 3) không còn to hơn. Không thấy chấm / khung lạ quanh ký hiệu |
| 4 | Kéo tăng chiều cao 1 dòng của bảng (chọn ô, kéo grip) | Ký hiệu dòng đó to lên theo, vẫn nằm giữa ô |
| 5 | Xuất thêm 1 bảng với cột Ký hiệu hẹp (kéo hẹp trên form) | Ký hiệu vẫn đều nhau |
| 6 | `PLOT` > Preview bảng | Không có chấm / khung thừa trong bản in |
| 7 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.
2. Nếu bước 3 vẫn lệch: log có dòng `[TableExporterAcad.SquareFrame] ... khung bao block = ...` cho biết khung vuông có tác dụng không → gửi LHBDIAG.
3. Nếu thấy 2 chấm xám nhỏ ở góc ký hiệu: bản CAD bỏ qua đối tượng trên layer tắt khi đo khung, add-in tự chuyển sang layer `LHB_KY_HIEU_KHUNG_HIEN` (bật, không in) → vẫn không in ra. Gửi LHBDIAG để tôi xem.

## Cần gửi về

1. Ảnh bảng xuất (bước 3), đặt cạnh ảnh bảng v8 cũ.
2. File `LHBDIAG_*.txt` (bước 7).

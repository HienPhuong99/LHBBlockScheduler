# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v9 Premium)

**File cần nhận:** `LHBBlockScheduler_20260929_v9_Premium.zip`
**MD5 DLL đúng:** `ACB0AF84E5D92D05678F24073D95D6BD`
**Máy test:** AutoCAD 2021

Bản v9 Premium gồm toàn bộ v8 cộng 13 tính năng Premium. Chi tiết từng tính năng: file `TINH_NANG_PREMIUM.md` trên GitHub. Premium dùng thử 30 ngày, không cần mã khi test.

## Chuẩn bị

1. **Tắt hẳn AutoCAD.** Giữ thư mục v8 (thư viện block mẫu tự chép sang).
2. Giải nén zip vào **thư mục mới trong Downloads**.
3. Dùng **bản copy** của bản vẽ PCCC để test (có đèn EXIT, đèn EM, đầu báo / đầu phun, tủ chữa cháy, đường ống).
4. Vẽ sẵn 2 polyline kín bao 2 khu trên bản vẽ (vd Tầng 1, Tầng 2), trong mỗi khu có 1 chữ tên khu.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `ACB0AF84E5D92D05678F24073D95D6BD`. Dòng lệnh có `[LHB] v9 Premium - Dùng thử Premium: còn 30 ngày` |
| 2 | Nhìn thanh Ribbon | Có tab **LHB Premium**: 4 nhóm (Thống kê, Khối lượng, Kiểm tra PCCC, Tiện ích), nút có icon màu. Bấm **Thống kê block** chạy LHBSCAN |
| 3 | Gõ `LHBPALETTE` | Bảng công cụ dock được sang cạnh trái / phải, có trạng thái dùng thử + các nút |
| 4 | Gõ `LHBKHUVUC` > **Chọn đường bao...** > chọn 2 polyline kín | 2 dòng, tên tự lấy từ chữ trong khu, có diện tích m². Bấm **Lưu & áp dụng** |
| 5 | `LHBSCAN`, quét cả 2 khu | Form tiêu đề **v9 Premium**. Trước cột SL có cột **Tầng 1**, **Tầng 2** (và "Ngoài khu vực" nếu có block ngoài). Tổng các cột khu = SL |
| 6 | Premium ▾ > **Mẫu bảng xuất...**: dòng phụ "Công trình test", tích **Thêm dòng tổng**, **Tiêu đề cột viết HOA**, màu header 4 > **Lưu & dùng mẫu này** | Báo đã lưu |
| 7 | **Xuất bảng** (AutoCAD Table) | Bảng có tiêu đề, dòng phụ, header chữ hoa nền xanh, cột khu vực, dòng TỔNG CỘNG cuối |
| 8 | Copy thêm 3 block đã có vào trong vùng đã quét, xoá 1 block. Gõ `LHBCAPNHAT`, Enter | Dòng lệnh báo số ô thay đổi. Ô SL / khu vực đổi thành **chữ đỏ**, dòng tổng đúng |
| 9 | Chèn 1 loại block chưa có trong bảng vào vùng đã quét, `LHBCAPNHAT`, Enter | Thêm 1 dòng mới cuối bảng (chữ đỏ, có ký hiệu) |
| 10 | Trên form bấm **Xuất Excel**, lưu file | Excel mở ra: tiêu đề, header xanh, cột Ký hiệu có ảnh, cột khu vực, dòng tổng |
| 11 | Premium ▾ > **Cột thuộc tính...** (bản vẽ có block có attribute) tích **Hiện cột** 1 thuộc tính > Áp dụng | Thêm cột thuộc tính sau cột Chủng loại. Tích **Tách dòng** thì mỗi giá trị 1 dòng |
| 12 | Premium ▾ > **Soát lỗi đếm...** | Danh sách lỗi. Explode thử 1 block trước khi soát → có dòng "Block bị explode". Double-click dòng → zoom tới chỗ lỗi |
| 13 | Chọn 1 dòng đầu báo / sprinkler, Premium ▾ > **Đánh số thiết bị...** > **Đánh số** | Chữ số (vd DBK-01, DBK-02...) cạnh từng block, thứ tự trái → phải, trên → dưới, layer `LHB_DANHSO`. **Xoá số đã đánh** xoá hết |
| 14 | Premium ▾ > **Vùng bảo vệ PCCC...**: nhập bán kính (m) cho 1 loại, tích Tô mờ > **Vẽ vùng bảo vệ** | Vòng tròn đúng bán kính quanh từng thiết bị, đoạn đỏ ở chỗ 2 thiết bị cách > 2R. **Xoá vùng bảo vệ** xoá hết |
| 15 | Chọn 1 dòng, Premium ▾ > **Thay block...** chọn block đích > **Thay block** | Block đổi đúng chỗ, đúng góc. Form tự quét lại. Ctrl+Z trong AutoCAD hoàn tác được |
| 16 | Gõ `LHBCHIEUDAI` > **Toàn bản vẽ** | Mỗi layer 1 dòng, chiều dài (m). Kiểm tra 1 layer ống bằng lệnh LIST / MEASUREGEOM. Tích **Tách theo khu vực** → thêm cột theo khu. **Xuất bảng CAD** / **Xuất Excel** |
| 17 | Gõ `LHBNHIEUBV` > **Thêm file DWG...** chọn 2 bản vẽ khác > **Thống kê** | Mỗi bản vẽ 1 cột + cột Tổng. **Xuất Excel** có sheet danh sách bản vẽ |
| 18 | Gõ `LHBBANQUYEN` | Hiện mã máy, trạng thái dùng thử. Bấm **Chép mã máy** → gửi mã máy về để tôi tạo thử 1 mã kích hoạt |
| 19 | Gõ `LHBRIBBON` 2 lần | Lần 1 tắt tab Ribbon, lần 2 bật lại |
| 20 | Gõ `LHBDIAG` | Tạo file `LHBDIAG_<ngày giờ>.txt` |

## Lưu ý

1. Bước nào lỗi: **vẫn chạy `LHBDIAG`** và gửi file về, không cần mô tả bằng lời.
2. Ribbon không hiện: gõ `LHBRIBBON` 2 lần. Vẫn không có thì gửi LHBDIAG (log có dòng `[Ribbon]`).
3. Bảng xuất bằng bản v8 trở về trước **không** cập nhật được bằng `LHBCAPNHAT` (thiếu thông tin vùng quét). Xuất lại bằng v9.

## Cần gửi về

1. File `LHBDIAG_*.txt`.
2. Ảnh tab Ribbon (bước 2) và palette (bước 3).
3. Ảnh form có cột khu vực (bước 5), bảng xuất (bước 7), bảng sau cập nhật (bước 8, 9).
4. File Excel ở bước 10.
5. Ảnh danh sách soát lỗi (bước 12), đánh số (bước 13), vùng bảo vệ (bước 14).
6. Mã máy ở bước 18.

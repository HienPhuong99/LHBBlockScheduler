# LHB Block Scheduler — Tính năng Premium (từ v9)

Bản v9 Premium thêm 13 tính năng, tham khảo lệnh COUNT của AutoCAD 2022+ (bảng tự cập nhật, báo lỗi đếm), các add-in đếm block trên Autodesk App Store (nhiều bản vẽ, thuộc tính, xuất Excel) và lisp thống kê PCCC của Việt Nam (chiều dài ống, đánh số).

Mọi tính năng cũ (quét, block mẫu, tìm trùng, xuất bảng, phím tắt...) vẫn miễn phí. Tính năng Premium dùng thử **30 ngày** từ lần nạp add-in đầu tiên, sau đó cần mã kích hoạt (mục 13).

## Cách mở

| Chỗ mở | Cách dùng |
|---|---|
| Tab Ribbon **LHB Premium** | Tự hiện sau khi kéo thả `LHB.lsp`. Gõ `LHBRIBBON` để tắt / bật |
| Bảng công cụ dock | Gõ `LHBPALETTE`. Kéo dock sang trái / phải màn hình |
| Form thống kê | Nút vàng **Premium ▾** (hàng 3). Nút **Xuất Excel** cạnh **Xuất bảng** |
| Lệnh gõ tay | Xem cột Lệnh trong bảng dưới, hoặc `LHBLENH` |

## 13 tính năng

| # | Tính năng | Lệnh | Làm gì |
|---|---|---|---|
| 1 | Thống kê theo tầng / khu vực | `LHBKHUVUC` | Chọn các đường bao kín (polyline / circle) quanh từng tầng / khu. Tên lấy tự động từ chữ to nhất trong đường bao. Bảng thống kê có thêm 1 cột SL cho mỗi khu + cột "Ngoài khu vực". Lưu trong bản vẽ |
| 2 | Bảng tự cập nhật | `LHBCAPNHAT` | Bảng AutoCAD Table xuất từ v9 nhớ vùng quét. Sửa bản vẽ xong gõ `LHBCAPNHAT`, Enter: SL / khu vực / thuộc tính tính lại, ô thay đổi tô **đỏ**, loại block mới thêm dòng cuối. Tên, đơn vị sửa tay trong bảng giữ nguyên |
| 3 | Nhiều bản vẽ | `LHBNHIEUBV` | Chọn nhiều file DWG (không cần mở). Mỗi bản vẽ 1 cột SL + cột Tổng. Xuất bảng CAD (có ký hiệu) và Excel |
| 4 | Chiều dài ống / dây | `LHBCHIEUDAI` | Line, polyline, arc, spline, mline theo layer. Đổi ra mét, % hao hụt, tách theo khu vực, đặt tên thống kê theo layer (nhớ lần sau). Xuất bảng CAD / Excel |
| 5 | Cột thuộc tính | Premium ▾ > Cột thuộc tính | Hiện giá trị thuộc tính (attribute) và tham số dynamic block thành cột. Tách dòng theo giá trị (vd theo K-factor, công suất) |
| 6 | Soát lỗi đếm | `LHBSOATLOI` | Báo: block bị explode thành nét rời (không được đếm), 2 tên block cùng hình (copy đổi tên), block trên layer tắt / đóng băng, block bị lật, tỉ lệ lạ, trùng / che lấp, ngoài khu vực. Double-click để zoom. Xuất Excel |
| 7 | Đánh số thiết bị | `LHBDANHSO` | SP-01, SP-02... từng loại, thứ tự trái → phải / trên → dưới / theo khu vực. Ghi chữ cạnh block (layer `LHB_DANHSO`) và / hoặc vào thuộc tính |
| 8 | Vùng bảo vệ PCCC | `LHBVUNGBV` | Nhập bán kính bảo vệ (m) cho đầu báo, đầu phun... theo tiêu chuẩn đang áp dụng. Vẽ vòng tròn, tô mờ, đánh dấu khoảng hở (2 thiết bị cách > 2R). Layer `LHB_VUNGBAOVE` không in |
| 9 | Xuất Excel có ảnh | Nút **Xuất Excel** | File `.xlsx` đúng các cột đang hiện (kể cả khu vực, thuộc tính), cột Ký hiệu là ảnh. Không cần cài Excel |
| 10 | Mẫu bảng xuất | `LHBMAUBANG` | Tiêu đề, dòng phụ (tên công trình), ẩn tiêu đề, dòng TỔNG CỘNG, header chữ hoa, font, cỡ chữ, màu nền header / tiêu đề. Lưu nhiều mẫu. Áp cho bảng CAD, Excel, bảng chiều dài, nhiều bản vẽ |
| 11 | Ribbon + palette | `LHBRIBBON`, `LHBPALETTE` | Tab Ribbon "LHB Premium" 4 nhóm, 14 nút có icon. Palette dock cạnh màn hình cùng các nút + trạng thái bản quyền |
| 12 | Thay block hàng loạt | `LHBTHAYBLOCK` | Chọn dòng trên form (hoặc quét chọn) → thay bằng block khác trong bản vẽ / block mẫu. Giữ điểm chèn, góc, layer, thuộc tính cùng tag; co tỉ lệ theo kích thước cũ; chọn chủng loại block đích. Ctrl+Z hoàn tác |
| 13 | Bản quyền | `LHBBANQUYEN` | Hiện mã máy. Dán mã kích hoạt để dùng Premium sau 30 ngày dùng thử |

## Giới hạn cần biết

- Bảng tự cập nhật: chỉ bảng **AutoCAD Table** xuất từ v9. Bảng Line + Text và bảng xuất bằng bản cũ không cập nhật được. Block đặt mới **ngoài khung vùng quét cũ** không được đếm khi cập nhật.
- Tìm block bị explode là dò hình gần đúng (khớp ≥ 70% nét, xoay 0/90/180/270°). Block nhỏ ít nét (< 3 nét đường / tròn / cung / polyline) không dò được.
- Vùng bảo vệ: add-in **không tra tiêu chuẩn**. Bán kính do người thiết kế nhập theo TCVN / NFPA đang áp dụng.
- Chiều dài theo khu vực: mỗi đoạn tính vào khu chứa trung điểm đoạn đó (đoạn dài vắt qua 2 khu tính hết vào 1 khu).
- Đơn vị bản vẽ lấy theo INSUNITS. Bản vẽ không đặt đơn vị coi là mm. Sai thì nhập tay trong hộp thoại Chiều dài (ô "1 đơn vị bản vẽ = ... mm").

## Tạo mã kích hoạt (chỉ người bán)

1. Khách gõ `LHBBANQUYEN`, bấm **Chép mã máy**, gửi mã dạng `XXXX-XXXX-XXXX-XXXX`.
2. Trên máy người bán chạy:
   ```
   LHBKeyGen.exe XXXX-XXXX-XXXX-XXXX 365
   ```
   `365` = số ngày dùng, `0` = vĩnh viễn. `LHBKeyGen.exe` và khoá bí mật `LHB_private_key.xml` nằm trong thư mục `LHB_KEYS` (ngoài mã nguồn, **không đưa lên GitHub, không gửi khách**).
3. Gửi khách chuỗi `LHB1.xxxx...`. Khách dán vào `LHBBANQUYEN` → **Kích hoạt**.

Mất file khoá bí mật thì không tạo được mã mới cho bản đã phát hành: sao lưu thư mục `LHB_KEYS`.

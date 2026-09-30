# LHB Block Scheduler — Tính năng Premium (từ v9)

Bản v9 Premium thêm 13 tính năng, tham khảo lệnh COUNT của AutoCAD 2022+ (bảng tự cập nhật, báo lỗi đếm), các add-in đếm block trên Autodesk App Store (nhiều bản vẽ, thuộc tính, xuất Excel) và lisp thống kê PCCC của Việt Nam (chiều dài ống, đánh số).

Mọi tính năng cũ (quét, block mẫu, tìm trùng, xuất bảng, phím tắt...) vẫn miễn phí.

> **Từ v9.5: đã bật bản quyền v2** — tính năng Premium dùng thử **30 ngày** (tính từ lần đầu nạp v9.5), sau đó cần mã kích hoạt `LHB2-...` (mục 13).
> Có mã review trọn đời dùng chung cho nhóm review. v9.3 – v9.4: Premium miễn phí, không cần mã.
> Thiết kế, mức an toàn, cách cấp / quản lý key: [`docs/BAN_QUYEN_VA_CAP_KEY.md`](docs/BAN_QUYEN_VA_CAP_KEY.md).

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
| 2 | Bảng tự cập nhật | `LHBCAPNHAT` | Bảng AutoCAD Table xuất từ v9 nhớ vùng quét. Sửa bản vẽ xong gõ `LHBCAPNHAT`, Enter: SL / khu vực / thuộc tính tính lại, ô thay đổi tô **đỏ**, loại block mới thêm dòng cuối. Tên, đơn vị sửa tay trong bảng giữ nguyên. Từ v9.4: tìm bảng cả trên Layout; chỉ thêm block mới đặt sau lúc xuất (bảng xuất từ v9.4); bảng bị thêm / xoá dòng, cột bằng tay thì báo và không cập nhật |
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
| 13 | Bản quyền | `LHBBANQUYEN` | Hiện mã máy (UUID bo mạch chủ) + nguồn. Dán mã `LHB2-...` → **Kích hoạt**: hiện loại key (theo máy / dùng chung / review), cấp cho ai, serial, hạn. Nút **Xoá mã**. `LHBDIAG` mục 3a ghi trạng thái bản quyền |

## Giới hạn cần biết

- Bảng tự cập nhật: chỉ bảng **AutoCAD Table** xuất từ v9. Bảng Line + Text và bảng xuất bằng bản cũ không cập nhật được. Block đặt mới **ngoài khung vùng quét cũ** không được đếm khi cập nhật.
- Tìm block bị explode là dò hình gần đúng (khớp ≥ 70% nét, xoay 0/90/180/270°). Block nhỏ ít nét (< 3 nét đường / tròn / cung / polyline) không dò được.
- Vùng bảo vệ: add-in **không tra tiêu chuẩn**. Bán kính do người thiết kế nhập theo TCVN / NFPA đang áp dụng.
- Chiều dài theo khu vực: mỗi đoạn tính vào khu chứa trung điểm đoạn đó (đoạn dài vắt qua 2 khu tính hết vào 1 khu).
- Đơn vị bản vẽ lấy theo INSUNITS. Bản vẽ không đặt đơn vị coi là mm. Sai thì nhập tay trong hộp thoại Chiều dài (ô "1 đơn vị bản vẽ = ... mm").

## Tạo mã kích hoạt (chỉ người bán)

Từ v9.5 dùng **LHBKeyGen v2** (`tools/LHBKeyGen`, hướng dẫn [`tools/LHBKeyGen/README.md`](tools/LHBKeyGen/README.md)). Mã `LHB1.` và `LHBKeyGen.exe` cũ của v9 – v9.4 không còn dùng.

1. Khách gõ `LHBBANQUYEN`, bấm **Chép mã máy**, gửi mã dạng `XXXX-XXXX-XXXX-XXXX` (key dùng chung / review không cần).
2. Cấp key bằng 1 trong 3 cách:
   - Double-click `LHBKeyGen.exe` (cạnh file khoá ký `LHB_SIGNING_KEY_kid*.txt`) → menu **1** (theo máy) hoặc **2** (dùng chung / review).
   - GitHub: tab **Actions** → **LHB - Cấp key bản quyền** → **Run workflow** (key hiện ở Summary).
   - Nhắn Claude Code: "cấp key theo máy cho ..., mã máy ..., hạn ..." (chạy workflow GitHub).
3. Gửi khách chuỗi `LHB2-...` (dạng tin nhắn / file .txt). Khách dán vào `LHBBANQUYEN` → **Kích hoạt**.

File khoá ký **không bao giờ** đưa lên GitHub (trừ ô Secrets) hay gửi khách. Mất file khoá = không cấp được key cho các bản đang nhận
khoá đó → sao lưu 2 nơi. Thu hồi key / thay khoá ký có hiệu lực từ bản add-in build sau (xem `docs/BAN_QUYEN_VA_CAP_KEY.md`).

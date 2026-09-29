# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v9.4 Premium "ổn định")

**File cần nhận:** `LHBBlockScheduler_20260929_v9.4_Premium.zip`
**MD5 DLL đúng:** `DA84C6DB97E8BFAFD4437EE1A1302C6C`
**Máy test:** AutoCAD 2021 (chạy được AutoCAD 2021 – 2024; 2025 trở lên chưa hỗ trợ)

v9.4 = v9.3 (Premium miễn phí, ký hiệu đều cỡ) + **sửa các lỗi đếm sai / ổn định đã phát hiện khi review**
(`docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md`, mục 3). Giao diện gần như giữ nguyên, thêm ô **Đếm trong XREF** và thanh trạng thái dưới form.

> Bản này build trên máy build tự động, tham chiếu AutoCAD 2021 bằng gói chính thức `AutoCAD.NET 24.0` của Autodesk
> (không dùng `libs\`). Nếu NETLOAD / kéo thả `LHB.lsp` báo lỗi nạp: gửi ảnh dòng lệnh + file `LHBDIAG_*.txt`.

## Bản này sửa gì

| # | Lỗi (mã trong tài liệu review) | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| 1 | Block trong **ARRAY** không được đếm (A1) | ARRAY liên kết là block ẩn danh `*U`, hàm duyệt gặp tên bắt đầu `*` là bỏ cả cụm | ARRAY / block ẩn danh = "vỏ trong suốt": đếm từng block bên trong, không tính là 1 tầng. Cột **Nguồn** ghi `+ ARRAY` |
| 2 | **MINSERT** 3 hàng × 4 cột chỉ đếm 1 (A2) | Không xử lý `MInsertBlock` | Đếm đủ hàng × cột, mỗi phần tử có vị trí riêng (tìm trùng, khu vực, đánh số đúng chỗ). Phần tử MINSERT không bị "Xoá bản thừa" / "Thay block" xoá cả cụm |
| 3 | **XREF** bị đếm như 1 block, block kiến trúc trong XREF lẫn vào bảng (A3) | Không kiểm tra `IsFromExternalReference` | Mặc định **bỏ qua XREF**. Ô mới **Đếm trong XREF** (hàng 2) để đếm block bên trong; block mẫu khớp được tên `XREF|TÊN` |
| 4 | Block con bị **ẩn theo chủng loại** (visibility) của dynamic block cha vẫn bị đếm (A4) | Duyệt block con không kiểm tra `Visible` | Bỏ block con đang ẩn |
| 5 | Thiết bị nằm sâu hơn **độ sâu quét** bị sót mà không biết (A5) | Độ sâu mặc định 2, không cảnh báo | Thanh trạng thái báo **đỏ ⚠** khi loại thiết bị đang đếm còn nằm sâu hơn giới hạn. Bật **Chỉ quét block mẫu**: tìm block mẫu ở **mọi tầng**, block mẫu có block con vẫn đếm là 1 thiết bị. Tuỳ chọn quét (độ sâu, block cha, chủng loại, layer, XREF) được **nhớ lại**, `LHBSCAN` + lệnh Premium dùng chung |
| 6 | Form thống kê giữ bản vẽ đã đóng / bản vẽ khác (B1) — nguy cơ crash | Form modeless không theo dõi bản vẽ | Đổi sang bản vẽ khác: form **tự ẩn**, quay lại thì hiện; đóng bản vẽ: form **tự đóng** (hộp thoại Block mẫu còn thay đổi thì tự lưu). Áp dụng cả hộp thoại Khu vực, Soát lỗi, Chiều dài, Nhiều bản vẽ, Block trùng |
| 7 | Vùng chọn **dùng chung mọi bản vẽ** (B2) | Biến static `LastSelectedObjectIds` | Mỗi form giữ vùng chọn của bản vẽ mình |
| 8 | `settings.json` / bộ mẫu hỏng khi CAD crash lúc ghi (B3) | Ghi đè thẳng file | Ghi file tạm rồi thay, giữ `.bak`; file chính hỏng thì tự đọc `.bak` |
| 9 | `LHBCAPNHAT` ghi nhầm dòng khi bảng bị thêm / xoá dòng bằng tay; kéo thêm block ngoài vùng chọn (A6) | Khớp dòng theo chỉ số; vùng quét = khung chữ nhật | Bảng bị sửa số dòng / cột → **báo và không cập nhật**. Chỉ thêm block **mới đặt sau lúc xuất** trong khung (bảng xuất từ v9.4) |
| 10 | Ảnh ký hiệu **sai hình** khi 2 bản vẽ có block cùng tên khác hình, BEDIT xong ảnh không đổi (C1) | Cache ảnh theo tên block | Tên file ảnh kèm **chữ ký nội dung** định nghĩa block |
| 11 | Ảnh tuỳ chỉnh trong AutoCAD Table trỏ `%APPDATA%` → gửi DWG mất ảnh (C2) | Không chép ảnh theo bản vẽ | Chép ảnh vào `<tên bản vẽ>_LHBImages` cạnh DWG, lưu **đường dẫn tương đối**. Bản vẽ chưa lưu: nhắc 1 lần trên dòng lệnh |
| 12 | Bảng luôn chèn vào **Model** dù đang ở Layout; `LHBCAPNHAT` không thấy bảng trên Layout (C3) | Ghi cứng Model Space | Chèn vào **không gian đang làm việc**; ở Layout hỏi dùng **tỉ lệ 1**; `LHBCAPNHAT` tìm cả Layout |
| 13 | Nhỏ: mở Excel trên .NET 8 (D3), MD5 trên máy bật FIPS (D5), số phiên bản ghi cứng 4 chỗ (E5), hướng dẫn cài đặt còn đường dẫn máy dev (E1), thay block chậm (O(n²)) | | Đã sửa; phiên bản chỉ còn 1 chỗ (`MyApp.Version`), `LHB.lsp` ghi phiên bản lúc đóng gói |

## Các bước test

Chuẩn bị 1 bản vẽ test (lưu ra file DWG), có vài block thiết bị (vd ĐÈN EXIT, đầu báo khói).

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Tắt hẳn AutoCAD, giải nén zip vào **thư mục mới**, mở AutoCAD, kéo thả `LHB.lsp`. Gõ `LHBVERSION`, rồi `LHBHELP` | MD5 = `DA84C6DB97E8BFAFD4437EE1A1302C6C`. Dòng lệnh có `[LHB] v9.4 Premium - Premium miễn phí...`. `LHBHELP` có dòng `---- PREMIUM (v9.4 Premium) ----` |
| 2 | **ARRAY**: `ARRAY` (Rectangular, liên kết - mặc định) 1 block thiết bị thành 3 hàng × 4 cột. `LHBSCAN`, chọn cả mảng | Có dòng block đó **SL = 12** (v9.3: không có dòng). Cột **Nguồn**: `Model + ARRAY`. Thanh trạng thái dưới form: `1 ARRAY (12 block bên trong)`. Double-click ô Nguồn / Ký hiệu → zoom đúng vùng mảng, 12 block sáng lên |
| 3 | **MINSERT**: gõ `MINSERT` → tên block → điểm chèn → tỉ lệ 1 → góc xoay 30 → hàng 2 → cột 3 → khoảng cách hàng / cột. `LHBSCAN` chọn nó | **SL = 6**, Nguồn `+ MINSERT`, thanh trạng thái `1 MINSERT = 6 phần tử`. Premium ▾ > Đánh số thiết bị: 6 số nằm đúng 6 vị trí |
| 4 | **XREF**: `XATTACH` 1 bản vẽ kiến trúc (có block cửa, nội thất). `LHBSCAN` → `ALL` | Không còn dòng tên XREF / block kiến trúc. Thanh trạng thái `bỏ qua 1 XREF` |
| 5 | Tích **Đếm trong XREF** (hàng 2) | Block trong XREF hiện thành dòng (tên thiết bị không có tiền tố `XREF|`), Nguồn `+ XREF`. Bỏ tích → mất. *Đang bật "Chỉ quét block mẫu" thì tích xong gõ `LHBSCAN` quét lại* |
| 6 | **Độ sâu**: tạo block `TU` chứa block thiết bị `DEN`, block `PHONG` chứa `TU`. Chèn 1 `PHONG` + 1 `DEN` rời. Tắt "Chỉ quét block mẫu", Độ sâu quét = 2, `LHBSCAN` chọn cả 2 | Thanh trạng thái chữ **đỏ ⚠**: `1 block (DEN x1) còn nằm trong block khác sâu hơn độ sâu quét 2...`. Chọn Độ sâu = 3 → SL `DEN` tăng 1, hết cảnh báo |
| 7 | Đóng form, `LHBSCAN` lại | Ô Độ sâu / Tách theo layer / Đếm trong XREF giữ đúng lựa chọn lần trước |
| 8 | **2 bản vẽ**: bản vẽ A mở form `LHBSCAN`. Mở / chuyển sang bản vẽ B | Form của A **tự ẩn**. Ở B chạy `LHBSCAN` → form B. Quay lại A → form A hiện lại; đổi "Tách theo layer" trên form A → vẫn đếm block của A |
| 9 | Đóng bản vẽ A khi form A đang mở (mở thêm cả hộp thoại Block mẫu... / Tìm trùng) | Form + hộp thoại tự đóng, **không lỗi, không treo CAD** |
| 10 | **Bảng trên Layout**: ở Model `LHBSCAN` mở form. Bấm tab Layout (form vẫn mở, không kích hoạt viewport), bấm **Xuất bảng**, chọn điểm trên Layout | Hỏi "Bảng sẽ chèn vào Layout..." → **Yes** → bảng nằm trên Layout, chữ cỡ in bình thường (tỉ lệ 1) |
| 11 | Sửa bản vẽ ở Model (COPY thêm 1 thiết bị trong vùng đã quét), về Layout gõ `LHBCAPNHAT` → Enter | Tìm thấy bảng trên Layout, ô SL tăng 1 tô **đỏ** |
| 12 | **Bảng bị sửa tay**: bảng AutoCAD Table đã xuất, xoá 1 dòng bằng tay (chọn ô > chuột phải > Rows > Delete), gõ `LHBCAPNHAT` | Báo `KHÔNG cập nhật - bảng đã bị thêm / xoá dòng hoặc cột bằng tay...`, bảng giữ nguyên. Ctrl+Z trả dòng → `LHBCAPNHAT` chạy bình thường |
| 13 | **Ảnh tuỳ chỉnh**: bản vẽ ĐÃ LƯU. Chọn 1 dòng > nút **Ảnh ▾** > Chọn file ảnh... > Xuất bảng (AutoCAD Table) | Cạnh file DWG có thư mục `<tên bản vẽ>_LHBImages` chứa ảnh. Lệnh `XREF` (bảng External References): ảnh có Saved Path `.\<tên>_LHBImages\...`. Chép DWG + thư mục ảnh sang chỗ khác, mở lại → ảnh vẫn hiện |
| 14 | **Ảnh theo nội dung**: 2 bản vẽ có block **cùng tên, khác hình**. `LHBSCAN` bản vẽ 1 rồi bản vẽ 2 | Cột Ký hiệu mỗi bản vẽ đúng hình của bản vẽ đó (v9.3: bản vẽ 2 hiện hình của bản vẽ 1). `BEDIT` sửa hình block → `LHBSCAN` lại → ảnh mới |
| 15 | Premium ▾ > **Xuất Excel** | File .xlsx tự mở như cũ |
| 16 | Chạy `LHBDIAG` sau khi test | File `LHBDIAG_*.txt` có dòng `Phiên bản: v9.4 Premium` và `Tuỳ chọn quét: [...]` |

## Cần gửi về

1. File `LHBDIAG_*.txt` (bước 16) — trong log có dòng `BlockExtractor: quét [...] -> ...` ghi số ARRAY / MINSERT / XREF từng lần quét.
2. Ảnh form thống kê ở bước 2, 3, 6 (thấy cột **Nguồn** + thanh trạng thái dưới cùng).
3. Ảnh bảng trên Layout (bước 10) và dòng lệnh của bước 12.
4. Lỗi / số liệu sai: ảnh + chạy `LHBDIAG` ngay sau khi gặp lỗi.
5. Nếu chưa test v9.1 – v9.3: xem thêm các bước trong `HUONG_DAN_TEST_20260929_v9.2_Premium.md` (ký hiệu đều cỡ, EXIT chỉ lối thoát nạn).

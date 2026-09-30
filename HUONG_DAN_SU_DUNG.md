# 📘 Hướng dẫn sử dụng LHB Block Scheduler

> Plugin AutoCAD giúp **thống kê, gộp nhóm và xuất bảng khối lượng Block** trực tiếp trên bản vẽ.

---

## 📋 Yêu cầu

- ✅ AutoCAD **2021 – 2024** bản đầy đủ 64-bit (AutoCAD LT không chạy được add-in .NET)
- ✅ Windows 10/11
- ⚠️ AutoCAD **2025 trở lên** chạy .NET 8/10: bản hiện tại (build .NET Framework 4.8) **chưa chạy được**, sẽ có bản riêng.

---

## 🚀 Cách 1: Kéo thả `LHB.lsp` (khuyến nghị)

1. Giải nén file zip (vd `LHBBlockScheduler_..._Premium.zip`) vào 1 thư mục cố định, ví dụ `D:\LHBBlockScheduler\`.
   Không chạy thẳng trong file zip.
2. Mở AutoCAD, mở 1 bản vẽ bất kỳ.
3. **Kéo thả** file `LHB.lsp` (trong thư mục vừa giải nén) vào vùng vẽ.
   - LISP tự tìm và nạp đúng `LHBBlockScheduler.dll` đi kèm (kiểm tra MD5 trong `build-info.txt`, không nạp nhầm bản cũ).
   - Có hộp thoại bảo mật → chọn **Load** / **Always Load**.
4. Dòng lệnh hiện `[LHB] v9.x Premium ...` là xong. Gõ `LHBSCAN` để thống kê, `LHBHELP` xem danh sách lệnh.

### Tự nạp mỗi lần mở AutoCAD

1. Gõ `APPLOAD` → Enter.
2. Mục **Startup Suite** → **Contents...** → **Add...** → chọn `LHB.lsp` trong thư mục add-in → **Close**.
3. (Tuỳ chọn, tránh hỏi bảo mật) `OPTIONS` → tab **Files** → **Trusted Locations** → **Add** thư mục add-in.

> 💡 Cập nhật bản mới: giải nén bản mới ra **thư mục mới**, **tắt hẳn AutoCAD**, mở lại rồi kéo thả `LHB.lsp` của bản mới
> (hoặc sửa đường dẫn trong Startup Suite). AutoCAD không gỡ được DLL đã nạp trong phiên đang chạy.

---

## 🔧 Cách 2: Nạp thủ công bằng NETLOAD

1. Gõ `NETLOAD` → Enter.
2. Chọn file `LHBBlockScheduler.dll` trong thư mục đã giải nén → **Open**.
3. Có hộp thoại bảo mật → **Load** / **Always Load**.

Lần sau mở AutoCAD phải NETLOAD lại (hoặc dùng Cách 1 + Startup Suite).

> ℹ️ Thư mục `Bundle\LHBBlockScheduler.bundle` trong mã nguồn là bundle cho **máy build** (nạp `LHBLoader.dll` đọc
> `%APPDATA%\LHBBlockScheduler\Runtime`), không dùng để cài cho người dùng.

---

## 📐 Cách sử dụng

### Lệnh 1: `LHBSCAN` — Thống kê Block (lệnh chính)

Đây là lệnh bạn sẽ dùng nhiều nhất.

1. Gõ `LHBSCAN` rồi nhấn **Enter**
2. AutoCAD sẽ yêu cầu bạn **chọn vùng**:
   - Click điểm góc thứ 1
   - Click điểm góc thứ 2 (tạo thành hình chữ nhật bao quanh các block)
   - Hoặc: gõ `ALL` rồi Enter để chọn tất cả
3. Nhấn **Enter** để xác nhận
4. Cửa sổ **thống kê Block** sẽ hiện ra

### Trong cửa sổ thống kê, bạn có thể:

| Thao tác | Cách làm |
|---|---|
| 🔍 **Tìm kiếm** | Gõ tên block vào ô tìm kiếm (hỗ trợ gõ không dấu) |
| 📊 **Sắp xếp** | Click vào tiêu đề cột để sắp xếp tăng/giảm |
| 🔗 **Gộp nhóm** | Chọn nhiều dòng (giữ Ctrl + Click) → nhấn nút **Gộp** |
| ⬆️⬇️ **Đổi thứ tự** | Chọn 1 dòng → nhấn nút **Lên** hoặc **Xuống** |
| ❌ **Xóa dòng** | Chọn dòng → nhấn nút **Xóa** |
| 📤 **Xuất bảng** | Nhấn nút **Xuất bảng** → click chọn điểm chèn trên bản vẽ |
| ↔️ **Căn lề** | Kéo chuột quét chọn các ô → nhấn **Trái / Giữa / Phải**. Bảng xuất căn đúng như vậy |
| 🔁 **Block trùng vị trí** | Cột **Trùng** báo số block cùng tên bị copy đè / che lấp nhau. Ô **Không đếm trùng** (bật sẵn) trừ phần thừa khỏi SL. Nút **Tìm trùng** mở danh sách chỗ trùng: zoom tới, khoanh đỏ, xoá bản thừa, đổi sai số vị trí và mức che lấp (%) |
| ➕ **Quét thêm** | Nút **Quét thêm** (hàng 1): chọn thêm vùng, SL cộng dồn vào bảng đang có, giữ tên / đơn vị đã sửa. Vùng đã chọn trước không đếm lại |
| 📚 **Block mẫu** | Chọn **Bộ block mẫu**, nút **Block mẫu...** mở thư viện. Ô **Chỉ quét block mẫu** bật: lúc quét chỉ dính block mẫu, block khác không được chọn; dòng được đặt tên, đơn vị, thứ tự theo thư viện. Cột **TT** chấm xanh = có trong bộ block mẫu |

### Block trong ARRAY / MINSERT / XREF, độ sâu quét (từ v9.4)

- **ARRAY** (lệnh ARRAY, kiểu liên kết): từng block trong mảng được đếm. **MINSERT** (chèn nhiều hàng × cột): đếm đủ
  hàng × cột. Cột **Nguồn** ghi `+ ARRAY` / `+ MINSERT` / `+ XREF`, rê chuột xem số block mỗi loại.
- **XREF** (bản vẽ tham chiếu ngoài): mặc định **bỏ qua** (không đếm cửa, nội thất... của bản vẽ kiến trúc). Muốn đếm block
  trong XREF: tích **Đếm trong XREF** (hàng 2). Block mẫu vẫn khớp tên với block trong XREF (tên dạng `XREF|TÊN`).
- Block con bị **ẩn theo trạng thái visibility** của dynamic block cha không còn bị đếm.
- **Độ sâu quét** (1 / 2 / 3 / Không giới hạn) và các ô tuỳ chọn được **nhớ lại** cho lần quét sau. Thanh trạng thái dưới
  cùng form ghi số liệu lần quét; chữ **đỏ ⚠** = có thiết bị nằm sâu hơn độ sâu quét chưa được đếm → tăng độ sâu.
- Ô **Chỉ quét block mẫu** bật: tìm block mẫu ở **mọi tầng** (không theo độ sâu); block mẫu có block con bên trong vẫn được
  đếm là 1 thiết bị.
- Chuyển sang bản vẽ khác: cửa sổ thống kê **tự ẩn**, quay lại bản vẽ đó thì hiện lại; đóng bản vẽ thì cửa sổ tự đóng.
- **Xuất bảng** chèn vào không gian đang làm việc: đang ở **Layout** thì bảng nằm trên Layout (hỏi dùng tỉ lệ 1 cho giấy).

### Chủng loại, kích thước của block động (từ v9.6)

- Cột **Chủng loại** = trạng thái **Visibility** của block động (vd `2 HƯỚNG`, `BỘT ABC 8KG`). Block động không có Visibility
  thì lấy tham số dạng chữ (Lookup...). Tham số **độ dài / rộng / cao, góc xoay, toạ độ, lật** (`Distance1`, `Angle1`,
  `Position1 X`, `Flip state1`...) **không** làm chủng loại → đầu báo tia chiếu kéo dài bao nhiêu cũng chung 1 dòng, chủng loại trống.
- Ô **Tách theo kích thước** (hàng 2, mặc định tắt): bật → mỗi độ dài khác nhau thành 1 dòng riêng, hiện thêm cột
  **Kích thước** (vd `12320`; 2 tham số: `1200 x 600`), bảng xuất CAD / Excel có cột này. Tắt → không tách, ẩn cột.
  Chỉ tính tham số độ dài (Linear, Polar, XY); không tính góc xoay và toạ độ điểm (vị trí nhãn...). Số làm tròn còn
  khoảng 4 chữ số có nghĩa (12320.33 → `12320`, 12.3204 → `12.32`), đơn vị = đơn vị bản vẽ. Sửa được trong ô như cột chữ khác;
  double-click tiêu đề cột để đổi tên (vd thành "Độ dài").
- Các nút **Căn lề Trái / Giữa / Phải** chuyển lên hàng 1 (cạnh nút Premium).

### Bảng xuất co giãn như Excel

- Bảng kiểu **AutoCAD Table** co giãn được như Excel: chọn bảng → kéo **grip ▲ trên đầu cột** để đổi độ rộng; chữ dài tự
  xuống dòng, dòng tự cao lên; ô ký hiệu tự co theo ô. Kéo grip ở góc phải để giãn cả bảng.
- Độ rộng cột lúc xuất = vừa chữ dài nhất trong cột (như double-click mép cột Excel), rộng hơn nếu cột trên form được kéo rộng hơn.
  Trên form: kéo mép tiêu đề cột để đổi rộng, **double-click mép cột** để vừa chữ.
- Lệnh **`LHBKHOPCOT`** (từ v9.6, không cần Premium): chọn bảng hoặc Enter (mọi bảng LHB) → mỗi cột chữ khớp lại độ rộng theo
  chữ đang có (sau khi sửa chữ trong bảng, sau `LHBCAPNHAT` thêm dòng tên dài...). Cột ký hiệu và tiêu đề gộp giữ nguyên.

> Từ v9.1 chỉ còn **thư viện block mẫu** (bỏ thư viện thiết bị cũ: nút Quy hoạch / Thêm vào TV / Chỉ đếm block có trong TV). Thư viện cũ đã có dữ liệu được tự chuyển 1 lần thành bộ mẫu tên `TV cu <tên>` (vd `TV cu default`), chọn ở ô **Bộ block mẫu** nếu muốn dùng.

### Lệnh `LHBLEGEND` — Lấy bảng Legend có sẵn làm block mẫu

1. Gõ `LHBLEGEND`, chọn bảng Legend (AutoCAD Table có cột ký hiệu là block).
2. Kiểm tra cột nào là **Ký hiệu (Block)**, **Tên thiết bị**, **Chủng loại**, **Đơn vị** (tự gợi ý theo tiêu đề cột).
3. Ô **Lưu vào bộ block mẫu** (mặc định bộ đang dùng, gõ tên mới = tạo bộ mới) → **Lưu vào bộ mẫu**. Mỗi dòng có block thành 1 block mẫu, tên thống kê = tên trong Legend. Dòng không có block trong ô ký hiệu bị bỏ qua.

### Lệnh `LHBMAU` — Thư viện block mẫu

1. Gõ `LHBMAU` (hoặc nút **Block mẫu...** trên cửa sổ thống kê) → hộp thoại **Thông tin block mẫu**.
2. **Thêm từ bản vẽ**: quét chọn các block mẫu (mỗi tên block + chủng loại thành 1 dòng). Block động chỉ có tham số độ dài / góc
   (vd đầu báo tia chiếu) có chủng loại trống = khớp mọi kích thước. Từ v9.6, block mẫu thêm từ bản cũ có chủng loại dạng
   `Distance1=47116.93...` tự để trống khi mở hộp thoại (nếu bản vẽ đang mở có block đó); lúc quét vẫn khớp đúng dù chưa sửa.
3. **Double-click** ô để sửa **Tên thống kê**, **Ghi chú** (hoặc chọn ô rồi gõ luôn / bấm F2); **Đơn vị** click 1 lần là xổ danh sách. Nút **▲ ▼** đổi thứ tự (= thứ tự trong bảng xuất), **X** xoá 1 dòng.
4. Xoá nhiều dòng: **Shift + click** chọn liền mạch, **Ctrl + click** chọn từng dòng, **Ctrl + A** chọn hết, rồi bấm phím **Delete** hoặc nút **Xoá dòng chọn**.
5. **Lưu thông tin**. Thư viện lưu cạnh add-in: `ThuVienMau\<bộ>.json` + `<bộ>.dwg` (hình block). Chưa lưu thì bấm Thoát > No để bỏ mọi thay đổi.
6. **Chèn vào bản vẽ**: chèn block mẫu đang chọn (bản vẽ chưa có block thì lấy từ file `.dwg` của thư viện).
7. **Bộ mới...**: tạo bộ mẫu khác (Data2, Nhà xưởng...), chọn bộ ở ô **Bộ mẫu**. **Xoá bộ...**: xoá hẳn bộ đang mở (không lấy lại được).

### Lệnh `LHBLENH` — Danh sách lệnh + phím tắt

1. Gõ `LHBLENH` (phím tắt mặc định `LHB`) → bảng **Danh sách lệnh LHB**: mọi lệnh của add-in kèm chức năng.
2. Bấm **Chạy** (hoặc double-click tên lệnh) để chạy lệnh.
3. Gõ phím tắt vào cột **Phím tắt** (nhiều phím tắt cách nhau dấu phẩy, vd `TK, TKB`), bấm **Lưu phím tắt** → gõ được ngay, lần sau mở AutoCAD vẫn còn.
4. Phím tắt mặc định: `LHB` = LHBLENH, `TKB` = LHBSCAN, `BLM` = LHBMAU. Nút **Mặc định** trả về bộ này.

> Phím tắt không sửa `acad.pgp`. Tên trùng lệnh có sẵn (LINE, COPY...) hoặc lệnh LISP khác bị từ chối; trùng lệnh tắt trong acad.pgp (L, C...) thì hỏi lại.

---

## ⭐ Tính năng Premium (từ v9)

Mở bằng tab Ribbon **LHB Premium**, bảng công cụ `LHBPALETTE`, nút vàng **Premium ▾** trên form thống kê, hoặc gõ lệnh. Từ v9.5 **đã bật bản quyền**: dùng thử Premium 30 ngày (tính từ lần đầu nạp v9.5), sau đó kích hoạt bằng `LHBBANQUYEN`. Chi tiết và giới hạn: file `TINH_NANG_PREMIUM.md`.

### Kích hoạt bản quyền (`LHBBANQUYEN`)
1. Gõ `LHBBANQUYEN`. Dòng đậm là trạng thái: "Dùng thử Premium: còn N ngày" / "Đã kích hoạt Premium (...)" / "Hết hạn dùng thử...".
2. **Mua key theo máy:** bấm **Chép mã máy**, gửi mã `XXXX-XXXX-XXXX-XXXX` cho người bán. Cài lại Windows không đổi mã máy; thay bo mạch chủ thì đổi.
3. Nhận key dạng `LHB2-...` → dán vào ô Mã kích hoạt (xuống dòng, dấu cách không sao) → **Kích hoạt**.
   Key dùng chung / key review (nhóm review) không cần gửi mã máy, dán là dùng.
4. **Xoá mã** để gỡ key khỏi máy (vd trước khi chuyển máy). Key báo lỗi thì đọc câu báo (chép thiếu, máy khác, hết hạn...) hoặc gửi `LHBDIAG` (mục 3a).
5. Tính năng thường (quét, xuất bảng, block mẫu, tìm trùng...) không cần key.

### Thống kê theo tầng / khu vực (`LHBKHUVUC`)
1. Vẽ polyline kín bao từng tầng / khu (nên có chữ tên khu bên trong).
2. `LHBKHUVUC` → **Chọn đường bao...** → chọn các polyline. Tên khu tự lấy theo chữ to nhất bên trong, sửa được.
3. **▲ ▼** đổi thứ tự cột, **Lưu & áp dụng**. Form thống kê có thêm cột SL mỗi khu (đứng trước cột SL), bảng xuất và Excel cũng có.

### Bảng tự cập nhật (`LHBCAPNHAT`)
Xuất bảng kiểu **AutoCAD Table**. Sửa bản vẽ xong gõ `LHBCAPNHAT`, chọn bảng hoặc Enter (mọi bảng LHB, cả trên Layout). Ô thay đổi chữ đỏ, loại block mới thêm dòng cuối, dòng tổng tính lại.
Từ v9.4: chỉ thêm block **mới đặt sau lúc xuất bảng** trong khung vùng quét (không kéo block cũ nằm ngoài vùng chọn ban đầu); bảng bị **thêm / xoá dòng, cột bằng tay** thì không cập nhật (tránh ghi nhầm dòng) — xuất lại bảng mới. Sửa chữ tên / đơn vị trong bảng vẫn được giữ.

### Xuất Excel
Nút **Xuất Excel** (cạnh Xuất bảng): file `.xlsx` đúng các cột đang hiện, có ảnh ký hiệu, tiêu đề / dòng tổng theo mẫu bảng.

### Mẫu bảng xuất (`LHBMAUBANG`)
Tiêu đề, dòng phụ (tên công trình), ẩn tiêu đề, dòng TỔNG CỘNG, header chữ HOA, font, hệ số cỡ chữ / chiều cao dòng, màu nền header / tiêu đề (mã màu AutoCAD). Đặt tên mẫu mới rồi **Lưu & dùng mẫu này**.

### Cột thuộc tính
Premium ▾ > **Cột thuộc tính...**: tích **Hiện cột** để thêm cột giá trị attribute / tham số dynamic block; tích **Tách dòng** để mỗi giá trị khác nhau thành 1 dòng.

### Soát lỗi đếm (`LHBSOATLOI`)
Liệt kê block bị explode (không được đếm), block khác tên nhưng cùng hình, block trên layer tắt / đóng băng, block lật, tỉ lệ lạ, trùng / che lấp, ngoài khu vực. Double-click dòng để zoom. **Khoanh tất cả** vẽ vòng trên layer `LHB_SOATLOI` (không in).

### Đánh số thiết bị (`LHBDANHSO`)
Chọn dòng trên form (không chọn = tất cả). Đặt tiền tố + số bắt đầu từng loại, thứ tự, số chữ số, cao chữ. Chữ số nằm trên layer `LHB_DANHSO` (in được); có thể ghi thêm vào 1 thuộc tính của block. **Xoá số đã đánh** xoá hết chữ số.

### Vùng bảo vệ PCCC (`LHBVUNGBV`)
Nhập bán kính bảo vệ (m) theo tiêu chuẩn áp dụng cho từng loại thiết bị → **Vẽ vùng bảo vệ**. Vòng tròn trên layer `LHB_VUNGBAOVE` (không in), đoạn đỏ đánh dấu 2 thiết bị gần nhau nhất mà cách hơn 2R. Bán kính được nhớ cho lần sau.

### Thay block hàng loạt (`LHBTHAYBLOCK`)
Chọn dòng trên form → Premium ▾ > **Thay block...** → chọn block đích (block trong bản vẽ hoặc block mẫu) + chủng loại. Giữ điểm chèn, góc xoay, layer, thuộc tính cùng tag. Ctrl+Z hoàn tác.

### Chiều dài ống / dây (`LHBCHIEUDAI`)
**Quét chọn...** hoặc **Toàn bản vẽ** → chiều dài theo layer (m). Nhập % hao hụt, tách theo khu vực, double-click cột Tên thống kê để đặt tên (nhớ theo layer). Đơn vị bản vẽ tự theo INSUNITS; sai thì nhập "1 đơn vị bản vẽ = ... mm".

### Nhiều bản vẽ (`LHBNHIEUBV`)
**Thêm file DWG...** → **Thống kê**: mỗi bản vẽ 1 cột SL + cột Tổng, dùng bộ block mẫu đang chọn. Không cần mở các bản vẽ.

> 💡 Mang sang máy khác: copy nguyên thư mục add-in (có thư mục `ThuVienMau`). Giải nén bản add-in mới sang thư mục khác trên cùng máy thì thư viện tự chép từ bản dự phòng `%APPDATA%\LHBBlockScheduler\ThuVienMau`.

> 💡 **Mẹo:** Đặt tên thống kê, đơn vị, thứ tự trong bộ block mẫu (`LHBMAU` hoặc `LHBLEGEND`) — lần sau quét bản vẽ khác có cùng block, dòng **tự đặt tên** theo bộ mẫu đang chọn.

---

### Lệnh `LHBLOG` — Xem log khi có lỗi

Nếu gặp lỗi, gõ `LHBLOG` → Enter → file log sẽ mở bằng Notepad.

---

## ❓ Xử lý sự cố

### Plugin không tự nạp khi mở AutoCAD

- Kiểm tra `APPLOAD` → **Startup Suite** có `LHB.lsp` và đường dẫn còn đúng (thư mục add-in không bị xoá / đổi tên)
- Kéo thả lại `LHB.lsp`; dòng lệnh báo "đang chạy bản cũ" → tắt hẳn AutoCAD rồi mở lại
- Thử dùng **Cách 2 (NETLOAD)** để test

### NETLOAD báo lỗi bảo mật

1. Gõ `SECURELOAD` → Enter
2. Nhập `0` → Enter
3. Thử NETLOAD lại

### Gõ lệnh LHBSCAN nhưng không nhận

- Kiểm tra command line có hiện `=== LHBBlockScheduler đã nạp ===` không
- Nếu không thấy → plugin chưa được nạp → thử NETLOAD lại

### Lệnh LHBSCAN báo lỗi

1. Gõ `LHBLOG` → Enter để xem chi tiết lỗi
2. Gửi nội dung file log cho người hỗ trợ kỹ thuật

---

## 🔄 Cập nhật plugin khi có phiên bản mới

1. **Đóng AutoCAD hoàn toàn**.
2. Giải nén bản mới ra **thư mục mới** (giữ thư mục cũ tới khi bản mới chạy tốt). Thư viện block mẫu (`ThuVienMau`) tự
   chép sang thư mục mới từ bản dự phòng trong `%APPDATA%\LHBBlockScheduler\ThuVienMau`.
3. Mở AutoCAD, kéo thả `LHB.lsp` của bản mới (Startup Suite: bỏ `LHB.lsp` cũ, thêm `LHB.lsp` mới).
4. Gõ `LHBVERSION` kiểm tra MD5 đúng với file hướng dẫn test của bản đó.

> ⚠️ **Quan trọng:** AutoCAD giữ DLL đã nạp tới khi tắt hẳn chương trình — không chép đè DLL khi AutoCAD đang mở.

---

## 📝 Tổng hợp các lệnh

| Lệnh | Mô tả |
|---|---|
| `LHBSCAN` | Quét block trên bản vẽ → mở giao diện thống kê đầy đủ tính năng |
| `LHBMAU` | Thư viện block mẫu: thêm / xoá / sắp xếp / đặt tên thống kê / chèn block mẫu |
| `LHBLENH` | Danh sách lệnh, chạy lệnh, đổi phím tắt (mặc định gõ `LHB`) |
| `LHBLEGEND` | Quét bảng Legend (Table) có sẵn trên bản vẽ vào bộ block mẫu |
| `LHBCLEARCACHE` | Xoá toàn bộ bộ nhớ đệm hình ảnh thumbnail (%APPDATA%\LHBBlockScheduler\Thumbs) |
| `LHBRELOAD` | Nạp lại phiên bản mới nhất từ thư mục Runtime (dành cho Dev, không cần tắt CAD) |
| `LHBLOG` | Mở file nhật ký ghi lỗi (log.txt) |
| `LHBDUPCLEAR` | Xoá vòng đỏ và đường dẫn đánh dấu block trùng (layer `LHB_BLOCK_TRUNG`, không in) |
| `LHBKHOPCOT` | Khớp độ rộng cột bảng AutoCAD Table đã xuất theo chữ (như double-click mép cột Excel) |
| `LHBHELP` | In danh sách các lệnh khả dụng ra Command Line |
| `LHBKHUVUC` | ⭐ Tầng / khu vực: bảng có cột SL từng khu |
| `LHBCAPNHAT` | ⭐ Cập nhật bảng AutoCAD Table đã xuất sau khi sửa bản vẽ |
| `LHBNHIEUBV` | ⭐ Thống kê nhiều bản vẽ DWG cùng lúc |
| `LHBCHIEUDAI` | ⭐ Chiều dài ống / dây theo layer |
| `LHBSOATLOI` | ⭐ Soát lỗi đếm |
| `LHBDANHSO` | ⭐ Đánh số thiết bị tự động |
| `LHBVUNGBV` | ⭐ Vẽ vùng bảo vệ PCCC |
| `LHBTHAYBLOCK` | ⭐ Thay block hàng loạt |
| `LHBMAUBANG` | ⭐ Mẫu bảng xuất |
| `LHBPALETTE` | Bật / tắt bảng công cụ LHB dock cạnh màn hình |
| `LHBRIBBON` | Bật / tắt tab Ribbon LHB Premium |
| `LHBBANQUYEN` | Mã máy, kích hoạt / xoá mã bản quyền Premium (`LHB2-...`) |


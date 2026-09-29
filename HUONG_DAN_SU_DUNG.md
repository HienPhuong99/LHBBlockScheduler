# 📘 Hướng dẫn sử dụng LHB Block Scheduler

> Plugin AutoCAD giúp **thống kê, gộp nhóm và xuất bảng khối lượng Block** trực tiếp trên bản vẽ.

---

## 📋 Yêu cầu

- ✅ AutoCAD 2021 trở lên (bản 64-bit)
- ✅ Windows 10/11

---

## 🚀 Cách 1: Cài đặt tự động (Khuyến nghị — chỉ cần làm 1 lần)

Cách này giúp plugin **tự nạp mỗi khi mở AutoCAD**, không cần thao tác gì thêm.

### Bước 1: Đóng AutoCAD hoàn toàn

Nếu AutoCAD đang mở, hãy **đóng hoàn toàn** (không chỉ đóng bản vẽ, mà đóng cả chương trình).

### Bước 2: Copy thư mục plugin

1. Mở **File Explorer** (phím tắt: `Win + E`)

2. Vào thư mục sau (copy dán vào thanh địa chỉ):
   ```
   C:\Users\vsp\Downloads\LHBBlockScheduler\Bundle\
   ```

3. Bạn sẽ thấy thư mục:
   ```
   📁 LHBBlockScheduler.bundle
   ```

4. **Copy** thư mục `LHBBlockScheduler.bundle` (Chuột phải → Copy, hoặc `Ctrl+C`)

5. Mở thư mục đích bằng cách **copy dán đường dẫn sau** vào thanh địa chỉ File Explorer:
   ```
   %APPDATA%\Autodesk\ApplicationPlugins
   ```
   > 💡 Nếu thư mục `ApplicationPlugins` chưa tồn tại, hãy tạo mới:
   > - Vào `%APPDATA%\Autodesk\`
   > - Chuột phải → New → Folder → đặt tên `ApplicationPlugins`

6. **Dán** thư mục vào đây (Chuột phải → Paste, hoặc `Ctrl+V`)

7. Kết quả cuối cùng phải có cấu trúc như sau:
   ```
   📁 %APPDATA%\Autodesk\ApplicationPlugins\
       📁 LHBBlockScheduler.bundle\
           📄 PackageContents.xml
           📁 Contents\
               📄 LHBBlockScheduler.dll
   ```

### Bước 3: Kiểm tra thư mục Contents có file DLL chưa

Mở thư mục `Contents` bên trong `LHBBlockScheduler.bundle`:
- Nếu **đã có** file `LHBBlockScheduler.dll` → bỏ qua, sang Bước 4
- Nếu **chưa có** → copy file từ đường dẫn sau:
  ```
  C:\Users\vsp\Downloads\LHBBlockScheduler\bin\Debug\net48\LHBBlockScheduler.dll
  ```
  Dán vào thư mục `Contents`.

### Bước 4: Mở AutoCAD

Mở AutoCAD bình thường. Plugin sẽ **tự động nạp**. Bạn sẽ thấy dòng thông báo ở command line:
```
=== LHBBlockScheduler đã nạp ===
```

✅ **Xong!** Plugin đã sẵn sàng sử dụng.

---

## 🔧 Cách 2: Nạp thủ công bằng NETLOAD (mỗi lần mở AutoCAD)

Dùng cách này nếu bạn chỉ muốn **thử nghiệm** hoặc không muốn cài cố định.

### Bước 1: Mở AutoCAD

Mở AutoCAD và mở bất kỳ bản vẽ nào.

### Bước 2: Gõ lệnh NETLOAD

1. Click vào **command line** ở dưới cùng màn hình AutoCAD
2. Gõ:
   ```
   NETLOAD
   ```
3. Nhấn **Enter**

### Bước 3: Chọn file DLL

Cửa sổ chọn file sẽ hiện ra. Tìm đến:
```
C:\Users\vsp\Downloads\LHBBlockScheduler\bin\Debug\net48\LHBBlockScheduler.dll
```

Click **Open**.

### Bước 4: Xác nhận

Nếu có hộp thoại hỏi về bảo mật, chọn **Load** hoặc **Always Load**.

Bạn sẽ thấy dòng thông báo:
```
=== LHBBlockScheduler đã nạp ===
```

✅ **Xong!** Nhưng lần sau mở AutoCAD phải NETLOAD lại.

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
| 💾 **Lưu mẫu** | Nhấn nút **Lưu Data** → đặt tên (gợi ý: đặt `default`) |
| 📥 **Nạp mẫu** | Chọn tên mẫu đã lưu từ dropdown |
| 📤 **Xuất bảng** | Nhấn nút **Xuất bảng** → click chọn điểm chèn trên bản vẽ |
| ↔️ **Căn lề** | Kéo chuột quét chọn các ô → nhấn **Trái / Giữa / Phải**. Bảng xuất căn đúng như vậy |
| 🔁 **Block trùng vị trí** | Cột **Trùng** báo số block cùng tên bị copy đè / che lấp nhau. Ô **Không đếm trùng** (bật sẵn) trừ phần thừa khỏi SL. Nút **Tìm trùng** mở danh sách chỗ trùng: zoom tới, khoanh đỏ, xoá bản thừa, đổi sai số vị trí và mức che lấp (%) |
| ➕ **Quét thêm** | Nút **Quét thêm** (hàng 3): chọn thêm vùng, SL cộng dồn vào bảng đang có, giữ tên / đơn vị đã sửa. Vùng đã chọn trước không đếm lại |
| 📚 **Block mẫu** | Chọn **Bộ block mẫu**, nút **Block mẫu...** mở thư viện. Ô **Chỉ quét block mẫu** bật: lúc quét chỉ dính block mẫu, block khác không được chọn; dòng được đặt tên, đơn vị, thứ tự theo thư viện |

### Lệnh `LHBMAU` — Thư viện block mẫu

1. Gõ `LHBMAU` (hoặc nút **Block mẫu...** trên cửa sổ thống kê) → hộp thoại **Thông tin block mẫu**.
2. **Thêm từ bản vẽ**: quét chọn các block mẫu (mỗi tên block + chủng loại thành 1 dòng).
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

## ⭐ Tính năng Premium (v9)

Mở bằng tab Ribbon **LHB Premium**, bảng công cụ `LHBPALETTE`, nút vàng **Premium ▾** trên form thống kê, hoặc gõ lệnh. Dùng thử 30 ngày, sau đó kích hoạt bằng `LHBBANQUYEN`. Chi tiết và giới hạn: file `TINH_NANG_PREMIUM.md`.

### Thống kê theo tầng / khu vực (`LHBKHUVUC`)
1. Vẽ polyline kín bao từng tầng / khu (nên có chữ tên khu bên trong).
2. `LHBKHUVUC` → **Chọn đường bao...** → chọn các polyline. Tên khu tự lấy theo chữ to nhất bên trong, sửa được.
3. **▲ ▼** đổi thứ tự cột, **Lưu & áp dụng**. Form thống kê có thêm cột SL mỗi khu (đứng trước cột SL), bảng xuất và Excel cũng có.

### Bảng tự cập nhật (`LHBCAPNHAT`)
Xuất bảng kiểu **AutoCAD Table**. Sửa bản vẽ xong gõ `LHBCAPNHAT`, chọn bảng hoặc Enter (mọi bảng LHB). Ô thay đổi chữ đỏ, loại block mới thêm dòng cuối, dòng tổng tính lại.

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

> 💡 **Mẹo:** Lưu mẫu với tên `default` — lần sau quét bản vẽ khác có cùng block, plugin sẽ **tự động áp dụng** tên hiển thị và nhóm đã lưu.

---

### Lệnh 2: `LHBSCANTEST` — Test nhanh

Dùng để kiểm tra xem plugin có nhận diện đúng block không, **không mở cửa sổ**.

1. Gõ `LHBSCANTEST` → Enter
2. Chọn vùng → Enter
3. Kết quả in ra ở command line, ví dụ:
   ```
   --- Kết quả quét: 3 loại Block ---
   Cửa Sổ : 4
   Cửa Đi : 3
   sprinkler : 4
   ```

---

### Lệnh 3: `LHBTHUMBTEST` — Test ảnh thumbnail

1. Gõ `LHBTHUMBTEST` → Enter
2. Click chọn 1 block trên bản vẽ
3. Plugin sẽ xuất ảnh PNG và mở lên cho bạn xem

---

### Lệnh 4: `LHBLOG` — Xem log khi có lỗi

Nếu gặp lỗi, gõ `LHBLOG` → Enter → file log sẽ mở bằng Notepad.

---

## ❓ Xử lý sự cố

### Plugin không tự nạp khi mở AutoCAD

- Kiểm tra thư mục `%APPDATA%\Autodesk\ApplicationPlugins\LHBBlockScheduler.bundle\` có đúng cấu trúc không
- Kiểm tra file `LHBBlockScheduler.dll` có nằm trong thư mục `Contents\` không
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

1. **Đóng AutoCAD hoàn toàn**
2. Copy file `LHBBlockScheduler.dll` mới (từ thư mục `bin\Debug\net48\`)
3. Dán đè vào:
   ```
   %APPDATA%\Autodesk\ApplicationPlugins\LHBBlockScheduler.bundle\Contents\
   ```
4. Mở lại AutoCAD

> ⚠️ **Quan trọng:** Phải đóng AutoCAD trước khi copy đè file DLL, vì AutoCAD đang giữ file khi chạy.

---

## 📝 Tổng hợp các lệnh

| Lệnh | Mô tả |
|---|---|
| `LHBSCAN` | Quét block trên bản vẽ → mở giao diện thống kê đầy đủ tính năng |
| `LHBMAU` | Thư viện block mẫu: thêm / xoá / sắp xếp / đặt tên thống kê / chèn block mẫu |
| `LHBLENH` | Danh sách lệnh, chạy lệnh, đổi phím tắt (mặc định gõ `LHB`) |
| `LHBSCANTEST` | Quét block nhanh → chỉ in kết quả ra Command Line (không mở Form) |
| `LHBTHUMBTEST` | Chọn 1 block → test xuất ảnh thumbnail PNG qua GraphicsSystem |
| `LHBLEGEND` | Quét bảng Legend (Table) có sẵn trên bản vẽ vào Thư viện thiết bị |
| `LHBCLEARCACHE` | Xoá toàn bộ bộ nhớ đệm hình ảnh thumbnail (%APPDATA%\LHBBlockScheduler\Thumbs) |
| `LHBRELOAD` | Nạp lại phiên bản mới nhất từ thư mục Runtime (dành cho Dev, không cần tắt CAD) |
| `LHBLOG` | Mở file nhật ký ghi lỗi (log.txt) |
| `LHBDUPCLEAR` | Xoá vòng đỏ và đường dẫn đánh dấu block trùng (layer `LHB_BLOCK_TRUNG`, không in) |
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
| `LHBBANQUYEN` | Mã máy, kích hoạt bản quyền Premium |


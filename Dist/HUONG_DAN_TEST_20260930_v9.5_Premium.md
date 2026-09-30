# Hướng dẫn test LHBBlockScheduler — bản 30/09/2026 (v9.5 Premium — bản quyền v2)

**File cần nhận:** `LHBBlockScheduler_20260930_v9.5_Premium.zip` + `KEY_REVIEW_LHB.txt` (key review, gửi riêng)
**MD5 DLL đúng:** `D1626C1147BC58F3AC0FED232D10BC95`
**Máy test:** AutoCAD 2021 (chạy được AutoCAD 2021 – 2024; 2025 trở lên chưa hỗ trợ)

v9.5 = v9.4 "ổn định" (sửa đếm ARRAY / MINSERT / XREF, form theo bản vẽ...) + **bản quyền v2, đã BẬT bản quyền**:
tính năng Premium dùng thử 30 ngày, sau đó cần mã kích hoạt `LHB2-...`. Tính năng thường (quét, xuất bảng, block mẫu, tìm trùng...) không cần mã.
Thiết kế và mức an toàn: `docs/BAN_QUYEN_VA_CAP_KEY.md` trên GitHub.

> Bản này build trên máy build tự động, tham chiếu AutoCAD 2021 bằng gói chính thức `AutoCAD.NET 24.0` của Autodesk.
> Nếu NETLOAD / kéo thả `LHB.lsp` báo lỗi nạp: gửi ảnh dòng lệnh + file `LHBDIAG_*.txt`.

## Bản này sửa gì

| # | Vấn đề (mã trong tài liệu review) | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| 1 | Bản quyền đang tắt, Premium ai cũng dùng (F1) | `Enforced = false` từ v9.3 | Bật bản quyền cùng hệ thống v2 bên dưới: dùng thử 30 ngày rồi cần key |
| 2 | Key ký RSA-1024, dưới chuẩn (F2) | Thiết kế v9 | Key `LHB2-...` ký **ECDSA P-256**. Add-in chỉ có khoá công khai → không làm được key giả. Key `LHB1.` cũ không còn dùng |
| 3 | Máy cài Ghost trùng mã máy, cài lại Windows đổi mã (F3) | Mã máy lấy từ `MachineGuid` của Windows | Mã máy lấy từ **UUID bo mạch chủ**: cài lại Windows không đổi; bo mạch không có UUID thì dùng `MachineGuid` (hộp thoại ghi rõ nguồn) |
| 4 | Xoá 1 giá trị registry là dùng thử lại, lùi đồng hồ không bị phát hiện (F4) | Ngày dùng thử lưu 1 chỗ, không kiểm | Lưu 2 nơi có mã kiểm (HMAC) gắn mã máy: xoá 1 nơi tự ghi lại; sửa tay / chép máy khác → hết hạn; **lùi ngày giờ máy** → tạm khoá tới khi chỉnh lại |
| 5 | Không thu hồi được key, 1 loại key (F6) | Thiết kế v9 | 3 loại key: **theo máy**, **dùng chung**, **review**. Key có serial, tên người được cấp, hạn; thu hồi theo serial ở bản sau |
| 6 | Báo lỗi key chung chung "không hợp lệ" | | Báo rõ: chép thiếu ký tự, key của máy khác (in cả 2 mã máy), hết hạn ngày..., đã thu hồi, key LHB1 cũ. Dán key lẫn xuống dòng, dấu cách, dấu `–` (Zalo / Word), cả câu nhắn chứa key đều đọc được |
| 7 | Hộp thoại `LHBBANQUYEN` | | Hiện loại key, cấp cho ai, serial, hạn, nguồn mã máy; nút **Xoá mã**. `LHBDIAG` thêm mục **3a. BẢN QUYỀN** |

## Các bước test

Làm bước 1 – 5 **trước** khi nhập key (để thấy chế độ dùng thử).

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Tắt hẳn AutoCAD, giải nén zip vào **thư mục mới**, mở AutoCAD, kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `D1626C1147BC58F3AC0FED232D10BC95`. Dòng lệnh lúc nạp: `[LHB] v9.5 Premium - Dùng thử Premium: còn 30 ngày. Tab Ribbon...` |
| 2 | Gõ `LHBBANQUYEN` | Dòng đậm màu cam `Dùng thử Premium: còn 30 ngày`, dòng xám `Chưa nhập mã kích hoạt.` Mã máy dạng `XXXX-XXXX-XXXX-XXXX` (không có chữ I, O, số 0, 1). `Nguồn mã máy: UUID bo mạch chủ (cài lại Windows không đổi mã)`. Bấm **Chép mã máy** → nút đổi thành `Đã chép`, dán vào Notepad đúng mã |
| 3 | Đóng hộp thoại. Gõ `LHBKHUVUC` (hoặc tính năng Premium bất kỳ) | Chạy bình thường. Dòng lệnh 1 lần: `[LHB Premium] Đang dùng thử, còn 30 ngày. Gõ LHBBANQUYEN để kích hoạt.` |
| 4 | Tắt / mở lại AutoCAD, kéo thả `LHB.lsp`, `LHBBANQUYEN` | Mã máy **giống hệt** bước 2, vẫn `còn 30 ngày` |
| 5 | Ô **Mã kích hoạt** gõ `LHB2-ABCDE` → **Kích hoạt**. Rồi thử `LHB1.ABCD` | Lần 1 báo `Mã bị thiếu ký tự (chép chưa hết?)`. Lần 2 báo `Mã dạng LHB1 (bản v9 - v9.4) không còn dùng từ v9.5...`. Trạng thái không đổi |
| 6 | Mở `KEY_REVIEW_LHB.txt`, chép dòng key `LHB2-...` (dán cả đoạn cũng được), dán vào ô Mã kích hoạt → **Kích hoạt** | Hộp thoại `Đã kích hoạt Premium (bản review (dùng chung), trọn đời) - cấp cho Nhóm review LHB 2026` |
| 7 | `LHBBANQUYEN` lại | Dòng đậm màu **xanh** như bước 6; dòng xám `Serial 64F68B85 · cấp ngày 30/09/2026 · key dùng chung - đừng gửi người ngoài nhóm`. Form thống kê: nút **Premium ▾** cuối menu và `LHBPALETTE` hiện cùng trạng thái |
| 8 | Tắt / mở lại AutoCAD, kéo thả `LHB.lsp` | Dòng lệnh lúc nạp: `[LHB] v9.5 Premium - Đã kích hoạt Premium (bản review (dùng chung), trọn đời) - cấp cho Nhóm review LHB 2026...`. Tính năng Premium không còn nhắc dùng thử |
| 9 | `LHBBANQUYEN` → **Xoá mã** → Yes | Trạng thái về `Dùng thử Premium: còn 30 ngày` (không mất ngày, không reset). Dán lại key review → **Kích hoạt** |
| 10 | `LHBDIAG` | File `LHBDIAG_*.txt` có mục `--- 3a. BẢN QUYỀN ---`: `Bắt bản quyền (Enforced) : True`, mã máy + nguồn, trạng thái, `Key đang lưu : serial 64F68B85, kid 1, bản review (dùng chung)...` |
| 11 | *(Tuỳ chọn, máy phụ, làm khi CHƯA có key hoặc sau Xoá mã)* Tắt AutoCAD. `regedit` → `HKEY_CURRENT_USER\Software\LHBBlockScheduler` → xoá giá trị `P2`. Mở AutoCAD, kéo thả `LHB.lsp`, `LHBBANQUYEN`, quay lại regedit bấm F5 | Giá trị `P2` **tự có lại**; số ngày dùng thử giữ như trước (không tính lại từ đầu). Log có dòng `[License] Ghi lại dữ liệu dùng thử (registry=True, file=False)` |
| 12 | *(Tuỳ chọn, như bước 11)* Chỉnh ngày Windows **lùi 3 ngày**, gõ `LHBBANQUYEN` | Báo `Ngày giờ máy đang lùi về trước lần dùng gần nhất (dd/MM/yyyy) - chỉnh lại ngày giờ để dùng thử tiếp`, Premium bị chặn. Chỉnh lại ngày đúng → `LHBBANQUYEN` về số ngày cũ. **Nhớ chỉnh lại ngày giờ máy** |
| 13 | Chưa test v9.4: làm thêm các bước 2 – 5, 8 – 9 trong `HUONG_DAN_TEST_20260929_v9.4_Premium.md` (ARRAY, MINSERT, XREF, 2 bản vẽ) | Như hướng dẫn v9.4 |

## Gửi key review cho đồng nghiệp

Gửi **2 file**: zip v9.5 + `KEY_REVIEW_LHB.txt`. Đồng nghiệp cài như thường (giải nén, kéo thả `LHB.lsp`), gõ `LHBBANQUYEN`,
dán key → **Kích hoạt**. Không cần gửi mã máy. Key dùng được mọi máy, trọn đời → **chỉ gửi người trong nhóm review**
(lọt ra ngoài thì thu hồi được ở bản sau, xem `docs/BAN_QUYEN_VA_CAP_KEY.md` mục 4).

## Cần gửi về

1. File `LHBDIAG_*.txt` (bước 10) — mục 3a + các dòng `[License]` trong log (mã máy, nguồn, kiểm key, dùng thử).
2. Ảnh hộp thoại `LHBBANQUYEN` ở bước 2 và bước 7.
3. Nếu nguồn mã máy **không** phải "UUID bo mạch chủ", hoặc mã máy bước 4 khác bước 2: báo ngay (kèm LHBDIAG).
4. Lỗi / câu báo lạ khi kích hoạt: ảnh + `LHBDIAG` ngay sau khi gặp.
5. Kết quả các bước v9.4 (bước 13): ảnh form ở bước ARRAY / MINSERT (thấy cột **Nguồn** + thanh trạng thái).

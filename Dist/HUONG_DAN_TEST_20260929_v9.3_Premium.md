# Hướng dẫn test LHBBlockScheduler — bản 29/09/2026 (v9.3 Premium)

**File cần nhận:** `LHBBlockScheduler_20260929_v9.3_Premium.zip`
**MD5 DLL đúng:** `D3E5A61DB704B975DB5D97ADD35CABC6`
**Máy test:** AutoCAD 2021

v9.3 = v9.2 (ký hiệu đều cỡ, xem `HUONG_DAN_TEST_20260929_v9.2_Premium.md`) + **Premium miễn phí, chưa bật bản quyền**.

## Bản này sửa gì

1. **Chưa bắt bản quyền** (yêu cầu 29/09/2026): mọi máy dùng full tính năng Premium, **không cần mã kích hoạt**, không hiện thông báo "Đang dùng thử, còn N ngày", chưa bắt đầu đếm 30 ngày dùng thử.
2. Không để 1 mã chung sẵn trong ô được: mã kích hoạt gắn theo **mã máy** (chữ ký RSA), mỗi mã chỉ đúng 1 máy. Muốn 1 mã chung cho mọi máy thì phải nhúng khoá bí mật vào DLL → ai cũng tự tạo được mã. Nên thay bằng công tắc trong code (`LicenseManager.Enforced = false`).
3. `LHBBANQUYEN` vẫn mở được: trạng thái xanh "Premium miễn phí (chưa bật bản quyền, không cần mã)", ô mã ghi sẵn "Bản hiện tại miễn phí...". Bấm **Kích hoạt** báo không cần mã. Dán mã thật `LHB1....` vẫn kiểm tra như cũ (để thử công cụ tạo mã).
4. Khi nào bảo **"bắt bản quyền"**: tôi đổi công tắc, build bản mới → dùng thử 30 ngày rồi cần mã theo mã máy.

## Các bước test

| # | Thao tác | Kết quả đúng |
|---|---|---|
| 1 | Tắt hẳn AutoCAD, giải nén zip vào thư mục mới, kéo thả `LHB.lsp`. Gõ `LHBVERSION` | MD5 = `D3E5A61DB704B975DB5D97ADD35CABC6`. Dòng lệnh có `[LHB] v9.3 Premium - Premium miễn phí (chưa bật bản quyền, không cần mã)` |
| 2 | Gõ `LHBBANQUYEN` | Trạng thái chữ xanh "Premium miễn phí...", ô mã ghi "Bản hiện tại miễn phí...". Bấm **Kích hoạt** → báo không cần mã |
| 3 | `LHBSCAN` > nút **Premium ▾** | Dòng cuối menu: "Premium miễn phí (chưa bật bản quyền, không cần mã)". Mở các mục Premium không hỏi mã, không có dòng "Đang dùng thử" |
| 4 | Gõ `LHBPALETTE` | Dòng trạng thái: "Premium miễn phí..." |
| 5 | Xuất bảng (AutoCAD Table), sửa bản vẽ, `LHBCAPNHAT` | Cập nhật bảng chạy bình thường (tính năng Premium) |

## Cần gửi về

1. Ảnh hộp thoại `LHBBANQUYEN` (bước 2).
2. Nếu chưa test v9.2: ảnh bảng xuất có cột Ký hiệu (EXIT chỉ lối phải đều cỡ các dòng khác) + file `LHBDIAG_*.txt`.

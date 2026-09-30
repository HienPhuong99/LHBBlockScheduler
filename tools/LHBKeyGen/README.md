# LHBKeyGen v2 — cấp mã kích hoạt LHB Premium

Công cụ **chỉ dành cho người bán**. Dùng cho add-in **từ v9.5** (mã `LHB2-...`). Mã `LHB1.` của v9 – v9.4 không còn dùng.
Thiết kế, mức an toàn, cách quản lý key: [`docs/BAN_QUYEN_VA_CAP_KEY.md`](../../docs/BAN_QUYEN_VA_CAP_KEY.md).

## File cần có (để cùng thư mục với `LHBKeyGen.exe`)

| File | Là gì | Ai được giữ |
|---|---|---|
| `LHBKeyGen.exe` (+ `.exe.config`) | Công cụ (chạy trên Windows có .NET Framework 4.8 — có sẵn trên Windows 10 / 11) | Người bán |
| `LHB_SIGNING_KEY_kid1.txt` | **Khoá ký bí mật.** Ai có file này tạo được key hợp lệ | **Chỉ người bán.** Không đưa lên GitHub, không gửi qua Zalo / email |
| `LHB_KEY_LEDGER.jsonl` | Sổ key: mỗi dòng 1 key đã cấp (khách, mã máy, hạn, ghi chú) | Người bán (có thông tin khách) |

Mất file khoá ký = không cấp được key mới cho các bản add-in đang nhận khoá đó → **sao lưu 2 nơi** (USB cất riêng + trình quản lý mật khẩu / ổ đám mây có mã hoá).

## Dùng hằng ngày: double-click `LHBKeyGen.exe`

Hiện menu tiếng Việt:

```
  1. Cấp key THEO MÁY (khách gửi mã máy trong LHBBANQUYEN)
  2. Cấp key DÙNG CHUNG / REVIEW (không gắn máy - chỉ gửi người tin cậy)
  3. Kiểm tra 1 key
  4. Xem / tìm sổ key
  5. Thu hồi 1 key
  6. Xuất sổ key ra Excel (CSV)
  7. Mã máy của máy này
  8. Tạo khoá ký mới (kid mới)
  9. Đặt / đổi mật khẩu file khoá ký
  0. Thoát
```

**Cấp key cho khách mua (mục 1):**
1. Khách gõ `LHBBANQUYEN` trong AutoCAD → **Chép mã máy** → gửi mã dạng `XXXX-XXXX-XXXX-XXXX`.
2. Chọn `1`, dán mã máy, nhập tên khách, hạn (Enter = trọn đời, `+365` = 1 năm, `31/12/2027`), ghi chú nội bộ (SĐT, số đơn).
3. Key được chép sẵn vào clipboard và ghi vào sổ. Gửi khách nguyên chuỗi `LHB2-...`; khách dán vào `LHBBANQUYEN` → **Kích hoạt**.

**Key dùng chung / review (mục 2):** không gắn máy — ai có chuỗi key đều dùng được trên mọi máy. Chỉ gửi nhóm tin cậy,
đặt tên nhóm rõ ràng (hiện trong `LHBBANQUYEN` của người dùng: "cấp cho ..."). Muốn chấm dứt: thu hồi (mục 5) + phát hành bản mới.

**Khách báo key không chạy (mục 3):** dán key → công cụ kiểm bằng **đúng hàm kiểm của add-in** và in lý do
(sai mã máy, hết hạn, đã thu hồi, add-in chưa nhận khoá...). Kiểm cả mã máy: dòng lệnh `verify KEY --machine XXXX-...`.

## Lệnh dòng lệnh

```
LHBKeyGen init       [--kid 1] [--out file] [--no-password | --password]
LHBKeyGen protect    [--key file]
LHBKeyGen pubkey     [--key file]
LHBKeyGen issue      --kind machine|floating|review --label "Tên" [--machine XXXX-XXXX-XXXX-XXXX]
                     [--expires never|+365|2027-12-31|31/12/2027] [--seats N] [--note ".."]
                     [--key file | --key-env BIEN] [--ledger file] [--summary file] [--by tên] [--no-clip]
LHBKeyGen verify     LHB2-... [--machine XXXX-...] [--key file] [--ledger file]
LHBKeyGen list       [--find chữ] [--ledger file]
LHBKeyGen revoke     SERIAL [--reason ".."] [--ledger file]
LHBKeyGen export-csv [--ledger file] [--out file.csv]
LHBKeyGen machine
```

- File khoá mặc định: `LHB_SIGNING_KEY*.txt` cạnh exe hoặc thư mục đang đứng (nhiều file → kid lớn nhất), hoặc biến `LHB_SIGNING_KEY_FILE`.
- Sổ key mặc định: `LHB_KEY_LEDGER.jsonl` cạnh file khoá, hoặc biến `LHB_KEY_LEDGER`.
- Mật khẩu file khoá: hỏi trên màn hình, hoặc biến `LHB_KEY_PASSPHRASE`.
- Mã thoát: 0 = xong / key hợp lệ; 1 = lỗi; 2 = key không đọc được; 3 = key không dùng được.

Ví dụ:

```bat
LHBKeyGen issue --kind machine --machine 7KQ2-MX9D-4TPA-HW3E --label "Cty PCCC ABC - anh Nam" --expires +365 --note "0909xxx, đơn 12"
LHBKeyGen issue --kind review --label "Nhóm review LHB 2026"
LHBKeyGen verify LHB2-AJASG-... --machine 7KQ2-MX9D-4TPA-HW3E
LHBKeyGen revoke 64F68B85 --reason "lộ key lên nhóm Facebook"
```

## Loại key

| Loại | Gắn máy | Dùng cho | Chấm dứt |
|---|---|---|---|
| `machine` (theo máy) | Có — mã máy = UUID bo mạch chủ | Khách mua lẻ | Hết hạn, hoặc thu hồi + bản mới |
| `floating` (dùng chung) | Không | Công ty mua nhiều suất, tự quản lý nội bộ | Hết hạn, hoặc thu hồi + bản mới |
| `review` (dùng chung) | Không | Đồng nghiệp / người review dùng thử lâu dài | Thu hồi, hoặc tắt `AcceptReviewKeys` ở bản mới (mọi key review hết tác dụng) |

## Sau khi thu hồi / tạo khoá mới — phải build add-in bản mới

Add-in chạy offline nên chỉ biết những gì được build vào nó (`Core/LicensePolicy.cs`):

- **Thu hồi:** lệnh `revoke` in sẵn các dòng `0xSERIAL, // tên - lý do` → dán vào `RevokedSerials` → build + phát hành bản mới.
  Bản cũ đang chạy vẫn nhận key đó (giới hạn của bản quyền offline).
- **Khoá ký mới (kid 2, 3...):** `init --kid 2` → dán dòng in ra (hoặc `pubkey`) vào `TrustedKeys` → build bản mới. Giữ kid cũ
  trong `TrustedKeys` để key đã bán vẫn chạy; bỏ kid cũ = mọi key ký bằng kid đó hết hiệu lực ở bản mới.

## Cấp key trên GitHub (không cần máy tính)

Workflow [`.github/workflows/lhb-cap-key.yml`](../../.github/workflows/lhb-cap-key.yml): tab **Actions** → **LHB - Cấp key bản quyền**
→ **Run workflow** → chọn việc (issue / verify / revoke / list), loại key, mã máy, tên khách... Key hiện ở **Summary** của lần chạy,
sổ key lưu ở nhánh riêng `lhb-license-ledger`. Cài đặt 1 lần (secret `LHB_SIGNING_KEY`): xem `docs/BAN_QUYEN_VA_CAP_KEY.md` mục 6.

## Build

```bat
dotnet build tools\LHBKeyGen\LHBKeyGen.csproj -c Release
```

Ra 2 bản: `bin\Release\net48\LHBKeyGen.exe` (Windows, không cần cài thêm) và `bin\Release\net8.0\LHBKeyGen.dll` (chạy `dotnet LHBKeyGen.dll`, dùng cho GitHub Actions / Linux).
Mã dùng chung với add-in (đọc / ký / kiểm key, mã máy): `Core/LicenseCodec.cs`, `Core/LicensePolicy.cs`, `Core/MachineFingerprint.cs`.
Kiểm thử ngoài AutoCAD: `dotnet run --project tests/LHBLicenseTests` (63 kiểm tra).

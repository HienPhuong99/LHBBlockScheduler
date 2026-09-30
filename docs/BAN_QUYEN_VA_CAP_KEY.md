# Bản quyền LHB Premium v2 và hệ thống cấp key

> **Ngày:** 30/09/2026 · **Áp dụng:** add-in từ **v9.5 Premium** (mã `LHB2-...`), công cụ `tools/LHBKeyGen` v2, workflow GitHub `LHB - Cấp key bản quyền`.
> Mã lỗi F1–F6 là mục 3.6 trong [`REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md`](REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md).

## Mục lục

1. [Trả lời nhanh](#1-trả-lời-nhanh)
2. [Thiết kế bản quyền v2](#2-thiết-kế-bản-quyền-v2)
3. [Mức an toàn thực tế: bẻ được gì, không bẻ được gì](#3-mức-an-toàn-thực-tế-bẻ-được-gì-không-bẻ-được-gì)
4. [Key review trọn đời cho đồng nghiệp](#4-key-review-trọn-đời-cho-đồng-nghiệp)
5. [Hệ thống cấp key: 3 cách cấp](#5-hệ-thống-cấp-key-3-cách-cấp)
6. [Cài đặt cấp key trên GitHub (1 lần)](#6-cài-đặt-cấp-key-trên-github-1-lần)
7. [Giữ khoá ký: quy tắc bắt buộc](#7-giữ-khoá-ký-quy-tắc-bắt-buộc)
8. [Phương án quản lý đề xuất theo giai đoạn](#8-phương-án-quản-lý-đề-xuất-theo-giai-đoạn)
9. [Tình huống thường gặp](#9-tình-huống-thường-gặp)
10. [Việc cần làm ngay](#10-việc-cần-làm-ngay)
11. [Mã nguồn và kiểm thử](#11-mã-nguồn-và-kiểm-thử)

---

## 1. Trả lời nhanh

**Cơ chế bảo mật đã làm chưa?** Có từ v9 nhưng yếu và **đang tắt** từ v9.3 (`LicenseManager.Enforced = false`, Premium miễn phí).
v9.5 làm lại toàn bộ (bản quyền v2) và **bật**: chưa có key thì dùng thử Premium 30 ngày, sau đó cần mã kích hoạt.
Tính năng thường (quét, xuất bảng, block mẫu, tìm trùng...) vẫn miễn phí, không cần key.

**Có khả năng bị bẻ không?** Tuỳ cách bẻ:

| Cách bẻ | v9 – v9.4 | v9.5 |
|---|---|---|
| Tự làm key giả (keygen lậu) | Khó nhưng RSA-1024 dưới chuẩn | **Không làm được.** Add-in chỉ có khoá công khai ECDSA P-256; làm key giả phải phá chữ ký ~2^128 phép tính |
| Sửa key có sẵn (đổi hạn, đổi máy, đổi loại) | Không | Không — sửa 1 ký tự là sai chữ ký (đã kiểm từng ký tự, mục 11) |
| Dùng key theo máy trên máy khác | Máy cài Ghost trùng `MachineGuid` là dùng được | Không — mã máy lấy từ UUID bo mạch chủ |
| Dùng lại 30 ngày dùng thử | Xoá 1 giá trị registry | Phải biết và xoá **cả 2 nơi**; sửa tay / chép máy khác → hết hạn; lùi đồng hồ → khoá tạm |
| **Sửa DLL** bằng dnSpy / ILSpy (cho hàm kiểm luôn trả "hợp lệ") | Làm được | **Vẫn làm được** — đúng với mọi add-in .NET chạy offline. Cách giảm ở [mục 8](#8-phương-án-quản-lý-đề-xuất-theo-giai-đoạn) |
| Chia sẻ key dùng chung / review | — | Được (theo thiết kế): ai có chuỗi key là dùng được. Chỉ gửi người tin cậy; thu hồi có hiệu lực từ bản sau |

Tóm lại: v9.5 **chặn được người dùng bình thường và keygen lậu**, không chặn được người rành .NET quyết tâm sửa DLL.
Không có bản quyền offline nào chặn được việc đó; phần mềm lớn chống bằng cập nhật thường xuyên + dịch vụ online + giá hợp lý.

**Cần repo hay MCP gì trên GitHub không?** Không cần thêm gì:

- **GitHub Actions** (có sẵn, repo private miễn phí 2.000 phút/tháng; mỗi lần cấp key ~1 phút) chạy `LHBKeyGen` để cấp key từ trình duyệt / app GitHub trên điện thoại.
- **GitHub Secrets** giữ khoá ký (mã hoá, không ai đọc lại được kể cả chủ repo, chỉ workflow dùng).
- **GitHub MCP** (đã kết nối trong phiên Claude Code) chạy được workflow đó → nhắn Claude "cấp key..." là xong ([mục 5](#5-hệ-thống-cấp-key-3-cách-cấp)).
- **Không** dùng thư viện bản quyền bên ngoài trong add-in (quy tắc dự án: không thêm DLL ngoài vào acad.exe). ECDSA, SHA-256, HMAC có sẵn trong .NET Framework 4.8.
- Sau này khi bán thật: máy chủ bản quyền nhỏ + cổng thanh toán Việt Nam (payOS / SePay) — [mục 8](#8-phương-án-quản-lý-đề-xuất-theo-giai-đoạn). Không cần bây giờ.

---

## 2. Thiết kế bản quyền v2

| Phần | v9 – v9.4 | v9.5 (bản quyền v2) |
|---|---|---|
| Chữ ký key | RSA-1024 | **ECDSA P-256 / SHA-256** (chuẩn hiện hành, chữ ký 64 byte), ký trên `"LHB-LICENSE-V2\|" + nội dung` |
| Khoá ký | 1 khoá, không thay được | Nhiều khoá theo **số hiệu (kid)** → thay khoá mà key đã bán vẫn chạy. File khoá đặt được **mật khẩu** (PBKDF2-SHA256 600.000 vòng + AES-256 + HMAC) |
| Mã máy | `MachineGuid` của Windows (Ghost trùng mã, cài lại Windows đổi mã) | **UUID bo mạch chủ** (SMBIOS, đọc bằng `GetSystemFirmwareTable`, không cần quyền admin): cài lại Windows không đổi, clone ổ đĩa sang máy khác thì khác. Bo mạch báo UUID rác → dự phòng `MachineGuid` |
| Loại key | Chỉ theo máy | **Theo máy** · **Dùng chung** (công ty / nhóm) · **Review** (dùng chung, bản sau có thể thôi nhận cả loại) |
| Nội dung key | Mã máy, ngày, hạn | + serial, gói tính năng (Premium; chừa chỗ Pro / Team), số máy dự kiến, **"cấp cho"** (tên khách hiện trong `LHBBANQUYEN`) |
| Thu hồi | Không | Danh sách serial thu hồi (`LicensePolicy.RevokedSerials`), có hiệu lực từ bản build sau |
| Dùng thử 30 ngày | 1 chuỗi ngày trong registry | 2 nơi (registry `HKCU\Software\LHBBlockScheduler\P2` + file `%LOCALAPPDATA%\LHBBlockScheduler\p2.dat`), có **HMAC gắn mã máy**; xoá 1 nơi → tự ghi lại; sửa / chép máy khác → hết hạn; **lùi đồng hồ** → tạm 0 ngày tới khi chỉnh lại giờ |
| Báo lỗi | "Mã không hợp lệ" | Nói rõ: chép thiếu ký tự, key máy khác (in 2 mã máy), hết hạn ngày..., đã thu hồi, khoá ký chưa nhận, key LHB1 cũ |
| Công cụ cấp key | `LHBKeyGen.exe MÃMÁY SỐNGÀY` | LHBKeyGen v2: menu tiếng Việt + lệnh, **sổ key**, kiểm key bằng đúng hàm kiểm của add-in, thu hồi, xuất Excel, **GitHub Actions** |
| Kiểm thử | Tay | 63 kiểm tra tự động ngoài AutoCAD (`tests/LHBLicenseTests`) |

**Định dạng key:** `LHB2-` + Base32 chia nhóm 5 ký tự. Bảng chữ không có `I`, `O`, `0`, `1` (khỏi nhầm khi gõ lại);
dán có xuống dòng, dấu cách, chữ thường, dấu gạch bị đổi thành `–` (Zalo / Word), ký tự vô hình, hoặc dán cả câu nhắn chứa key đều đọc được. Nội dung: phiên bản · kid · loại · cờ · gói tính năng · serial · ngày cấp · hạn ·
mã máy (10 byte) · số máy · "cấp cho" (≤ 48 byte UTF-8) · chữ ký 64 byte. Key dài ~180–270 ký tự kể cả dấu gạch (tuỳ độ dài tên "cấp cho") → gửi dạng tin nhắn / file .txt, không đọc qua điện thoại.

**Add-in kiểm theo thứ tự** (`Core/LicensePolicy.Validate`, dùng chung với `LHBKeyGen verify`):
đọc được key → kid có trong add-in → chữ ký đúng → chưa bị thu hồi → loại review còn được nhận → đúng mã máy (key theo máy) → còn hạn → có gói Premium.
Kết quả nhớ theo key + ngày (không kiểm chữ ký lại mỗi lần bấm nút).

**Dùng thử bắt đầu** lúc nạp v9.5 lần đầu (tên dữ liệu mới, máy đã chạy v9 – v9.2 vẫn được đủ 30 ngày).
Không đọc / ghi được registry / file (máy khoá quyền) → cho dùng hôm đó và ghi log, không khoá oan.

---

## 3. Mức an toàn thực tế: bẻ được gì, không bẻ được gì

| Tấn công | Chặn bởi | Còn hở / cách giảm |
|---|---|---|
| Keygen lậu, key giả | ECDSA P-256, add-in không chứa khoá bí mật | Chỉ khi **lộ file khoá ký** → [mục 7](#7-giữ-khoá-ký-quy-tắc-bắt-buộc) (thay kid) |
| Sửa key (hạn, loại, mã máy, tên) | Chữ ký phủ toàn bộ nội dung | — |
| Key theo máy đem sang máy khác | Mã máy = UUID bo mạch chủ | Máy ảo nhân bản giữ nguyên UUID; bo mạch rẻ không có UUID (dự phòng `MachineGuid`, Ghost trùng) |
| Chia sẻ key dùng chung / review | Không chặn được khi offline | Gửi đúng người, tên nhóm rõ; lộ → thu hồi serial + bản mới. Chặn thật cần kiểm online (giai đoạn 2) |
| Reset dùng thử | 2 nơi + HMAC + phát hiện lùi đồng hồ | Xoá **cả 2** nơi, hoặc tạo user Windows mới → dùng thử lại. Chặn thật cần dùng thử theo tài khoản online |
| Sửa DLL (dnSpy / ILSpy) | Không | Obfuscate + ký số DLL + cập nhật thường xuyên + đưa phần giá trị lên dịch vụ (giai đoạn 3). Làm chậm, không chặn tuyệt đối |
| Bản cũ vẫn nhận key đã thu hồi | Không (offline) | Khuyến khích cập nhật (bản mới có tính năng / sửa lỗi); giai đoạn 2 kiểm online |

**Vì sao v9.5 vẫn đáng làm dù sửa DLL được:** đa số người dùng không sửa DLL; khi có key giả / keygen lậu lan truyền thì mất
doanh thu hàng loạt — v9.5 chặn đúng chỗ đó. Người sửa DLL phải sửa lại mỗi bản mới; bản crack luôn cũ.

---

## 4. Key review trọn đời cho đồng nghiệp

| | |
|---|---|
| Serial | `64F68B85` |
| Loại | Review (dùng chung, **không gắn máy**) |
| Cấp cho | Nhóm review LHB 2026 |
| Hạn | Trọn đời · gói Premium · khoá ký kid 1 |
| Chuỗi key | File `KEY_REVIEW_LHB.txt` gửi riêng (không đưa lên repo; có trong sổ key) |

**Gửi đồng nghiệp:** file zip v9.5 + `KEY_REVIEW_LHB.txt`. Đồng nghiệp cài như thường (kéo thả `LHB.lsp`), gõ `LHBBANQUYEN`,
dán key → **Kích hoạt** → thấy "Đã kích hoạt Premium (bản review (dùng chung), trọn đời) - cấp cho Nhóm review LHB 2026".
Không cần gửi mã máy.

**Nếu key lọt ra ngoài nhóm:** `LHBKeyGen revoke 64F68B85 --reason "..."` → dán dòng in ra vào `RevokedSerials` → build bản mới
(bản mới không nhận key này) → cấp key review mới với tên khác cho nhóm. Khi bán chính thức có thể đặt
`LicensePolicy.AcceptReviewKeys = false` để mọi key review hết tác dụng ở bản đó.

---

## 5. Hệ thống cấp key: 3 cách cấp

Hướng dẫn công cụ: [`tools/LHBKeyGen/README.md`](../tools/LHBKeyGen/README.md). Loại key nên dùng:

| Khách | Loại key | Ghi chú |
|---|---|---|
| Mua lẻ 1 máy | `machine` + hạn 1 năm hoặc trọn đời | Khách gửi mã máy trong `LHBBANQUYEN` |
| Công ty mua N suất | N key `machine` (khuyên dùng), hoặc 1 key `floating` ghi `--seats N` | Key dùng chung không đếm được số máy khi offline → chỉ cho khách tin cậy |
| Người review / đồng nghiệp | `review` | Bản sau có thể thôi nhận cả loại |

**Cách A — máy Windows của bạn:** double-click `LHBKeyGen.exe` (cạnh file khoá ký + sổ key) → menu `1` (theo máy) hoặc `2` (dùng chung / review).
Key tự chép vào clipboard, tự ghi sổ.

**Cách B — GitHub (trình duyệt / app GitHub trên điện thoại):** repo → **Actions** → **LHB - Cấp key bản quyền** → **Run workflow** →
chọn `issue`, loại key, dán mã máy, tên khách, hạn → **Run**. Mở lần chạy → **Summary** có key. Sổ key tự lưu ở nhánh `lhb-license-ledger`.
Cũng dùng được: `verify` (dán key vào ô target), `revoke` (serial vào ô target, lý do vào ô note), `list` (tìm trong sổ).

**Cách C — nhắn Claude Code** (phiên có GitHub MCP, sau khi làm [mục 6](#6-cài-đặt-cấp-key-trên-github-1-lần)):
"Cấp key theo máy cho Cty ABC, mã máy XXXX-XXXX-XXXX-XXXX, hạn 1 năm, ghi chú đơn 12". Claude chạy workflow ở cách B và
đọc key trong log lần chạy. Claude không cần (và không nên được đưa) file khoá ký.

**Chọn 1 sổ chính.** Cách A ghi `LHB_KEY_LEDGER.jsonl` trên máy bạn, cách B/C ghi nhánh `lhb-license-ledger` trên GitHub.
Khuyên dùng GitHub làm sổ chính (có lịch sử, không mất khi hỏng máy); cấp trên máy thì chép dòng mới vào sổ GitHub, hoặc ngược lại
tải `lhb-ledger.jsonl` về đổi tên thành `LHB_KEY_LEDGER.jsonl` để xem bằng menu 4 / xuất Excel bằng menu 6.

---

## 6. Cài đặt cấp key trên GitHub (1 lần)

1. **Merge PR** chứa v9.5 vào `main` (workflow chỉ hiện ở tab Actions khi file `.github/workflows/lhb-cap-key.yml` có trên nhánh mặc định).
2. Repo → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:
   - Name: `LHB_SIGNING_KEY`
   - Secret: mở `LHB_SIGNING_KEY_kid1.txt` bằng Notepad, chép **toàn bộ** nội dung dán vào.
   - Nếu đã đặt mật khẩu cho file khoá (`LHBKeyGen protect`): thêm secret `LHB_KEY_PASSPHRASE` = mật khẩu.
3. Thử: **Actions** → **LHB - Cấp key bản quyền** → **Run workflow** → action `list` → phải chạy xanh.
4. Cấp thử 1 key `review` tên "Thử", rồi `revoke` serial của nó (để quen thao tác).

**Ai thấy được gì:** chỉ người có quyền **ghi** repo chạy được workflow. Người có quyền **đọc** repo xem được key trong log và
sổ key (tên khách, SĐT trong ghi chú) → repo giữ private, không thêm cộng tác viên không tin cậy. Không ai (kể cả bạn) đọc lại
được giá trị secret sau khi lưu; workflow không in khoá ra log.

---

## 7. Giữ khoá ký: quy tắc bắt buộc

File `LHB_SIGNING_KEY_kid*.txt` là **thứ duy nhất tạo được key hợp lệ**. Lộ = ai cũng làm key được; mất = không cấp được key mới
cho các bản đang nhận kid đó.

1. **Sao lưu 2 nơi:** USB cất riêng + trình quản lý mật khẩu (Bitwarden / 1Password...) hoặc ổ đám mây có mã hoá. Xoá bản trong Downloads sau khi chép.
2. **Đặt mật khẩu:** `LHBKeyGen protect` (menu 9), tối thiểu 10 ký tự, lưu mật khẩu trong trình quản lý mật khẩu.
3. **Không bao giờ** commit vào repo (`.gitignore` đã chặn `LHB_SIGNING_KEY*`, `LHB_KEY_LEDGER*`), không gửi qua Zalo / email / Drive chia sẻ.
4. **Tạo khoá riêng cho việc bán (kid 2) trên máy bạn** — khuyên làm trước khi bán key đầu tiên. Khoá kid 1 được tạo trong phiên
   Claude Code trên cloud (container tạm) và gửi cho bạn qua cuộc trò chuyện, nên coi như "khoá thử / khoá review":
   1. `LHBKeyGen init --kid 2` (menu 8) trên máy bạn, đặt mật khẩu.
   2. Gửi Claude **chỉ dòng khoá công khai** in ra (`{ 2, "..." },` — không bí mật) → thêm vào `TrustedKeys`, build bản mới.
   3. Từ bản đó cấp key bán bằng kid 2 (cập nhật secret `LHB_SIGNING_KEY` trên GitHub = file kid 2). Giữ kid 1 để key review vẫn chạy;
      khi thôi review, bỏ kid 1 khỏi `TrustedKeys` ở bản sau.
5. **Nếu lộ khoá:** tạo kid mới → bỏ kid bị lộ khỏi `TrustedKeys` → build bản mới → cấp lại key cho khách hợp lệ theo sổ key
   (lọc `list --find` / CSV) → báo khách cập nhật.

---

## 8. Phương án quản lý đề xuất theo giai đoạn

| Giai đoạn | Khi nào | Làm gì | Chi phí |
|---|---|---|---|
| **1. Offline + GitHub** (đang có) | Review, bán thử, < 50 khách | Key ký ECDSA, cấp bằng LHBKeyGen / GitHub Actions / nhắn Claude, sổ key trên nhánh riêng. Khách chuyển khoản → bạn chạy workflow → gửi key qua Zalo | 0đ |
| **2. Máy chủ bản quyền + thanh toán tự động** | Bắt đầu bán thật / quảng cáo | Cloudflare Workers + D1 (gói miễn phí): `activate / validate / deactivate`, trả "lease" có chữ ký hạn 7–30 ngày để chạy offline tới hạn. payOS / SePay webhook → tự cấp key < 1 phút. Khách tự chuyển máy, **thu hồi có hiệu lực ngay**, key dùng chung **đếm được số máy thật**. Giữ key offline cho doanh nghiệp không có mạng | 0–3tr/năm |
| **3. Chống sửa DLL** | Có bản crack lưu hành | Obfuscator (Obfuscar miễn phí / ConfuserEx) chạy lúc build, **test kỹ trong AutoCAD** (lệnh `[CommandMethod]`, `DataContract`, Ribbon qua reflection dễ hỏng khi đổi tên); ký số DLL (~5,5–6tr/năm, AutoCAD bớt cảnh báo khi nạp); kiểm tra rải nhiều chỗ; đưa phần giá trị lên dịch vụ (thư viện ký hiệu, bộ luật TCVN cập nhật) | 0–6,5tr + ký số |

**Vì sao chưa làm giai đoạn 2, 3 ngay:** chưa có kết quả test v9 – v9.5 trong AutoCAD; obfuscate mà không test được trong AutoCAD dễ làm
add-in không nạp được; máy chủ online cần tên miền, chính sách bảo mật dữ liệu khách, và nên đi cùng website bán hàng.
Thiết kế v2 đã chừa chỗ: gói tính năng (bit Pro / Team), kid, loại key, serial → nối máy chủ sau không phải đổi định dạng key.

**So sánh nhanh cách quản lý:**

| Cách | Ưu | Nhược |
|---|---|---|
| Tự làm offline (hiện tại) | 0đ, chạy không cần mạng, bạn nắm toàn bộ | Không thu hồi tức thì, không đếm máy của key dùng chung |
| Keygen.sh (dịch vụ) | Có sẵn máy chủ, dashboard | USD, dữ liệu khách ở nước ngoài, add-in phải gọi API ngoài |
| Tự làm Cloudflare + payOS / SePay | Rẻ, tự động từ thanh toán tới key, VietQR | Cần 2–3 tuần làm + bảo trì |
| Chợ Autodesk (entitlement theo Autodesk ID) | Autodesk lo thanh toán | Cần giao diện tiếng Anh, khách Việt ít mua qua chợ |

---

## 9. Tình huống thường gặp

| Tình huống | Xử lý |
|---|---|
| Khách cài lại Windows | Mã máy không đổi (UUID bo mạch) → dán lại key cũ |
| Khách thay bo mạch / đổi máy | Mã máy đổi → cấp key mới theo mã máy mới, thu hồi key cũ (`revoke`), ghi chú "chuyển máy" |
| Khách dùng PC + laptop | 2 key theo máy (khuyên), hoặc 1 key dùng chung `--seats 2` cho khách tin cậy |
| Nguồn mã máy hiện "MachineGuid" | Bo mạch không có UUID dùng được; cài lại Windows sẽ đổi mã → cấp lại khi khách báo |
| Key báo "Mã bị thiếu / thừa ký tự" | Khách chép thiếu → gửi lại dạng file .txt |
| Key báo "khoá số N mà bản add-in này chưa nhận" | Khách dùng bản add-in cũ hơn khoá ký → gửi bản mới |
| Máy khách từng để sai ngày (năm sau) rồi chỉnh lại | Dùng thử báo "Ngày giờ máy đang lùi..." tới khi qua ngày sai đó → cấp key theo máy hạn ngắn (`--expires +30`) |
| Khách gửi `LHBDIAG_*.txt` | Mục **3a. BẢN QUYỀN**: mã máy, nguồn, key đang lưu (serial, loại, hạn), lý do không nhận, số ngày dùng thử |
| Kiểm key khách gửi | `LHBKeyGen verify KEY --machine MÃMÁY` (hoặc workflow `verify`) — báo đúng như add-in |

---

## 10. Việc cần làm ngay

- [ ] Chép `LHB_SIGNING_KEY_kid1.txt` + `LHB_KEY_LEDGER.jsonl` vào chỗ an toàn (2 nơi), xoá bản trong Downloads.
- [ ] (Nên) `LHBKeyGen protect` đặt mật khẩu file khoá.
- [ ] Gửi đồng nghiệp: zip v9.5 + `KEY_REVIEW_LHB.txt`; nhờ gửi lại `LHBDIAG_*.txt` nếu lỗi.
- [ ] Merge PR vào `main`, thêm secret `LHB_SIGNING_KEY`, chạy thử workflow `list`.
- [ ] Trước khi bán key đầu tiên: tạo kid 2 trên máy bạn, gửi Claude dòng khoá công khai.

---

## 11. Mã nguồn và kiểm thử

| File | Vai trò |
|---|---|
| `Core/LicenseCodec.cs` | Định dạng key LHB2, Base32, ký / kiểm ECDSA (dùng chung add-in + LHBKeyGen) |
| `Core/LicensePolicy.cs` | Khoá công khai theo kid, serial thu hồi, nhận key review, hàm kiểm `Validate` (dùng chung) |
| `Core/MachineFingerprint.cs` | Mã máy từ UUID SMBIOS (dự phòng MachineGuid) |
| `Core/TrialLogic.cs` | Quy tắc dùng thử 2 nơi + HMAC + lùi đồng hồ (hàm thuần, có kiểm thử) |
| `Core/LicenseManager.cs` | `Enforced = true`, trạng thái, dùng thử (đọc / ghi registry + file), log `[License]` |
| `UI/LicenseDialog.cs` | `LHBBANQUYEN`: mã máy + nguồn, key, chi tiết (serial, loại, hạn), Kích hoạt / Xoá mã |
| `Commands.cs` | `LHBDIAG` mục 3a. BẢN QUYỀN |
| `tools/LHBKeyGen/` | LHBKeyGen v2 (net48 exe cho Windows + net8.0 cho GitHub Actions): `Program.cs`, `KeyFile.cs`, `Ledger.cs` |
| `.github/workflows/lhb-cap-key.yml` | Cấp / kiểm / thu hồi / tra key trên GitHub, sổ key ở nhánh `lhb-license-ledger` |
| `tests/LHBLicenseTests/` | 63 kiểm tra: Base32, mã máy, cấp + kiểm mọi loại key, hạn, sửa từng ký tự, key giả, thu hồi, nhãn, key dán kiểu Zalo / Word hoặc cả tin nhắn, 3000 chuỗi ngẫu nhiên, dùng thử, SMBIOS, file khoá có mật khẩu |

Chạy kiểm thử (không cần AutoCAD): `dotnet run --project tests/LHBLicenseTests -c Release`.
Workflow đã chạy thử trên repo git cục bộ: cấp (tạo nhánh sổ lần đầu), cấp tiếp, xem sổ, thu hồi, khoá có mật khẩu (thiếu / có secret),
thu hồi khi chưa có sổ, chữ khách nhập chứa `$(...)` không chạy thành lệnh; nhánh `main` không bị đụng.

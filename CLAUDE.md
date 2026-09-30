# Dự án: LHBBlockScheduler — AutoCAD Add-in thống kê Block

## Ngữ cảnh kỹ thuật (BẮT BUỘC tuân thủ)
- C#, SDK-style csproj, x64. Từ v9.7 đa đích (mỗi dòng AutoCAD 1 DLL): `net48` = AutoCAD 2021 – 2024 (AcMgd, AcDbMgd,
  AcCoreMgd trong `libs\`), `net8.0-windows` = 2025 – 2026, `net10.0-windows` = 2027 (NuGet `AutoCAD.NET` 25.0.1 / 26.0.0
  CHỈ để biên dịch, `ExcludeAssets="runtime"`). Mặc định chỉ build net48; `-p:LhbAllTargets=true` build cả 3 -> `bin\Release\
  net48|net8|net10` (cần .NET 10 SDK). Code chỉ cho .NET 8/10 đặt trong `#if NET` (`MyApp.BuildTarget`, `UiKit.KeepFrameworkFont`).
- 3 DLL AutoCAD LUÔN `<Private>false</Private>`. Không bao giờ đổi thành true (NuGet: luôn `ExcludeAssets="runtime"`).
- UI: WinForms viết bằng code (không Designer.cs).
- JSON: `Core/JsonHelper.cs` (DataContractJsonSerializer). KHÔNG dùng System.Text.Json (crash trong acad.exe).
  DataContractJsonSerializer không chạy property initializer -> field bool mới đặt tên sao cho mặc định = false.
- Dữ liệu: `%APPDATA%\LHBBlockScheduler\` (settings.json, Libraries\, Thumbs\, log.txt). Thư mục gốc add-in (cạnh LHB.lsp:
  ThuVienMau\, log.txt, LHBDIAG) = `Logger.AddinRootFolder` (`AddinPaths.ResolveRoot`: DLL ở thư mục con net8 / net10 -> thư mục cha).
- Xuất bảng 2 kiểu: `TableExporterAcad` (AutoCAD Table, mặc định) và `TableExporter` (Line + DBText, "cũ").

## Quy tắc code AutoCAD API (sai là crash CAD)
1. Mọi truy cập database trong `using (var tr = db.TransactionManager.StartTransaction())` + `tr.Commit()`.
2. KHÔNG gọi AutoCAD API từ background thread / async.
3. Tên block dùng `br.DynamicBlockTableRecord`. Bỏ qua block tên bắt đầu "*".
4. Form mở modeless. Ghi database từ modeless form cần `using (doc.LockDocument())`.
5. Không serialize ObjectId ra JSON.
6. Block ĐÈN EXIT (dynamic) ném `eInvalidExtents` với `GeometricExtents` -> luôn dùng `ExtentsHelper`.

## Logging (thay cho debug)
- Không debug được. Log: `Core/Logger.cs` -> `%APPDATA%\LHBBlockScheduler\log.txt`.
- Máy dev KHÔNG có AutoCAD. User test trên máy khác và gửi về file `LHBDIAG_*.txt` (đặt tại
  `C:\Users\vsp\Downloads\tool\`). Khi user báo lỗi: đọc file LHBDIAG/log trước, không hỏi mô tả bằng lời.
- Sửa xong 1 lỗi, thêm log chi tiết ở đúng chỗ đó.

## Quy trình phát hành bản test
1. `dotnet build -c Release -p:LhbAllTargets=true` (3 bản DLL), sửa hết lỗi compile + cảnh báo của cả 3 đích.
2. `powershell -ExecutionPolicy Bypass -File build.ps1` -> `Dist\LHBBlockScheduler\` (MD5 trong build-info.txt).
3. Từ v9 (yêu cầu user 29/09/2026) mọi bản thêm hậu tố Premium: tiêu đề form "Thống kê Block vN Premium" (từ v9.4 chỉ sửa
   `MyApp.Version` trong Properties/AssemblyInfo.cs),
   `Dist\LHBBlockScheduler_<yyyyMMdd>_vN_Premium.zip`, `Dist\HUONG_DAN_TEST_<yyyyMMdd>_vN_Premium.md` (vN có thể là v9.1).
   Zip = thư mục Dist\LHBBlockScheduler + HUONG_DAN_SU_DUNG.md + TINH_NANG_PREMIUM.md + (từ v9.6) hướng dẫn test của bản
   (ghi MD5 vào hướng dẫn test TRƯỚC khi nén; phiên cloud: `python3 tools/pack_cloud.py stage` rồi `zip <yyyyMMdd>`).
   (v8 trở về trước: `Dist\LHBBlockScheduler_<yyyyMMdd>_vN.zip`, `Dist\HUONG_DAN_TEST_<yyyyMMdd>_vN.md`)
   (theo mẫu các bản trước: bản này sửa gì, MD5, bảng bước test, cần gửi về gì), gửi cả 2 file cho user.
4. (Đổi 29/09/2026 theo user) KHÔNG gửi demo, KHÔNG hỏi ý giữa chừng: tự chọn phương án tốt nhất, làm xong trọn
   (build, đóng gói, tài liệu, push GitHub) rồi báo cáo 1 lần.
5. Mã nguồn ở GitHub private HienPhuong99/LHBBlockScheduler: commit + push sau mỗi bản, zip commit + push luôn trong `Dist\`
   (từ 30/09/2026, `.gitignore` không chặn `Dist/*.zip` nữa; v9 - v9.4 ở Releases). Máy dev có `gh` thì tạo thêm Release.
6. (30/09/2026 theo user: "từ lần sau cứ push code và nén luôn nha, tôi chỉ việc vô file kiểm tra thôi") Mỗi bản TỰ làm hết:
   build -> nén zip -> push code + zip -> gửi file zip cho user (phiên cloud: SendUserFile; máy dev: chép vào
   `C:\Users\vsp\Downloads\tool\`). User chỉ mở file kiểm tra: báo cáo KHÔNG giao việc GitHub cho user (tạo Release, gộp PR...).

## Trạng thái (30/09/2026): bản v9.7 Premium (chạy AutoCAD 2021 – 2027) đã phát hành, CHƯA có kết quả test (v9 – v9.6 cũng chưa)
v9.7 Premium (MD5 net48 `401475A3C654362B5D0CF9EFEE757BFA`, net8 `695E16FFB32622ED3743562E2E9E038B`, net10 `5A55D24AB9F341328203A2BC868F42E0`,
`Dist\HUONG_DAN_TEST_20260930_v9.7_Premium.md`, build cloud tại commit 6001c3f) = v9.6 + đa phiên bản AutoCAD (review D1; user 30/09
"Tiếp tục nhé bạn" -> tự chọn bước tiếp theo của lộ trình: đồng nghiệp dùng key review có thể đang chạy AutoCAD 2025+):
- csproj đa đích (xem Ngữ cảnh kỹ thuật). `[assembly: SupportedOSPlatform("windows")]` trong `#if NET` (GenerateAssemblyInfo=false
  nên SDK không tự ghi, thiếu thì CA1416 báo ~6000 cảnh báo). Build 3 đích 0 lỗi 0 cảnh báo, không chép DLL AutoCAD ra output.
- Gói: thư mục gốc = bản net48 + LHB.lsp + build-info.txt (MD5 net48 = "dấu" nhận thư mục add-in như cũ); `net8\`, `net10\` mỗi
  thư mục DLL + PDB + deps.json + build-info.txt riêng. `LHB.lsp`: `*LHB_EXPECTED_MD5_NET8*` / `_NET10*` (chữ mẫu
  `@@LHB_BUILD_MD5_NET8@@` / `_NET10@@`, build.ps1 + pack_cloud.py thay), `lhb:TargetSub` theo `(atof (getvar "ACADVER"))`
  (< 25 gốc, < 26 net8, còn lại net10), ACADVER < 24 báo không hỗ trợ, gói thiếu bản -> báo, kiểm build-info.txt thư mục con,
  `lhb:RootOf` (chọn DLL trong net8 / net10 ở hộp thoại -> thư mục cha), cảnh báo "nạp nhầm bản gốc vào AutoCAD 2025+".
- `MyApp.BuildTarget` / `RuntimeText` / `AcadVersionText`, `CheckAcadVersion` (Application.Version.Major ngoài dải của bản ->
  cảnh báo dòng lệnh), LHBVERSION / LHBDIAG in thêm. `Logger.AddinRootFolder` cho ThuVienMau, log.txt cạnh add-in, LHBDIAG,
  InstallDir registry. `UiKit.KeepFrameworkFont` (5 form không đặt font: .NET 8 đổi mặc định sang Segoe UI 9 -> đặt
  SystemFonts.DefaultFont như .NET Framework). D4: `CommandAliasManager` gọi `Autodesk.AutoCAD.Internal.Utils` qua hàm nhỏ
  `[MethodImpl(NoInlining)]`, field `_callbacks` kiểu object (thiếu API -> chỉ phím tắt lỗi, không hỏng cả lớp).
- build.ps1: có .NET 10 SDK (`dotnet --list-sdks`) -> `-p:LhbAllTargets=true` + chép net8 / net10 vào Dist, không có -> chỉ net48
  (LHB.lsp báo "gói không có bản" trên AutoCAD 2025+). `tools/pack_cloud.py` đóng gói 3 bản, bước zip kiểm đủ 3 MD5 trong hướng dẫn test.
- tests/LHBCoreTests 46 kiểm tra (+7 `AddinPaths.ResolveRoot`).
Chưa làm (cần test thật): bundle ApplicationPlugins phát hành 3 ComponentEntry (D2, cùng installer E2); DPI (D6).
Cần xác nhận khi có kết quả test v9.7: AutoCAD 2021 vẫn nạp bản gốc + mọi thứ như v9.6; AutoCAD 2025 / 2026 / 2027 nạp đúng
net8 / net10, form không tràn chữ, Ribbon (reflection AdWindows .NET 8), ảnh ký hiệu (GraphicsSystem), Excel mở, ECDSA / mã máy
chạy trên .NET 8 (log `[License]`), phím tắt (API nội bộ).
v9.6 + v9.7 làm ở phiên cloud, PR #3 từ nhánh claude/feature-review-monetization-plan-4hj52r (chưa gộp main: gộp khi user bảo).
v9.6 Premium (MD5 `5FEEE00BD988C1B1EDE8E98E84A63C89`, `Dist\HUONG_DAN_TEST_20260930_v9.6_Premium.md`, build cloud tại commit 76ccb1e) = v9.5 +
yêu cầu user 30/09 (ảnh hộp thoại block mẫu + form + bảng CAD có đầu báo tia chiếu chủng loại "Distance1=12320.3286822983"):
- Chủng loại block động CHỈ từ Visibility, không có Visibility thì tham số dạng chữ (Lookup...). Tham số SỐ (độ dài Linear /
  Polar / XY, góc, toạ độ Point, lật Flip) không bao giờ làm chủng loại (trước: ghép "Tên=Giá trị" mọi tham số -> mỗi độ dài
  1 dòng, cột Chủng loại bảng xuất rộng vì chữ dài). `BlockExtractor.ReadDynamic` trả `DynamicInfo` (Variant, HasVisibility,
  Sizes, NumericNames, LegacyNames = tên tham số số khi KHÔNG có Visibility); `ReadDynamicInfo`, `FindDynamicInfo(db, tên)`.
- "Tách theo kích thước" (form hàng 2, settings `SplitBySize`, `ExtractionOptions.SplitBySize`, TableScanInfo.SplitBySize):
  kích thước = tham số đơn vị Distance / Area, `VisibleInCurrentVisibilityState`, trừ toạ độ "... X/Y" của Point; nhiều tham
  số "1200 x 600"; làm tròn `DynamicParamText.FormatSize` (~4 chữ số có nghĩa, dấu chấm cố định). Khoá gom "||SIZE:". Cột
  `colSize` "Kích thước" (DataPropertyName Size, sửa được) hiện theo ô tick; bảng CAD / Excel (số) có cột này. Nút Căn lề
  chuyển lên hàng 1. Quét thêm (`FindRowFor`) nay khớp theo GroupKey. Nhiều bản vẽ không tách theo kích thước.
- Block mẫu cũ "Distance1=47116.93": `DynamicParamText.MatchVariant / StripNumericParts` bỏ phần tham số số khi so (có tên
  tham số số của block đang quét, chỉ khi block không có Visibility -> trạng thái Visibility dạng "K=80" không bị đụng);
  `TemplateLibraryDialog.FixLegacyVariants` tự để trống + lưu khi mở hộp thoại (block có trong bản vẽ); Thêm từ bản vẽ không thêm trùng.
- LHBCAPNHAT: TableScanInfo `Version` 3 (`TableUpdater.ScanInfoVersion`); bảng Version < 3 quét với
  `ExtractionOptions.LegacyVariant` (chủng loại kiểu cũ) để khoá dòng cũ vẫn khớp.
- Ô ký hiệu: block động (`BlockItem.IsDynamic`) luôn chép hình từ instance như block có chủng loại; tên LHB_SYM kèm kích thước.
- `LHBKHOPCOT` (`Core/TableAutoFit.cs`, không cần Premium, Ribbon / palette / LHBLENH / LHB.lsp): khớp độ rộng cột chữ của
  AutoCAD Table theo chữ (cùng công thức lúc xuất), bỏ ô gộp + cột ký hiệu. User hỏi "bảng co giãn, tự fix như Excel":
  AutoCAD Table vốn kéo grip được (chữ xuống dòng, ký hiệu AutoFit), lệnh này = double-click mép cột Excel.
- `Core/DynamicParamText.cs` hàm thuần + `tests/LHBCoreTests` (39 kiểm tra, chạy với văn hoá vi-VN).
Cần xác nhận khi có kết quả test v9.6: đầu báo tia chiếu 1 dòng chủng loại trống; tách theo kích thước đúng độ dài; block mẫu
cũ tự để trống + chỉ quét block mẫu đếm đủ mọi độ dài; `UnitsType` / `VisibleInCurrentVisibilityState` đọc được (log
`BlockExtractor: N block động có tham số số`); LHBKHOPCOT đổi rộng cột, dòng tự thấp lại.
v9.5 làm ở phiên cloud, PR #2 (gộp main 30/09 theo lời user "push và gộp lại hết") từ nhánh claude/feature-review-monetization-plan-4hj52r (PR #1 = v9.4 đã gộp main 30/09).
Zip v9.5 gửi user là bản build cloud (MD5 `D1626C1147BC58F3AC0FED232D10BC95`); nếu build lại trên máy dev bằng libs\ như v9.4
thì MD5 đổi -> sửa MD5 trong `Dist\HUONG_DAN_TEST_20260930_v9.5_Premium.md` + dòng v9.5 bên dưới.
v9.4 làm ở phiên cloud trên nhánh PR #1; 30/09/2026 user bảo gộp PR vào main + tạo Release `v9.4-premium`.
Zip phát hành build lại trên máy dev bằng libs\ -> MD5 DLL `5F5B53084349716CB7D9A6FF4054DE30` (bản build cloud MD5
`DA84C6DB97E8BFAFD4437EE1A1302C6C` không phát hành). Zip + hướng dẫn test cũng chép vào `C:\Users\vsp\Downloads\tool\` như các bản cũ.
Repo GitHub (private): HienPhuong99/LHBBlockScheduler. Zip: `Dist\` trên GitHub (từ v9.5), v9 - v9.4 ở Releases. libs\*.dll KHÔNG đưa lên repo.
Review + kế hoạch thương mại hoá (29/09/2026): `docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md` (mã lỗi A1..F6 dùng trong commit/test).
Đã build thử net48/net8.0-windows/net10.0-windows bằng NuGet `AutoCAD.NET` 24.0/25.0.1/26.0 (ExcludeAssets=runtime, không cần
libs\): 0 lỗi -> dùng được cho CI. AutoCAD 2025/2026 đã lên .NET 10 qua bản cập nhật 08-09/2026.
Build trên máy không có Windows/AutoCAD (phiên cloud): chép AcMgd/AcDbMgd/AcCoreMgd từ gói NuGet AutoCAD.NET 24.0.0
(`autocad.net`, `autocad.net.core`, `autocad.net.model`) vào 1 thư mục, `dotnet build -c Release -p:AutoCADInstallDir=<thư mục>/
-p:TargetFrameworkRootPath=<nuget>/microsoft.netframework.referenceassemblies.net48/1.0.3/build/
-p:CustomAfterMicrosoftCommonTargets=<file targets rỗng định nghĩa lại BuildLoaderAndRuntimeCopy + CleanLoader>` (bỏ bước
PowerShell / %APPDATA% của máy dev); DLL tham chiếu acmgd/acdbmgd/accoremgd 24.0.0.0 giống build bằng libs\. Đóng gói bằng
`tools/pack_cloud.py` (đúng các bước build.ps1: Dist, build-info.txt UTF-8 BOM, thay @@LHB_BUILD_MD5@@ + @@LHB_VERSION@@ trong
LHB.lsp; bước zip kiểm hướng dẫn test đã ghi đúng MD5).
MD5 DLL phụ thuộc cả commit đang đứng: SDK .NET 8 nhúng SourceLink (URL + mã commit) vào PDB -> mã PDB nằm trong DLL. Build bản
phát hành từ commit đã chốt mã, rồi commit riêng phần cập nhật MD5 trong tài liệu (v9.5: DLL build tại commit 795f1a1).
Bản quyền + cấp key (30/09/2026): `docs/BAN_QUYEN_VA_CAP_KEY.md` (thiết kế, mức an toàn, quy trình cấp / thu hồi / xoay khoá).
v9.5 Premium (MD5 `D1626C1147BC58F3AC0FED232D10BC95`, `Dist\HUONG_DAN_TEST_20260930_v9.5_Premium.md`) = v9.4 + bản quyền v2
(user 30/09: "bảo mật hơn + cấp 1 key trọn đời để share đồng nghiệp review + hệ thống gen key"):
- `LicenseManager.Enforced = true`: Premium dùng thử 30 ngày rồi cần key. Tính năng thường không cần key.
- Key `LHB2-` + Base32 (bỏ I O 0 1) nhóm 5: payload 26 byte + nhãn "cấp cho" ≤ 48 byte UTF-8 + chữ ký ECDSA P-256/SHA-256 64 byte
  trên "LHB-LICENSE-V2|"+payload (`Core/LicenseCodec.cs`, DÙNG CHUNG add-in + keygen + tests). Loại Machine / Floating / Review,
  kid (xoay khoá), serial (thu hồi), gói tính năng (bit Premium). Parse bỏ khoảng trắng, mọi dấu gạch (Pd), ký tự Cf, thử từng
  chỗ "LHB2" trong chuỗi dán, độ dài key tính từ byte độ dài nhãn (chữ dính sau key bỏ qua). Mã `LHB1.` (v9 - v9.4) báo không dùng nữa.
- `Core/LicensePolicy.cs` (dùng chung): `TrustedKeys` (kid -> base64 X|Y khoá CÔNG KHAI; kid 1 tạo 30/09 trong phiên cloud),
  `RevokedSerials`, `AcceptReviewKeys`, `Validate` (thứ tự: định dạng, kid, chữ ký, thu hồi, review, mã máy, hạn, Premium).
  Thêm kid / thu hồi = sửa file này + build bản mới (bản cũ offline vẫn nhận key cũ).
- `Core/MachineFingerprint.cs`: mã máy = 10 byte SHA-256("LHB-HW-V2|" + UUID SMBIOS loại 1 qua GetSystemFirmwareTable 'RSMB'),
  UUID rác -> MachineGuid -> tên máy + user. Hiện "XXXX-XXXX-XXXX-XXXX" + nguồn.
- `Core/TrialLogic.cs` (hàm thuần) + `LicenseManager.TrialStore`: 17 byte (ngày bắt đầu / dùng gần nhất, HMAC-SHA256 khoá theo mã máy)
  ở registry HKCU\Software\LHBBlockScheduler "P2" + %LOCALAPPDATA%\LHBBlockScheduler\p2.dat; bắt đầu sớm nhất, tự ghi lại nơi thiếu,
  sai HMAC -> hết hạn, lùi đồng hồ > 1 ngày -> tạm 0; lỗi IO -> cho 1 ngày (không khoá oan). Tên mới -> máy chạy v9 - v9.2 đủ 30 ngày.
- `UI/LicenseDialog.cs` (LHBBANQUYEN): mã máy + nguồn, chi tiết key, Xoá mã. `LHBDIAG` mục 3a. BẢN QUYỀN. Log `[License]`.
- Key review trọn đời đã cấp: serial `64F68B85`, kid 1, "Nhóm review LHB 2026" (chuỗi key KHÔNG nằm trong repo, gửi user file
  KEY_REVIEW_LHB.txt). Lộ -> `LHBKeyGen revoke 64F68B85` + thêm vào RevokedSerials, hoặc AcceptReviewKeys = false.
- `tools/LHBKeyGen` v2 (net48 exe + net8.0): menu tiếng Việt + lệnh init/protect/pubkey/issue/verify/list/revoke/export-csv/machine,
  `KeyFile.cs` (file khoá text, mật khẩu tuỳ chọn PBKDF2 600k + AES-256-CBC + HMAC), `Ledger.cs` (sổ JSONL). README trong thư mục.
  `.github/workflows/lhb-cap-key.yml`: workflow_dispatch issue/verify/revoke/list, secret `LHB_SIGNING_KEY` (+ `LHB_KEY_PASSPHRASE`),
  sổ key ở nhánh orphan `lhb-license-ledger`, input qua env (chống chèn lệnh). Cần merge vào main mới hiện ở tab Actions.
  `tests/LHBLicenseTests` (net8.0, 63 kiểm tra, `LHB_TEST_REAL_KEY` = kiểm key thật). Chạy: `dotnet run --project tests/LHBLicenseTests`.
- BÍ MẬT (KHÔNG BAO GIỜ commit, .gitignore chặn `LHB_SIGNING_KEY*`, `LHB_KEY_LEDGER*`): khoá ký `LHB_SIGNING_KEY_kid1.txt` + sổ
  `LHB_KEY_LEDGER.jsonl` đã gửi user (zip LHB_KEYS_v2_BI_MAT_20260930.zip) -> để ở `C:\Users\vsp\Downloads\tool\LHB_KEYS\`.
  Khuyên user tạo kid 2 trên máy mình trước khi bán (chỉ gửi Claude dòng khoá công khai để thêm vào TrustedKeys).
Cần xác nhận khi có kết quả test v9.5: nguồn mã máy = SMBIOS-UUID và ổn định qua khởi động lại; ECDSA (CNG) chạy trong acad.exe
(log `[License] Kiểm mã serial ...: HỢP LỆ`); dùng thử tự ghi lại khi xoá P2; kích hoạt key review.
v9.4 Premium (MD5 phát hành `5F5B53084349716CB7D9A6FF4054DE30`, `Dist\HUONG_DAN_TEST_20260929_v9.4_Premium.md`) = v9.3 + sửa P0/P1 của
review (user 29/09: "Sửa trước những lỗi đã phát hiện và tồn đọng"):
- A1-A5 `BlockExtractor` viết lại phần duyệt: `Classify` -> Table (bỏ), Xref (bỏ, hoặc vỏ trong suốt khi
  `ExtractionOptions.CountXrefBlocks` / settings `CountXrefBlocks` / ô "Đếm trong XREF"), Container (block ẩn danh *U không
  dynamic = ARRAY liên kết `AssocArray.IsAssociativeArray` hoặc *U khác: vỏ trong suốt, không tăng độ sâu), Block. MINSERT:
  `ElementOffsets` (cột X / hàng Y xoay theo Rotation, `PlaneToWorld(Normal)`), mỗi phần tử 1 ScannedRef, `IsMInsertElement`
  -> `BlockInstanceRef.IsTopLevel` = false (không xoá / thay / ghi thuộc tính riêng). Bỏ block con `!Visible`. `RefVia`
  (Array/MInsert/Xref/Anonymous) -> cột Nguồn. `ScanStats` (log + thanh trạng thái form + dòng lệnh); cảnh báo độ sâu chỉ khi
  block con bị cắt cùng tên với block đang đếm (`MissedByDepth`). Chế độ block mẫu (`ExtractionOptions.TemplateFilter`, "Chỉ
  quét block mẫu"): đi sâu không giới hạn, chỉ ghi nhận block mẫu, block mẫu không bị loại vì có block con, chỉ đi vào block
  mẫu khi CountParentBlocks. `TemplateSelectionFilter(db, lib, countXrefs)` cùng quy tắc. `TemplateLibraryManager.Match` thử
  lại bỏ tiền tố `XREF|` (`StripXrefPrefix`). Chọn đối tượng lọc DXF INSERT. `ExtractionOptions.FromSettings`: LHBSCAN, lệnh
  Premium, nhiều bản vẽ dùng tuỳ chọn form đã lưu (form lưu ScanDepth / CountParentBlocks / SplitBy* / CountXrefBlocks).
- B1 `UI/DocumentBinding.cs`: form + hộp thoại modeless ẩn khi đổi bản vẽ, hiện lại khi quay về, tự đóng ở
  DocumentToBeDestroyed (static `_closingDocs` để form con đóng theo form cha không hỏi); TemplateLibraryDialog tự lưu
  (`SaveOnDocumentClose`). Form: `EnsureDocActive()` trước thao tác bản vẽ. B2: bỏ `LastSelectedObjectIds`, form giữ
  `_selectedIds`; `ExtractFromSelection(doc, options, out selectedIds)`, `ExtractAdditionalSelection(doc, options, known, out
  added, out already)`. B3 `Core/FileHelper.cs` (WriteAllTextAtomic: file tạm + File.Replace giữ .bak; ReadWithBackup).
- A6 `TableUpdater`: `CheckStructure` (TableRowCount/TableColumnCount, bảng cũ tính từ FirstDataRow + Rows + dòng tổng) -> bảng
  bị thêm / xoá dòng, cột bằng tay thì không cập nhật; `HandseedAtScan` (hex) -> chỉ thêm block mới (handle >= Handseed) trong
  khung, `RootsTruncated` (> 20000 gốc) thì như cũ; `TableRowKeys.Label` chỉ để log dòng sửa tay; tìm bảng mọi Layout.
- C1 `ThumbnailGenerator`: file `<tên>[_<chủng loại>]__<chữ ký SHA-256 12 hex nội dung BTR>.png` (`ComputeBtrSignature`: loại,
  layer, màu, ẩn/hiện, Bounds, chữ; không dùng tên BTR *U), dọn ảnh > 90 ngày. C2 `TableExporter.LocalizeImage / SetImageSource
  / RelinkImagesToDrawing`: chép ảnh vào `<DWG>_LHBImages`, SourceFileName tương đối + ActiveFileName tuyệt đối, bản vẽ chưa lưu
  (`SavedDrawingPath`: IsNamedDrawing, không .dwt) nhắc 1 lần; block của XREF dùng ảnh ký hiệu thay vì tham chiếu định nghĩa
  phụ thuộc XREF. C3: bảng vào `db.CurrentSpaceId`, đường dẫn block trùng chỉ vẽ khi bảng ở Model,
  `UiKit.ScaleForCurrentSpace` hỏi tỉ lệ 1 khi ở Layout.
- D3 ExcelExporter.Open UseShellExecute. D5 `Core/HashHelper.cs` (MD5 hệ thống, bị FIPS chặn thì tự tính MD5 RFC 1321 - đã
  kiểm khớp 300 mẫu; ShortHash giữ đúng tên LHB_SYM_ cũ). E5: `MyApp.Version` = 1 nguồn (AssemblyVersion/FileVersion/
  InformationalVersion "v9.4 Premium", tiêu đề form, dòng lệnh, LHBDIAG); build.ps1 ghi ProductVersion vào `@@LHB_VERSION@@`
  của LHB.lsp. E1: PackageContents ghi rõ bundle máy dev, SeriesMax R24.3; HUONG_DAN_SU_DUNG phần cài đặt viết lại (kéo thả
  LHB.lsp / Startup Suite, AutoCAD 2021-2024). BlockReplacer HashSet. ScheduleManager: `ZoomAndHighlightItem` theo Corners WCS,
  highlight theo đường dẫn từng block, bỏ ObjectId bản vẽ khác.
Cần xác nhận khi có kết quả test v9.4: ARRAY/MINSERT/XREF đếm đúng (log `BlockExtractor: quét [...]`); form ẩn/hiện/đóng theo
bản vẽ không lỗi; ảnh tương đối `.\<DWG>_LHBImages\` nạp được (log `[TableExporter] Ảnh ...`); bảng trên Layout +
LHBCAPNHAT; `AssocArray.IsAssociativeArray` chạy (không thì ARRAY vẫn đếm nhưng ghi "ẩn danh").
v9.3 Premium (MD5 `D3E5A61DB704B975DB5D97ADD35CABC6`, `Dist\HUONG_DAN_TEST_20260929_v9.3_Premium.md`) = v9.2 + CHƯA BẮT
BẢN QUYỀN (user 29/09/2026: "để xài free, khi nào nói bắt bản quyền thì hãy tính"): `LicenseManager.Enforced = false`.
(Đã bật lại ở v9.5 cùng bản quyền v2: key dùng chung giờ làm được an toàn vì add-in chỉ giữ khoá công khai; dùng thử đổi
sang giá trị registry "P2" nên máy test v9 - v9.2 vẫn đủ 30 ngày.)
v9.2 Premium (MD5 `A841B41AFBE6346399F2478603D31F4C`, `Dist\HUONG_DAN_TEST_20260929_v9.2_Premium.md`) = v9.1 + sửa ô
Ký hiệu không đều (ảnh test v8: EXIT "CHỈ LỐI THOÁT NẠN" tỉ lệ ~2.4:1 phóng gần kín ô rộng ~2.3 x chiều cao dòng).
Kết luận từ ảnh: lề ô "vuông" của v7 (`cell.Borders.X.Margin`) KHÔNG giới hạn AutoFit -> AutoFit co khung bao block vào
cả ô. Sửa: `TableExporterAcad.EnsureSquareFrame` thêm 2 DBPoint (-0.5,-0.5) / (0.5,0.5) trên layer `LHB_KY_HIEU_KHUNG`
(tắt, không in) vào mọi block LHB_SYM / LHB_IMG đã chuẩn hoá -> khung bao vuông 1x1. `ProbeExtents` đo lại
GeometricExtents: không vuông (CAD bỏ qua layer tắt) thì chuyển điểm sang `LHB_KY_HIEU_KHUNG_HIEN` (bật, không in, màu 250).
SetCellBlock bỏ lề không đều, chỉ còn lề đều 7.5% cạnh ngắn. Cần xác nhận: ký hiệu đều; log `[TableExporterAcad.SquareFrame]`.
v9.1 Premium (MD5 `581F5A95D57EE036FED3F3DC01CF00DD`, `Dist\HUONG_DAN_TEST_20260929_v9.1_Premium.md`): tối ưu + bỏ phần thừa,
user hỏi có cần thư viện GitHub không -> KHÔNG thêm NuGet/DLL ngoài vào add-in (chạy trong acad.exe, dễ xung đột DLL
như System.Text.Json; JSON/Excel/Ribbon đã tự viết bằng thư viện .NET có sẵn). Thay đổi:
- Tốc độ: `TableExporterAcad.SuppressRegen` (Table.SuppressRegenerateTable) khi điền bảng / `TableUpdater` / `ExportGrid`;
  block ký hiệu dựng 1 lần / lần xuất (`ResolveSymbol` cache theo symName). `BlockExtractor`: `ScanCache` (khung bao +
  danh sách block con theo BTR), lọc `ObjectId.ObjectClass` (`BlockExtractor.BlockRefClass`) trước khi mở,
  `ReadDynamic` đọc DynamicBlockReferencePropertyCollection 1 lần (ReadVisibility dùng chung). `ThumbnailGenerator`:
  nhớ ShapeHash theo file (`ForgetHash` khi render lại), LockBits thay GetPixel. `Logger`: giữ StreamWriter mở,
  AutoFlush, đóng sau 1 giây rảnh (Timer), log > 5 MB -> log.old.txt; đọc log đang giữ phải dùng `Logger.ReadTail`
  (File.ReadAllLines báo IOException). `DuplicateFinder.Detect` chỉ log chi tiết 30 nhóm; `BlockInstanceRef.Group`.
  `LicenseManager.Current` nhớ kết quả theo mã + ngày.
- Form: ảnh ký hiệu qua CellFormatting + `_thumbCache` (không còn RefreshThumbnailImages), DoubleBuffered
  (`UiKit.DoubleBuffer`), tooltip qua CellToolTipTextNeeded, `ReplaceAllItems` tắt RaiseListChangedEvents rồi reset 1 lần,
  tìm kiếm trễ 250 ms + `_grid.CurrentCell = null` trước khi ẩn dòng (lỗi v9: InvalidOperationException "Row associated
  with the currency manager's position cannot be made invisible", đã tái hiện bằng chương trình thử WinForms), lọc lại
  sau mỗi ListChanged Reset. Toolbar còn 2 hàng. `UiKit.PromptText` dùng chung.
- Bỏ thư viện thiết bị cũ (DeviceLibraryManager, nút Quy hoạch / Thêm vào TV / Chỉ đếm block có trong TV, settings
  OnlyLibraryBlocks / CurrentLibraryName). `TemplateLibraryManager.MigrateDeviceLibraries` chuyển 1 lần
  `%APPDATA%\...\Libraries\*.json` thành bộ mẫu "TV cu <tên>" theo KnownBlockNames (settings DeviceLibrariesMigrated).
  `LHBLEGEND` lưu vào bộ block mẫu (khoá tên block gốc + chủng loại) + WblockClone định nghĩa vào <bộ>.dwg.
  Không làm khớp theo ShapeHash cho block mẫu (dễ đặt nhầm tên thiết bị giống hình) -> chỉ khớp tên.
  Bỏ lệnh LHBSCANTEST, LHBTHUMBTEST, hàm chết ScheduleManager.MergeItems/MoveUp/..., Logger.ClearLog, TableTemplate.Clone.
Cần xác nhận khi có kết quả test v9.1: thời gian quét / xuất bảng trong log so với v9; tìm kiếm không lỗi; bộ "TV cu ...".
v9 Premium (MD5 `ACB0AF84E5D92D05678F24073D95D6BD`, `Dist\HUONG_DAN_TEST_20260929_v9_Premium.md`, tính năng: `TINH_NANG_PREMIUM.md`).
User giao tự làm hết 13 tính năng, không hỏi, không demo. Thiết kế:
- Cột Premium trên form = cột unbound tên "zone:<khu>" / "attr:<khoá>", giá trị trong `BlockItem.ExtraValues`,
  `TableExporter.GetItemTextForColumn` đọc được -> bảng CAD / Excel tự có. Tính lại trong
  `BlockScheduleForm.RefreshPremiumColumns` (gọi cuối `RecomputeDuplicates`). Code Premium của form ở
  `UI/BlockScheduleForm.Premium.cs` (partial).
- P1 khu vực: `Core/ZoneManager.cs`, lưu NOD "LHB_PREMIUM"/"ZONES" (JSON tên + handle đường bao). Block thừa do trùng
  không tính (`BlockInstanceRef.IsExcludedDuplicate`).
- P2 tự cập nhật: `Core/TableUpdater.cs`, JSON `TableScanInfo` trong Extension Dictionary của Table (key LHB_SCAN):
  tuỳ chọn quét, handle gốc, khung vùng quét, khoá dòng = `BlockInstanceRef.GroupKey` (khoá gom dòng lúc quét).
- P3 `Core/MultiDrawingCounter.cs` (ReadDwgFile + đổi WorkingDatabase tạm), `BlockExtractor.ExtractFromDatabase`
  (doc = null -> không tạo ảnh). P4 `Core/LengthCounter.cs`. P5 `Core/PremiumColumns.cs` + `ExtractionOptions.SplitAttributeKeys`,
  thuộc tính đọc lúc quét (`ScannedRef.Attributes`, khoá "A:TAG" / "D:tham số").
- P6 `Core/CountChecker.cs` (dò block bị explode theo chữ ký hình: line/circle/arc/polyline, 4 góc xoay, khớp ≥70%).
  P7 `Core/DeviceNumbering.cs` (layer LHB_DANHSO). P8 `Core/CoverageDrawer.cs` (layer LHB_VUNGBAOVE, không in).
  P12 `Core/BlockReplacer.cs`.
- P9 `Core/XlsxWriter.cs` tự ghi OpenXML (System.IO.Compression), đã mở thử bằng Excel 16 trên máy dev OK.
  `Core/ExcelExporter.cs`. P10 `Core/TableTemplate.cs` (settings TableTemplates; tên trùng
  Autodesk.AutoCAD.DatabaseServices.TableTemplate -> file UI dùng alias). Bảng lưới chung: `TableExporterAcad.ExportGrid`.
- P11 `UI/RibbonBuilder.cs` gọi AdWindows qua reflection (không có AdWindows.dll trong libs), chờ Application.Idle,
  tạo lại khi đổi WSCURRENT; `UI/LhbPalette.cs` (PaletteSet). P13 `Core/LicenseManager.cs`: (v9 - v9.4) RSA-1024, mã máy =
  SHA256(MachineGuid), mã LHB1 - THAY bằng bản quyền v2 từ v9.5 (xem mục v9.5). Khoá bí mật + exe keygen ở
  `C:\Users\vsp\Downloads\tool\LHB_KEYS\` (NGOÀI repo, không bao giờ commit).
- Lệnh mới: LHBKHUVUC, LHBCAPNHAT, LHBNHIEUBV, LHBCHIEUDAI, LHBSOATLOI, LHBDANHSO, LHBVUNGBV, LHBTHAYBLOCK,
  LHBMAUBANG, LHBPALETTE, LHBRIBBON, LHBBANQUYEN (lệnh chạy riêng quét bằng `ExtractFromSelection(remember:false)`
  để không đè vùng chọn của form).
Cần xác nhận khi có kết quả test: Ribbon hiện được qua reflection; Table.InsertRows cuối bảng; PaletteSet; Excel mở
trên máy user; dò explode không báo nhầm.
v8 (MD5 `776431E25DD53DE19E889ACA8FE33529`, `Dist\HUONG_DAN_TEST_20260929_v8.md`), ảnh user: hộp thoại block mẫu v7 chạy OK:
- `TemplateLibraryDialog`: MultiSelect, phím Delete / nút "Xoá dòng chọn" xoá nhiều dòng (hỏi xác nhận, chưa ghi
  file tới khi Lưu), nút "Xoá bộ..." (`TemplateLibraryManager.DeleteSet` xoá cả bản dự phòng APPDATA).
  EditMode đổi EditOnEnter -> EditOnKeystrokeOrF2 (double-click sửa) vì ô đang sửa nuốt Delete / Shift+click.
- Bảng lệnh `LHBLENH` (`UI/CommandListDialog.cs`, `Core/CommandAliasManager.cs`): phím tắt = lệnh thật đăng ký bằng
  `Autodesk.AutoCAD.Internal.Utils.AddCommand` (nhóm LHB_PHIMTAT) lúc NETLOAD (`MyApp.Initialize`), lưu
  settings `CommandAliases` (null = mặc định LHB/TKB/BLM). Từ chối tên trùng lệnh có sẵn (`Utils.IsCommandNameInUse`),
  trùng acad.pgp thì hỏi. LHBDIAG mục 3b in trạng thái phím tắt.
- Cột Block Name (`colBlockName`) cho ẩn (chỉ còn colCount khoá); ẩn thì bảng xuất bỏ cột. User muốn sửa thẳng
  vào v8 (giữ tên file v8, MD5 mới) thay vì tạo v9.
v7 (MD5 `37C206C675B525C0F1E5F42922C10D8A`, `Dist\HUONG_DAN_TEST_20260929_v7.md`), test v6 xác nhận EXIT nét liền OK:
- Ký hiệu đều cỡ: `TableExporterAcad.SetCellBlock` đặt lề để phần trong lề là ô VUÔNG (cạnh = chiều cao dòng
  trừ lề) -> AutoFit cho cạnh dài mọi ký hiệu bằng nhau. Bảng Line+Text: cạnh dài = 80% cạnh ngắn ô.
- Nút "Quét thêm" (form hàng 3): `BlockExtractor.ExtractAdditionalSelection` chỉ trích đối tượng chưa có trong
  `LastSelectedObjectIds`, form cộng vào dòng có cùng Instance.Key (giữ chỉnh sửa), dòng mới thêm cuối.
- Thư viện block mẫu: `Core/TemplateLibraryManager.cs`, `Models/TemplateLibrary.cs`, `UI/TemplateLibraryDialog.cs`,
  lệnh `LHBMAU`. Lưu `<thư mục DLL>\ThuVienMau\<bộ>.json + .dwg` (WblockClone định nghĩa block), dự phòng
  `%APPDATA%\...\ThuVienMau` (tự chép sang thư mục add-in mới nếu trống). Khớp theo tên block + chủng loại.
  "Chỉ quét block mẫu" (settings `ScanAllBlocks`=false mặc định) = `TemplateSelectionFilter` gắn vào
  `Editor.SelectionAdded` (giữ cả block cha chứa block mẫu), sau đó `TemplateLibraryManager.Apply` lọc + đặt tên.
  Quyết định 29/09 (user giao tự chọn): giữ khớp tên + chủng loại như tool trong video (mẫu không ghi chủng
  loại = khớp mọi chủng loại). Thư viện thiết bị cũ (Quy hoạch / Thêm vào TV, khớp theo ShapeHash) giữ riêng
  tới khi test v7 OK, rồi mới gộp ở v8: bỏ nút cũ, nhập 1 lần `%APPDATA%\...\Libraries\*.json` vào bộ mẫu,
  TemplateEntry thêm ShapeHash làm đường khớp dự phòng cho block copy đổi tên. Gộp đổi UI -> demo trước.
v6 = v5 + sửa LISP: `(vl-catch-all-apply 'lhb-dllinfo nil)` khi hàm chưa định nghĩa báo "bad function"
NGOÀI vùng bắt lỗi -> luôn kiểm `(type lhb-dllinfo)` trước. DLL giữ nguyên MD5 v5.
v5 (29/09, MD5 `A98634FE77862EC32437EA78B05D1E3C`, `Dist\HUONG_DAN_TEST_20260929_v6.md`):
- `LHB.lsp` mang MD5 DLL (build.ps1 thay `@@LHB_BUILD_MD5@@` khi đóng gói), chỉ NETLOAD thư mục có
  build-info.txt cùng MD5; bỏ qua registry InstallDir trỏ bản cũ; quét Downloads/Desktop/Documents sâu 3 cấp.
  Hàm LISP `lhb-dllinfo` (Commands.cs) trả (đường dẫn, MD5) bản đang chạy để LISP báo "đang chạy bản cũ".
- Độ rộng cột bảng xuất theo cột grid (`ColumnExportDef.GridWidthPx`, 1 dòng grid 48px = 1 dòng bảng).
- Ký hiệu dynamic block: chép entity đang hiện trong BTR ẩn danh của instance (không copy+explode instance nữa),
  nhân tỉ lệ vào LinetypeScale khi explode/chuẩn hoá (lỗi mũi tên EXIT nét đứt + xoay 90°).
- Trùng = cùng TÊN block (bỏ qua chủng loại) + (cùng điểm chèn HOẶC khung bao xoay theo block che lấp
  ≥ `DuplicateOverlapPercent`% block nhỏ). `DuplicateGroup` dùng chung nhiều dòng, `Keep` giữ lại, `ExtraFor(item)`.
Chờ duyệt demo mục 4: "thư viện block mẫu" mang theo thư mục add-in, chỉ quét chọn block mẫu (xem video
`C:\Users\vsp\Downloads\Hình ảnh nè\Autocad-net-LHBScheduleBlock-...mp4`, hộp thoại "Thông tin Block mẫu").
v3 (28/09) và v4 (29/09, MD5 `5484C5A279400B0FBC78A885A2F981C6`) gồm:
- Ô ký hiệu trong AutoCAD Table dùng AutoFit (co giãn theo ô). Block ký hiệu `LHB_SYM_*` của dynamic block
  hoặc block lỗi extents được phẳng hoá (explode, giữ màu ByBlock/layer 0) rồi chuẩn hoá cạnh dài = 1.
- Cột "Ảnh" đổi thành "Ký hiệu". Grid chọn theo ô (RowHeaderSelect). Nút căn lề Trái/Giữa/Phải lưu vào
  `BlockItem.CellAlignments`, cả 2 kiểu xuất bảng dùng.
- Block trùng vị trí (`Core/DuplicateFinder.cs`, `UI/DuplicateDialog.cs`): trùng = cùng tên + chủng loại +
  điểm chèn cách ≤ sai số (settings `DuplicateTolerance`). Cột "Trùng", ô "Không đếm trùng" (mặc định bật,
  settings `CountDuplicateBlocks`=false). Xuất bảng vẽ đường dẫn từ dòng bảng tới chỗ trùng trên layer
  `LHB_BLOCK_TRUNG` (không in). Lệnh `LHBDUPCLEAR`. Đổi SL luôn qua `BlockScheduleForm.RecomputeDuplicates`.
Cần xác nhận khi có kết quả test: AutoFit ký hiệu đều cỡ và co giãn đúng; màu EXIT sau phẳng hoá;
Ctrl+Z sau "Xoá bản thừa"; highlight block lồng qua FullSubentityPath.

## Quy tắc làm việc
- Vai trò: sửa lỗi / thêm tính năng theo yêu cầu, KHÔNG viết lại kiến trúc trừ khi user yêu cầu rõ.
- Sau mỗi thay đổi code, tự chạy `dotnet build` và sửa hết lỗi compile trước khi báo cáo.
- Báo cáo cuối mỗi lần sửa: (1) lỗi gì + nguyên nhân gốc, (2) sửa file nào, sửa gì, (3) kết quả build,
  (4) hướng dẫn test chính xác trong AutoCAD.
- Comment code tiếng Việt, tên biến/hàm tiếng Anh.
- (29/09/2026 theo user) KHÔNG đặt lịch tự kiểm tra (send_later / trigger) và KHÔNG theo dõi PR tự động
  (subscribe_pr_activity) vì tốn token: tạo PR xong thì báo cáo rồi dừng, user tự nhắn khi cần.

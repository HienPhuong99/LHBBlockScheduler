# Dự án: LHBBlockScheduler — AutoCAD Add-in thống kê Block

## Ngữ cảnh kỹ thuật (BẮT BUỘC tuân thủ)
- C#, SDK-style csproj, net48, x64. AutoCAD .NET API (AcMgd, AcDbMgd, AcCoreMgd trong `libs\`), AutoCAD 2021+.
- 3 DLL AutoCAD LUÔN `<Private>false</Private>`. Không bao giờ đổi thành true.
- UI: WinForms viết bằng code (không Designer.cs).
- JSON: `Core/JsonHelper.cs` (DataContractJsonSerializer). KHÔNG dùng System.Text.Json (crash trong acad.exe).
  DataContractJsonSerializer không chạy property initializer -> field bool mới đặt tên sao cho mặc định = false.
- Dữ liệu: `%APPDATA%\LHBBlockScheduler\` (settings.json, Libraries\, Thumbs\, log.txt).
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
1. `dotnet build -c Release`, sửa hết lỗi compile.
2. `powershell -ExecutionPolicy Bypass -File build.ps1` -> `Dist\LHBBlockScheduler\` (MD5 trong build-info.txt).
3. Từ v9 (yêu cầu user 29/09/2026) mọi bản thêm hậu tố Premium: tiêu đề form "Thống kê Block vN Premium",
   `Dist\LHBBlockScheduler_<yyyyMMdd>_vN_Premium.zip`, `Dist\HUONG_DAN_TEST_<yyyyMMdd>_vN_Premium.md` (vN có thể là v9.1).
   Zip = thư mục Dist\LHBBlockScheduler + HUONG_DAN_SU_DUNG.md + TINH_NANG_PREMIUM.md.
   (v8 trở về trước: `Dist\LHBBlockScheduler_<yyyyMMdd>_vN.zip`, `Dist\HUONG_DAN_TEST_<yyyyMMdd>_vN.md`)
   (theo mẫu các bản trước: bản này sửa gì, MD5, bảng bước test, cần gửi về gì), gửi cả 2 file cho user.
4. (Đổi 29/09/2026 theo user) KHÔNG gửi demo, KHÔNG hỏi ý giữa chừng: tự chọn phương án tốt nhất, làm xong trọn
   (build, đóng gói, tài liệu, đẩy GitHub + Release) rồi báo cáo 1 lần.
5. Mã nguồn ở GitHub private HienPhuong99/LHBBlockScheduler: commit + push sau mỗi bản, zip đính kèm Releases.

## Trạng thái (29/09/2026): bản v9.3 Premium đã phát hành, CHƯA có kết quả test (v9, v9.1, v9.2 cũng chưa)
Repo GitHub (private): HienPhuong99/LHBBlockScheduler. Zip đính kèm ở Releases. libs\*.dll KHÔNG đưa lên repo.
Review + kế hoạch thương mại hoá (29/09/2026, không sửa mã): `docs/REVIEW_VA_KE_HOACH_THUONG_MAI_HOA.md`. Bản kế tiếp nên là
v9.4 "ổn định": sửa nhóm P0/P1 mục 3 (ARRAY/MINSERT/XREF không đếm đúng, ảnh ký hiệu cache theo tên block, form modeless khi
đóng/đổi bản vẽ, LastSelectedObjectIds static, bảng chỉ vào Model Space, ảnh tuỳ chỉnh trỏ %APPDATA%, ghi settings không an
toàn, Process.Start trên .NET 8). Đã build thử net48/net8.0-windows/net10.0-windows bằng NuGet `AutoCAD.NET` 24.0/25.0.1/26.0
(ExcludeAssets=runtime, không cần libs\): 0 lỗi -> dùng được cho CI. AutoCAD 2025/2026 đã lên .NET 10 qua bản cập nhật 08-09/2026.
v9.3 Premium (MD5 `D3E5A61DB704B975DB5D97ADD35CABC6`, `Dist\HUONG_DAN_TEST_20260929_v9.3_Premium.md`) = v9.2 + CHƯA BẮT
BẢN QUYỀN (user 29/09/2026: "để xài free, khi nào nói bắt bản quyền thì hãy tính"): `LicenseManager.Enforced = false`
-> IsLicensed luôn true, TrialDaysLeft không đụng registry, StatusText "Premium miễn phí...". Không làm key chung (mã gắn
mã máy, key chung = lộ khoá bí mật). KHI USER BẢO BẮT BẢN QUYỀN: đặt Enforced = true, build bản mới; cân nhắc đổi tên giá
trị registry PremiumTrialStart (máy test v9-v9.2 đã ghi ngày dùng thử từ 29/09/2026) để 30 ngày tính từ lúc bật.
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
  tạo lại khi đổi WSCURRENT; `UI/LhbPalette.cs` (PaletteSet). P13 `Core/LicenseManager.cs`: RSA-1024 SHA256, mã máy =
  SHA256(MachineGuid) 10 byte, dùng thử 30 ngày (registry PremiumTrialStart). Keygen `tools/LHBKeyGen`, khoá bí mật +
  exe ở `C:\Users\vsp\Downloads\tool\LHB_KEYS\` (NGOÀI repo, không bao giờ commit). Đã test tạo / kiểm mã ngoài CAD.
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

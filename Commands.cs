using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using LHBBlockScheduler.Core;
using LHBBlockScheduler.UI;
using Exception = System.Exception;

namespace LHBBlockScheduler
{
    public class Commands
    {
        /// <summary>Lệnh chính: quét chọn Block, mở Form thống kê đầy đủ tính năng.</summary>
        [CommandMethod("LHBSCAN")]
        public void ScanAndShowForm()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                // Bộ block mẫu đang dùng: có block mẫu và đang bật "Chỉ quét block mẫu" -> lúc quét chỉ dính block mẫu
                var template = TemplateLibraryManager.Load(SettingsManager.Current.CurrentTemplateSet);
                bool onlyTemplate = TemplateLibraryManager.IsFilterActive(template);
                Logger.Log($"LHBSCAN: bộ block mẫu '{template.Name}' ({template.Entries.Count} block), chỉ quét block mẫu={onlyTemplate}");

                // v9.4: tuỳ chọn quét theo settings (form lưu lại khi đổi) - trước đây LHBSCAN luôn dùng mặc định
                var options = ExtractionOptions.FromSettings(SettingsManager.Current, onlyTemplate ? template : null);
                var items = BlockExtractor.ExtractFromSelection(doc, options, out var selectedIds);
                if (items == null) return;

                // Tên thống kê / đơn vị / thứ tự theo block mẫu
                items = TemplateLibraryManager.Apply(items, template, onlyTemplate);
                WriteScanStats(ed, options.Stats);
                if (items.Count == 0)
                {
                    ed.WriteMessage(onlyTemplate
                        ? $"\nKhông có block mẫu (bộ '{template.Name}') trong vùng đã chọn. Gõ LHBMAU để xem / thêm block mẫu."
                        : "\nKhông tìm thấy Block hợp lệ trong vùng đã chọn.");
                    return;
                }

                var form = new BlockScheduleForm(items, doc, selectedIds, options.Stats);
                Application.ShowModelessDialog(form);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBSCAN");
                ed.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        /// <summary>Mở hộp thoại "Thông tin block mẫu": xem / thêm / sắp xếp / chèn block mẫu.</summary>
        [CommandMethod("LHBMAU")]
        public void OpenTemplateLibrary()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                var dlg = new TemplateLibraryDialog(doc, SettingsManager.Current.CurrentTemplateSet);
                Application.ShowModelessDialog(dlg);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBMAU");
                doc.Editor.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        // ============================== PREMIUM (v9) ==============================

        /// <summary>In số liệu lần quét (ARRAY / MINSERT / XREF / cảnh báo độ sâu) ra dòng lệnh - v9.4.</summary>
        internal static void WriteScanStats(Editor ed, ScanStats stats)
        {
            if (ed == null || stats == null) return;
            ed.WriteMessage($"\n[LHB] Quét: {stats.Summary()}.");
            string warn = stats.DepthWarning();
            if (warn != null) ed.WriteMessage($"\n[LHB] Lưu ý: {warn}");
        }

        /// <summary>Quét chọn block cho lệnh Premium chạy riêng (không mở form): tuỳ chọn quét + block mẫu + trừ trùng theo settings.</summary>
        private static List<Models.BlockItem> ScanForPremium(Document doc)
        {
            var s = SettingsManager.Current;
            var template = TemplateLibraryManager.Load(s.CurrentTemplateSet);
            bool only = TemplateLibraryManager.IsFilterActive(template);
            var opts = ExtractionOptions.FromSettings(s, only ? template : null);
            // Lệnh chạy riêng không đụng vùng chọn của form thống kê đang mở (mỗi form giữ vùng chọn riêng từ v9.4)
            var items = BlockExtractor.ExtractFromSelection(doc, opts, out _);
            if (items == null) return null;
            items = TemplateLibraryManager.Apply(items, template, only);
            WriteScanStats(doc.Editor, opts.Stats);
            DuplicateFinder.Detect(items, s.DuplicateTolerance, s.DuplicateOverlapPercent);
            DuplicateFinder.SetExclusion(items, !s.CountDuplicateBlocks);
            if (items.Count == 0) doc.Editor.WriteMessage("\n[LHB] Không có block trong vùng chọn.");
            return items.Count == 0 ? null : items;
        }

        private static void RunPremium(string feature, Action<Document> action)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                if (!LicenseManager.EnsurePremium(feature)) return;
                Logger.Log($"[Premium] Lệnh: {feature}");
                action(doc);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Premium] " + feature);
                doc.Editor.WriteMessage($"\nLỗi {feature} - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        [CommandMethod("LHBKHUVUC")]
        public void Premium_Zones() => RunPremium("Tầng / khu vực", doc => Application.ShowModelessDialog(new ZoneDialog(doc)));

        [CommandMethod("LHBCAPNHAT")]
        public void Premium_UpdateTables() => RunPremium("Cập nhật bảng", TableUpdater.RunCommand);

        /// <summary>v9.6: khớp độ rộng cột bảng AutoCAD Table theo chữ trong cột (như double-click mép cột trong Excel).</summary>
        [CommandMethod("LHBKHOPCOT")]
        public void FitTableColumns()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                TableAutoFit.RunCommand(doc);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBKHOPCOT");
                doc.Editor.WriteMessage($"\nLỗi khớp cột bảng - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        [CommandMethod("LHBNHIEUBV")]
        public void Premium_MultiDrawing() => RunPremium("Nhiều bản vẽ", doc => Application.ShowModelessDialog(new MultiDrawingDialog(doc)));

        [CommandMethod("LHBCHIEUDAI")]
        public void Premium_Length() => RunPremium("Chiều dài ống / dây", doc => Application.ShowModelessDialog(new LengthDialog(doc)));

        [CommandMethod("LHBSOATLOI")]
        public void Premium_Check() => RunPremium("Soát lỗi đếm", doc =>
        {
            var items = ScanForPremium(doc);
            if (items != null) Application.ShowModelessDialog(new CheckDialog(doc, () => items));
        });

        [CommandMethod("LHBDANHSO")]
        public void Premium_Numbering() => RunPremium("Đánh số thiết bị", doc =>
        {
            var items = ScanForPremium(doc);
            if (items != null) using (var d = new NumberingDialog(doc, items)) Application.ShowModalDialog(d);
        });

        [CommandMethod("LHBVUNGBV")]
        public void Premium_Coverage() => RunPremium("Vùng bảo vệ PCCC", doc =>
        {
            var items = ScanForPremium(doc);
            if (items != null) using (var d = new CoverageDialog(doc, items)) Application.ShowModalDialog(d);
        });

        [CommandMethod("LHBTHAYBLOCK")]
        public void Premium_Replace() => RunPremium("Thay block", doc =>
        {
            var items = ScanForPremium(doc);
            if (items != null) using (var d = new ReplaceBlockDialog(doc, items)) Application.ShowModalDialog(d);
        });

        [CommandMethod("LHBMAUBANG")]
        public void Premium_TableTemplate() => RunPremium("Mẫu bảng xuất", doc =>
        {
            using (var d = new TableTemplateDialog()) Application.ShowModalDialog(d);
        });

        [CommandMethod("LHBBANQUYEN")]
        public void License()
        {
            try
            {
                using (var d = new LicenseDialog()) Application.ShowModalDialog(d);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBBANQUYEN");
            }
        }

        [CommandMethod("LHBPALETTE")]
        public void Palette()
        {
            try
            {
                LhbPalette.Toggle();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBPALETTE");
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        /// <summary>Bật / tắt tab Ribbon LHB Premium (nhớ trong settings).</summary>
        [CommandMethod("LHBRIBBON")]
        public void ToggleRibbon()
        {
            var s = SettingsManager.Current;
            s.DisableRibbon = !s.DisableRibbon;
            SettingsManager.SaveSettings();
            try
            {
                if (s.DisableRibbon) RibbonBuilder.Remove();
                else RibbonBuilder.WaitForRibbon();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBRIBBON");
            }
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(s.DisableRibbon
                ? "\n[LHB] Đã tắt tab Ribbon LHB Premium. Gõ LHBRIBBON để bật lại.\n"
                : "\n[LHB] Đã bật tab Ribbon LHB Premium.\n");
        }

        /// <summary>Bảng danh sách lệnh của add-in: xem chức năng, chạy lệnh, đổi phím tắt (mặc định gõ LHB).</summary>
        [CommandMethod("LHBLENH")]
        public void OpenCommandList()
        {
            try
            {
                CommandListDialog.ShowSingle();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBLENH");
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        /// <summary>Quét bảng Legend (Table object) có sẵn trên bản vẽ vào bộ block mẫu.</summary>
        [CommandMethod("LHBLEGEND")]
        public void ScanLegendTable()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                Logger.Log("[LHBLEGEND] Bắt đầu lệnh LHBLEGEND. Yêu cầu người dùng chọn bảng Table...");
                var peo = new PromptEntityOptions("\nChọn bảng Legend (AutoCAD Table): ");
                peo.SetRejectMessage("\nĐối tượng chọn phải là một AutoCAD Table.");
                peo.AddAllowedClass(typeof(Table), true);
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    Logger.Log($"[LHBLEGEND] Người dùng huỷ chọn hoặc chọn không hợp lệ: status={per.Status}");
                    return;
                }

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    string entType = ent.GetType().Name;
                    bool isTable = ent is Table;
                    Logger.Log($"[LHBLEGEND] Đã pick object: ObjectId={per.ObjectId}, Handle={per.ObjectId.Handle}, Kiểu='{entType}', isTable={isTable}");

                    if (isTable)
                    {
                        var tb = (Table)ent;
                        Logger.Log($"[LHBLEGEND] Đã đọc bảng Table thành công: số hàng={tb.Rows.Count}, số cột={tb.Columns.Count}");
                    }
                    tr.Commit();
                }

                using (var dlg = new LegendTableMapDialog(doc, per.ObjectId))
                {
                    Application.ShowModalDialog(dlg);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBLEGEND");
                ed.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        /// <summary>Xoá toàn bộ cache thumbnail để render lại từ đầu.</summary>
        [CommandMethod("LHBCLEARCACHE")]
        public void ClearCacheCommand()
        {
            try
            {
                ThumbnailGenerator.ClearCache();
                var doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nĐã xoá toàn bộ cache thumbnail tại: {ThumbnailGenerator.ThumbCacheFolder}\n");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBCLEARCACHE");
            }
        }

        /// <summary>Xoá vòng đỏ + đường dẫn đánh dấu block trùng (layer LHB_BLOCK_TRUNG).</summary>
        [CommandMethod("LHBDUPCLEAR")]
        public void ClearDuplicateMarkers()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                int n = DuplicateFinder.ClearMarkers(doc);
                doc.Editor.WriteMessage($"\nĐã xoá {n} nét đánh dấu block trùng (layer {DuplicateFinder.MarkerLayerName}).\n");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBDUPCLEAR");
                doc.Editor.WriteMessage($"\nLỗi - xem chi tiết tại {Logger.GetLogFilePath()}");
            }
        }

        /// <summary>Mở file log.txt bằng Notepad.</summary>
        [CommandMethod("LHBLOG")]
        public void OpenLog()
        {
            try
            {
                string path = Logger.GetLogFilePath();
                if (!File.Exists(path))
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nChưa có file log.");
                    return;
                }
                Logger.Flush(); // đóng file log đang giữ để Notepad mở được
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LHBLOG");
            }
        }

        /// <summary>In đường dẫn tuyệt đối của cả 2 vị trí file log ra Command line.</summary>
        [CommandMethod("LHBLOGPATH")]
        public void PrintLogPath()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n==================================================================");
            ed.WriteMessage("\n[LHB] ĐƯỜNG DẪN CÁC FILE LOG HIỆN HÀNH:");

            string localPath = Logger.LocalLogFilePath;
            ed.WriteMessage($"\n  1. Log cạnh DLL   : {localPath ?? "(Không xác định)"}");
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                var fi = new FileInfo(localPath);
                ed.WriteMessage($" (Tồn tại, {fi.Length:N0} bytes, sửa đổi: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
            else
            {
                ed.WriteMessage(" (Chưa tạo hoặc thư mục read-only)");
            }

            string appDataPath = Logger.AppDataLogFilePath;
            ed.WriteMessage($"\n  2. Log tại APPDATA: {appDataPath}");
            if (File.Exists(appDataPath))
            {
                var fi = new FileInfo(appDataPath);
                ed.WriteMessage($" (Tồn tại, {fi.Length:N0} bytes, sửa đổi: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
            else
            {
                ed.WriteMessage(" (Chưa tồn tại trên đĩa)");
            }

            ed.WriteMessage("\n==================================================================\n");
        }

        /// <summary>
        /// Xem thông tin phiên bản từ build-info.txt NẰM CẠNH DLL ĐANG CHẠY.
        /// Hoạt động chính xác trên bất kỳ máy nào kể cả khi copy ZIP sang máy khác.
        /// </summary>
        [CommandMethod("LHBVERSION")]
        public void ShowVersion()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            string dllPath = Assembly.GetExecutingAssembly().Location;
            string dllDir = Path.GetDirectoryName(dllPath);
            string buildInfoPath = Path.Combine(dllDir, "build-info.txt");

            string config = "Chưa rõ";
            string buildTime = "Chưa rõ";
            string fileMd5 = "Chưa rõ";
            long dllSize = 0;

            if (File.Exists(dllPath))
            {
                var fi = new FileInfo(dllPath);
                dllSize = fi.Length;
                buildTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (File.Exists(buildInfoPath))
            {
                try
                {
                    foreach (var rawLine in File.ReadAllLines(buildInfoPath))
                    {
                        var line = rawLine?.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        int colonIdx = line.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = line.Substring(0, colonIdx).Trim();
                            string val = line.Substring(colonIdx + 1).Trim();
                            if (key.Equals("Configuration", StringComparison.OrdinalIgnoreCase)) config = val;
                            else if (key.Equals("BuildTime", StringComparison.OrdinalIgnoreCase)) buildTime = val;
                            else if (key.Equals("DllSize", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long s)) dllSize = s;
                            else if (key.Equals("MD5", StringComparison.OrdinalIgnoreCase)) fileMd5 = val.Trim().ToUpperInvariant();
                        }
                    }
                }
                catch { }
            }

            string actualMd5 = ComputeMd5(dllPath);

            ed.WriteMessage("\n==================================================================");
            ed.WriteMessage("\n[LHB] THÔNG TIN PHIÊN BẢN ĐANG CHẠY:");
            ed.WriteMessage($"\n  - Phiên bản           : {MyApp.DisplayVersion}, bản DLL cho {MyApp.BuildTarget}");
            ed.WriteMessage($"\n  - AutoCAD / .NET      : {MyApp.AcadVersionText} / {MyApp.RuntimeText}");
            ed.WriteMessage($"\n  - Đường dẫn DLL       : {dllPath}");
            ed.WriteMessage($"\n  - File build-info.txt : {(File.Exists(buildInfoPath) ? buildInfoPath : "(Không tìm thấy cạnh DLL)")}");
            ed.WriteMessage($"\n  - Cấu hình (Config)   : {config}");
            ed.WriteMessage($"\n  - Thời gian build     : {buildTime}");
            ed.WriteMessage($"\n  - Kích thước DLL      : {dllSize:N0} bytes");
            ed.WriteMessage($"\n  - MD5 (từ build-info) : {fileMd5}");
            ed.WriteMessage($"\n  - MD5 (tính từ DLL)   : {actualMd5}");
            if (!string.IsNullOrEmpty(fileMd5) && fileMd5 != "Chưa rõ" && !string.Equals(fileMd5, actualMd5, StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage("\n  - CẢNH BÁO NGUY HIỂM  : MD5 thực tế KHÔNG KHỚP với build-info.txt!");
            }
            ed.WriteMessage("\n==================================================================\n");
        }

        /// <summary>
        /// Lệnh LHBDIAG: Gom toàn bộ bằng chứng chẩn đoán hệ thống vào file text ở thư mục gốc add-in (cạnh LHB.lsp).
        /// Chạy được kể cả khi form chưa mở.
        /// </summary>
        [CommandMethod("LHBDIAG")]
        public void RunDiagnostics()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n[LHBDIAG] Đang thu thập thông tin chẩn đoán hệ thống...");

            var sb = new StringBuilder();
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string fileTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            sb.AppendLine("================================================================================");
            sb.AppendLine($" BÁO CÁO CHẨN ĐOÁN LHBBLOCKSCHEDULER (LHBDIAG) - {nowStr}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            // 1. Thông tin DLL và Build
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string dllDir = Path.GetDirectoryName(dllPath);
            string buildInfoPath = Path.Combine(dllDir, "build-info.txt");
            string actualMd5 = ComputeMd5(dllPath);

            sb.AppendLine("--- 1. THÔNG TIN ASSEMBLY & BUILD ---");
            sb.AppendLine($"Phiên bản                : {MyApp.DisplayVersion} ({MyApp.AssemblyVersionText})");
            sb.AppendLine($"Bản DLL cho              : {MyApp.BuildTarget}");
            sb.AppendLine($".NET đang chạy           : {MyApp.RuntimeText}");
            sb.AppendLine($"Đường dẫn DLL đang chạy : {dllPath}");
            sb.AppendLine($"Thư mục gốc add-in       : {Logger.AddinRootFolder}");
            sb.AppendLine($"MD5 thực tế từ DLL       : {actualMd5}");
            if (File.Exists(dllPath))
            {
                var fi = new FileInfo(dllPath);
                sb.AppendLine($"Kích thước DLL           : {fi.Length:N0} bytes");
                sb.AppendLine($"LastWriteTime DLL        : {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
            if (File.Exists(buildInfoPath))
            {
                sb.AppendLine("Nội dung build-info.txt  :");
                foreach (var line in File.ReadAllLines(buildInfoPath))
                {
                    sb.AppendLine($"  {line}");
                }
            }
            else
            {
                sb.AppendLine("build-info.txt           : (Không tìm thấy cạnh DLL)");
            }
            sb.AppendLine();

            // 2. Thông tin AutoCAD và Môi trường
            sb.AppendLine("--- 2. THÔNG TIN AUTOCAD & BẢN VẼ ---");
            try
            {
                sb.AppendLine($"AutoCAD Version (API)    : {Application.Version}");
                object acadVer = Application.GetSystemVariable("ACADVER");
                sb.AppendLine($"Biến hệ thống ACADVER    : {acadVer}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Lỗi đọc phiên bản AutoCAD: {ex.Message}");
            }

            if (doc != null)
            {
                try
                {
                    sb.AppendLine($"Tên bản vẽ đang mở       : {doc.Name}");
                    sb.AppendLine($"Đường dẫn bản vẽ (DB)    : {doc.Database?.Filename}");
                    sb.AppendLine($"DIMSCALE                 : {Application.GetSystemVariable("DIMSCALE")}");

                    bool isWcs = false;
                    try
                    {
                        short worldUcs = Convert.ToInt16(Application.GetSystemVariable("WORLDUCS"));
                        isWcs = (worldUcs == 1);
                    }
                    catch
                    {
                        var ucsMatrix = doc.Editor.CurrentUserCoordinateSystem;
                        isWcs = ucsMatrix.IsEqualTo(Autodesk.AutoCAD.Geometry.Matrix3d.Identity);
                    }
                    sb.AppendLine($"UCS hiện hành là WCS?    : {(isWcs ? "CÓ (WCS)" : "KHÔNG (Đang dùng UCS riêng)")}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Lỗi đọc thông tin bản vẽ : {ex.Message}");
                }
            }
            else
            {
                sb.AppendLine("Trạng thái bản vẽ        : Không có bản vẽ nào đang mở.");
            }
            sb.AppendLine();

            // 3. Đường dẫn & Trạng thái các thư mục/file cấu hình
            sb.AppendLine("--- 3. ĐƯỜNG DẪN & TRẠNG THÁI FILE / THƯ MỤC DỮ LIỆU ---");
            AppendItemStatus(sb, "Local log.txt (cạnh DLL)", Logger.LocalLogFilePath);
            AppendItemStatus(sb, "AppData log.txt", Logger.AppDataLogFilePath);
            AppendItemStatus(sb, "Settings file (settings.json)", SettingsManager.SettingsFilePath);
            AppendDirStatus(sb, "Thư mục block mẫu", TemplateLibraryManager.Folder);
            AppendDirStatus(sb, "Thư mục block mẫu dự phòng", TemplateLibraryManager.BackupFolder);
            AppendDirStatus(sb, "Thư viện thiết bị cũ", TemplateLibraryManager.OldLibrariesFolder);
            AppendDirStatus(sb, "Thư mục Thumbs cache", ThumbnailGenerator.ThumbCacheFolder);
            AppendDirStatus(sb, "Thư mục CustomImages", ThumbnailGenerator.CustomImagesFolder);
            sb.AppendLine();

            // v9.5: bản quyền v2 - để hỗ trợ khách từ xa (mã máy, nguồn, key đang lưu, dùng thử)
            sb.AppendLine("--- 3a. BẢN QUYỀN ---");
            try
            {
                var lic = LicenseManager.Current;
                sb.AppendLine($"Bắt bản quyền (Enforced)  : {LicenseManager.Enforced}");
                sb.AppendLine($"Mã máy                   : {LicenseManager.MachineCode} (nguồn: {LicenseManager.MachineSource})");
                sb.AppendLine($"Trạng thái               : {LicenseManager.StatusText}");
                if (lic.Data != null)
                    sb.AppendLine($"Key đang lưu             : serial {lic.Data.SerialText}, kid {lic.Data.KeyId}, {lic.Data.KindText}, cấp cho '{lic.Data.Label}', " +
                                  $"cấp {lic.Data.Issued:dd/MM/yyyy}, hạn {(lic.Expiry.HasValue ? lic.Expiry.Value.ToString("dd/MM/yyyy") : "trọn đời")}" +
                                  (lic.Data.Kind == LicenseKind.Machine ? $", mã máy trong key {lic.Data.MachineCode}" : ""));
                if (!lic.Valid) sb.AppendLine($"Key không dùng được vì    : {lic.Error}");
                if (!lic.Valid && LicenseManager.Enforced) sb.AppendLine($"Dùng thử còn             : {LicenseManager.TrialDaysLeft} ngày {LicenseManager.TrialProblem}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Lỗi đọc bản quyền: {ex.Message}");
            }
            sb.AppendLine();

            sb.AppendLine("--- 3b. PHÍM TẮT LỆNH (LHBLENH) ---");
            try
            {
                CommandAliasManager.AppendDiag(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Lỗi đọc phím tắt: {ex.Message}");
            }
            sb.AppendLine();

            // 4. Các bộ block mẫu: tóm tắt từng block mẫu (không in ảnh base64 cho gọn file)
            sb.AppendLine("--- 4. THƯ VIỆN BLOCK MẪU ---");
            try
            {
                sb.AppendLine($"Bộ đang dùng: '{SettingsManager.Current.CurrentTemplateSet}', chỉ quét block mẫu = {!SettingsManager.Current.ScanAllBlocks}, " +
                              $"đã chuyển thư viện cũ = {SettingsManager.Current.DeviceLibrariesMigrated}");
                // v9.4: tuỳ chọn quét form lưu lại (LHBSCAN / lệnh Premium dùng chung)
                sb.AppendLine($"Tuỳ chọn quét: [{ExtractionOptions.FromSettings(SettingsManager.Current)}], không đếm trùng = {!SettingsManager.Current.CountDuplicateBlocks}");
                foreach (var set in TemplateLibraryManager.ListSets())
                {
                    var lib = TemplateLibraryManager.Load(set);
                    string dwg = TemplateLibraryManager.DwgPath(set);
                    sb.AppendLine();
                    sb.AppendLine($"  === Bộ '{set}': {lib.Entries.Count} block mẫu, file .dwg {(File.Exists(dwg) ? $"{new FileInfo(dwg).Length:N0} bytes" : "CHƯA CÓ")} ===");
                    foreach (var e in lib.Entries)
                        sb.AppendLine($"  {e.BlockName}" + (string.IsNullOrEmpty(e.VisibilityState) ? "" : $" [{e.VisibilityState}]") +
                                      $" -> '{e.DisplayName}', {e.Unit}, {e.BlockKind}, ảnh={(string.IsNullOrEmpty(e.ThumbnailBase64) ? "KHÔNG" : "có")}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[LỖI ĐỌC THƯ VIỆN BLOCK MẪU: {ex.Message}]");
            }
            sb.AppendLine();

            // 5. 200 dòng cuối của log.txt
            sb.AppendLine("--- 5. 200 DÒNG CUỐI CỦA LOG.TXT ---");
            string activeLogPath = Logger.GetLogFilePath();
            if (File.Exists(activeLogPath))
            {
                sb.AppendLine($"Nguồn log: {activeLogPath}");
                try
                {
                    // Đọc chia sẻ: Logger có thể đang giữ file log
                    foreach (var l in Logger.ReadTail(activeLogPath, 200))
                    {
                        sb.AppendLine(l);
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[LỖI ĐỌC LOG: {ex.Message}]");
                }
            }
            else
            {
                sb.AppendLine($"Chưa có file log tại '{activeLogPath}'.");
            }
            sb.AppendLine();
            sb.AppendLine("========================== HẾT BÁO CÁO CHẨN ĐOÁN ==========================");

            // Ghi ra file <thư mục gốc add-in>\LHBDIAG_<yyyyMMdd_HHmmss>.txt (v9.7: bản AutoCAD 2025+ ở thư mục con net8 /
            // net10 cũng ghi ra thư mục gốc, cạnh LHB.lsp - chỗ user quen lấy file)
            string diagFileName = $"LHBDIAG_{fileTimestamp}.txt";
            string targetDiagPath = Path.Combine(Logger.AddinRootFolder ?? dllDir, diagFileName);
            bool saved = false;

            try
            {
                File.WriteAllText(targetDiagPath, sb.ToString(), Encoding.UTF8);
                saved = true;
            }
            catch
            {
                // Nếu thư mục DLL read-only, fallback ghi vào AppData
                try
                {
                    targetDiagPath = Path.Combine(Logger.AppDataFolder, diagFileName);
                    if (!Directory.Exists(Logger.AppDataFolder))
                        Directory.CreateDirectory(Logger.AppDataFolder);
                    File.WriteAllText(targetDiagPath, sb.ToString(), Encoding.UTF8);
                    saved = true;
                }
                catch { }
            }

            if (saved)
            {
                ed.WriteMessage("\n==================================================================");
                ed.WriteMessage($"\n[LHBDIAG] ĐÃ TẠO FILE CHẨN ĐOÁN THÀNH CÔNG:");
                ed.WriteMessage($"\n  -> {targetDiagPath}");
                ed.WriteMessage("\n==================================================================\n");
            }
            else
            {
                ed.WriteMessage($"\n[LHBDIAG] Không thể ghi file chẩn đoán: Quyền truy cập bị từ chối.\n");
            }
        }

        /// <summary>
        /// Hàm LISP (lhb-dllinfo) -> ("đường dẫn DLL đang chạy" "MD5"). LHB.lsp gọi để biết AutoCAD đang giữ bản nào:
        /// .NET không gỡ được DLL đã nạp, NETLOAD bản mới cùng tên khi bản cũ đã nạp thì AutoCAD vẫn chạy bản cũ
        /// (lỗi "gọi NETLOAD bản cũ khi load lisp", test 29/09/2026). Bản cũ không có hàm này -> LISP biết là bản cũ.
        /// </summary>
        [LispFunction("LHB-DLLINFO")]
        public ResultBuffer DllInfo(ResultBuffer args)
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string md5 = ComputeMd5(dllPath);
            Logger.Log($"[LHB-DLLINFO] LISP hỏi bản đang chạy: '{dllPath}', MD5={md5}");
            return new ResultBuffer(
                new TypedValue((int)LispDataType.Text, dllPath),
                new TypedValue((int)LispDataType.Text, md5));
        }

        private static void AppendItemStatus(StringBuilder sb, string label, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine($"{label,-30} : (Đường dẫn không xác định)");
                return;
            }
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                sb.AppendLine($"{label,-30} : TỒN TẠI ({fi.Length:N0} bytes) -> {path}");
            }
            else
            {
                sb.AppendLine($"{label,-30} : CHƯA CÓ -> {path}");
            }
        }

        private static void AppendDirStatus(StringBuilder sb, string label, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine($"{label,-30} : (Đường dẫn không xác định)");
                return;
            }
            if (Directory.Exists(path))
            {
                int count = Directory.GetFileSystemEntries(path).Length;
                sb.AppendLine($"{label,-30} : TỒN TẠI ({count} mục) -> {path}");
            }
            else
            {
                sb.AppendLine($"{label,-30} : CHƯA CÓ -> {path}");
            }
        }

        private static string ComputeMd5(string filePath)
        {
            if (!File.Exists(filePath)) return "FILE_NOT_FOUND";
            try
            {
                // v9.4: HashHelper tự tính MD5 khi Windows bật chính sách FIPS (MD5.Create() ném lỗi)
                return HashHelper.Md5HexOfFile(filePath);
            }
            catch (Exception ex)
            {
                return $"ERROR_{ex.Message}";
            }
        }
    }
}

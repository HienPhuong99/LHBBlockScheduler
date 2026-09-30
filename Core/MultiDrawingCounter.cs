using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using LHBBlockScheduler.Models;
using Exception = System.Exception;

namespace LHBBlockScheduler.Core
{
    /// <summary>1 loại thiết bị trong thống kê nhiều bản vẽ.</summary>
    public class MultiRow
    {
        public string Key { get; set; }
        public string BlockName { get; set; }
        public string VisibilityState { get; set; }
        public string DisplayName { get; set; }
        public string Unit { get; set; }
        public int Order { get; set; }
        public bool FromTemplate { get; set; }
        /// <summary>v9.6: tên tham số số của block động không có Visibility (khớp block mẫu lưu từ bản cũ).</summary>
        public ICollection<string> NumericParamNames { get; set; }
        /// <summary>SL theo bản vẽ, key = nhãn bản vẽ.</summary>
        public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int Total => Counts.Values.Sum();
    }

    /// <summary>
    /// Thống kê nhiều bản vẽ cùng lúc (Premium P3): mỗi file DWG đọc bằng database phụ (ReadDwgFile, không mở lên màn
    /// hình), quét mọi block trong Model Space với tuỳ chọn quét đang dùng, áp block mẫu, trừ block trùng, cộng dồn theo
    /// tên block + chủng loại. Bản vẽ đang mở trong AutoCAD thì đọc thẳng database đang mở (có cả phần chưa lưu).
    /// </summary>
    public static class MultiDrawingCounter
    {
        public static List<MultiRow> Count(Document current, IList<string> files, bool includeCurrent, ExtractionOptions options,
                                           TemplateLibrary template, bool onlyTemplate, bool excludeDuplicates,
                                           Action<string> progress, out List<string> labels, out List<string> errors)
        {
            labels = new List<string>();
            errors = new List<string>();
            var rows = new Dictionary<string, MultiRow>(StringComparer.OrdinalIgnoreCase);

            var sources = new List<(string Label, string Path)>();
            if (includeCurrent && current != null) sources.Add((Path.GetFileNameWithoutExtension(current.Name) + " (đang mở)", null));
            foreach (var f in files) sources.Add((Path.GetFileNameWithoutExtension(f), f));

            foreach (var (label0, path) in sources)
            {
                string label = label0;
                for (int i = 2; labels.Contains(label, StringComparer.OrdinalIgnoreCase); i++) label = label0 + " (" + i + ")";
                progress?.Invoke(label);
                try
                {
                    List<BlockItem> items;
                    var openDoc = path == null ? current : FindOpenDocument(path);
                    if (openDoc != null)
                    {
                        items = Extract(openDoc.Database, options, template, onlyTemplate, excludeDuplicates);
                    }
                    else
                    {
                        using (var sdb = new Database(false, true))
                        {
                            sdb.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                            sdb.CloseInput(true);
                            // Khung bao chữ / kiểu chữ tính theo WorkingDatabase -> tạm chuyển sang bản vẽ phụ
                            var oldWdb = HostApplicationServices.WorkingDatabase;
                            HostApplicationServices.WorkingDatabase = sdb;
                            try
                            {
                                items = Extract(sdb, options, template, onlyTemplate, excludeDuplicates);
                            }
                            finally
                            {
                                HostApplicationServices.WorkingDatabase = oldWdb;
                            }
                        }
                    }
                    labels.Add(label);

                    foreach (var it in items)
                    {
                        string key = it.BlockName + "||" + (options.SplitByVisibility ? it.VisibilityState ?? "" : "");
                        if (!rows.TryGetValue(key, out var row))
                        {
                            var entry = TemplateLibraryManager.Match(template, it.BlockName, it.VisibilityState, it.NumericParamNames);
                            row = new MultiRow
                            {
                                Key = key,
                                NumericParamNames = it.NumericParamNames,
                                BlockName = it.BlockName,
                                VisibilityState = options.SplitByVisibility ? it.VisibilityState : "",
                                DisplayName = it.DisplayName ?? it.BlockName,
                                Unit = it.Unit ?? "Cái",
                                FromTemplate = entry != null,
                                Order = entry != null ? template.Entries.IndexOf(entry) : int.MaxValue
                            };
                            rows[key] = row;
                        }
                        row.Counts[label] = (row.Counts.TryGetValue(label, out int n) ? n : 0) + it.Count;
                    }
                    Logger.Log($"[MultiDrawing] '{label}': {items.Count} loại block, {items.Sum(i => i.Count)} block" + (openDoc != null ? " (bản vẽ đang mở)" : ""));
                }
                catch (Exception ex)
                {
                    errors.Add($"{label}: {ex.Message}");
                    Logger.Error(ex, $"[MultiDrawing] Đọc '{path ?? label}'");
                }
            }

            return rows.Values.OrderBy(r => r.FromTemplate ? 0 : 1).ThenBy(r => r.Order)
                       .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<BlockItem> Extract(Database db, ExtractionOptions options, TemplateLibrary template, bool onlyTemplate, bool excludeDuplicates)
        {
            var items = BlockExtractor.ExtractFromDatabase(db, BlockExtractor.ModelSpaceBlockIds(db), options);
            items = TemplateLibraryManager.Apply(items, template, onlyTemplate);
            // Tìm trùng khi database còn mở (DuplicateFinder đọc handle)
            DuplicateFinder.Detect(items, SettingsManager.Current.DuplicateTolerance, SettingsManager.Current.DuplicateOverlapPercent);
            DuplicateFinder.SetExclusion(items, excludeDuplicates);
            return items;
        }

        private static Document FindOpenDocument(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (Document d in Application.DocumentManager)
            {
                try
                {
                    if (!string.IsNullOrEmpty(d.Name) && File.Exists(d.Name) &&
                        string.Equals(Path.GetFullPath(d.Name), full, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
                catch { }
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using LHBBlockScheduler.Models;

namespace LHBBlockScheduler.Core
{
    /// <summary>Cột thuộc tính (Premium P5): giá trị thuộc tính / tham số của các block trong 1 dòng.</summary>
    public static class PremiumColumns
    {
        public const string AttrPrefix = "attr:";

        /// <summary>Mọi khoá thuộc tính / tham số có trong các dòng (A:... trước, D:... sau).</summary>
        public static List<string> CollectAttributeKeys(IEnumerable<BlockItem> items)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
                foreach (var inst in it.Instances ?? new List<BlockInstanceRef>())
                    if (inst.Attributes != null)
                        foreach (var k in inst.Attributes.Keys) set.Add(k);
            return set.OrderBy(k => k.StartsWith("A:") ? 0 : 1).ThenBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Ghi item.ExtraValues["attr:&lt;khoá&gt;"]: 1 giá trị -> giá trị đó; nhiều giá trị -> liệt kê tối đa 3 ("a; b; c...").
        /// Block thừa do trùng không tính.
        /// </summary>
        public static void ComputeAttributeValues(IEnumerable<BlockItem> items, IEnumerable<string> attrKeys)
        {
            var keys = attrKeys.ToList();
            foreach (var item in items)
            {
                item.ExtraValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in item.ExtraValues.Keys.Where(k => k.StartsWith(AttrPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
                    item.ExtraValues.Remove(k);
                if (item.Instances == null) continue;
                foreach (var key in keys)
                {
                    var values = item.Instances.Where(i => !i.IsExcludedDuplicate && i.Attributes != null && i.Attributes.ContainsKey(key))
                                               .Select(i => i.Attributes[key] ?? "")
                                               .Where(v => v.Length > 0)
                                               .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (values.Count == 0) continue;
                    item.ExtraValues[AttrPrefix + key] = values.Count <= 3 ? string.Join("; ", values) : string.Join("; ", values.Take(3)) + "...";
                }
            }
        }
    }
}

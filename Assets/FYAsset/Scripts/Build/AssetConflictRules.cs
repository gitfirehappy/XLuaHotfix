using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 资源条目唯一性、警告与阻断规则（仅编辑器/构建期使用）。
/// 
/// 规则层级：
/// - 阻断(Block)：构建必须中止，开发者必须修复
/// - 警告(Warn)：允许构建，但开发者应关注
/// - 通过(Pass)：无问题
/// </summary>
public static class AssetConflictRules
{
    #region 数据模型

    /// <summary>
    /// 冲突检查结果
    /// </summary>
    public class ConflictReport
    {
        public List<ConflictEntry> Blocks = new();
        public List<ConflictEntry> Warnings = new();

        public bool HasBlocks => Blocks.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
        public bool IsClean => !HasBlocks && !HasWarnings;
    }

    /// <summary>
    /// 单条冲突信息
    /// </summary>
    public class ConflictEntry
    {
        public ConflictType Type;
        public string Message;
        public List<RuntimeAssetEntry> InvolvedEntries = new();
    }

    /// <summary>
    /// 冲突类型枚举
    /// </summary>
    public enum ConflictType
    {
        /// <summary>
        /// 同一 EntryId 出现多次 → 阻断
        /// </summary>
        DuplicateEntryId,

        /// <summary>
        /// 同一 Address + PrimaryType + 完全相同 LabelSet → 阻断
        /// （运行时 Resolve 无法区分）
        /// </summary>
        IdenticalAddressTypeLabelSet,

        /// <summary>
        /// 同一 Address + PrimaryType，靠不同 Labels 区分 → 警告
        /// </summary>
        AddressTypeSameLabelsDiffer,

        /// <summary>
        /// 标签子集歧义：条目 A 的 LabelSet 是条目 B 的子集，
        /// 查询时可能意外命中 → 警告
        /// </summary>
        LabelSubsetAmbiguity,
    }

    #endregion

    #region 公开入口

    /// <summary>
    /// 对一组条目执行全量冲突检查。
    /// 编辑器/构建期使用，允许 LINQ。
    /// </summary>
    public static ConflictReport Validate(IList<RuntimeAssetEntry> entries)
    {
        var report = new ConflictReport();

        CheckDuplicateEntryIds(entries, report);
        CheckAddressTypeConflicts(entries, report);
        CheckLabelSubsetAmbiguity(entries, report);

        return report;
    }

    #endregion

    #region 检查规则实现

    /// <summary>
    /// 规则 1：EntryId 必须全局唯一 → 重复则阻断
    /// </summary>
    private static void CheckDuplicateEntryIds(IList<RuntimeAssetEntry> entries, ConflictReport report)
    {
        var groups = entries.GroupBy(e => e.EntryId);
        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count <= 1) continue;

            report.Blocks.Add(new ConflictEntry
            {
                Type = ConflictType.DuplicateEntryId,
                Message = string.Concat(
                    "EntryId '", group.Key, "' 出现 ", items.Count.ToString(), " 次，必须唯一。"),
                InvolvedEntries = items
            });
        }
    }

    /// <summary>
    /// 规则 2 + 3：Address + PrimaryType 冲突检测
    /// - 完全相同 LabelSet → 阻断
    /// - 不同 LabelSet → 警告
    /// </summary>
    private static void CheckAddressTypeConflicts(IList<RuntimeAssetEntry> entries, ConflictReport report)
    {
        var groups = entries
            .GroupBy(e => string.Concat(
                (e.Address ?? "").ToLowerInvariant(), "|",
                (e.PrimaryType ?? "").ToLowerInvariant()));

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count <= 1) continue;

            // 检查 LabelSet 是否完全相同
            var labelSetGroups = items
                .GroupBy(e => GetNormalizedLabelSetKey(e))
                .ToList();

            foreach (var lsg in labelSetGroups)
            {
                var sameLabels = lsg.ToList();
                if (sameLabels.Count > 1)
                {
                    // 完全相同 Address + Type + LabelSet → 阻断
                    report.Blocks.Add(new ConflictEntry
                    {
                        Type = ConflictType.IdenticalAddressTypeLabelSet,
                        Message = string.Concat(
                            "Address='", items[0].Address, "' + Type='", items[0].PrimaryType,
                            "' + LabelSet 完全相同，运行时 Resolve 无法区分。共 ",
                            sameLabels.Count.ToString(), " 条。"),
                        InvolvedEntries = sameLabels
                    });
                }
            }

            if (labelSetGroups.Count > 1)
            {
                // 同 Address + Type 但不同 LabelSet → 警告
                report.Warnings.Add(new ConflictEntry
                {
                    Type = ConflictType.AddressTypeSameLabelsDiffer,
                    Message = string.Concat(
                        "Address='", items[0].Address, "' + Type='", items[0].PrimaryType,
                        "' 有 ", items.Count.ToString(), " 条条目，靠 Labels 区分。确认这是预期行为。"),
                    InvolvedEntries = items
                });
            }
        }
    }

    /// <summary>
    /// 规则 4：标签子集歧义检测
    /// 在同一 Address + PrimaryType 簇中，检查是否存在 LabelSet 子集关系
    /// </summary>
    private static void CheckLabelSubsetAmbiguity(IList<RuntimeAssetEntry> entries, ConflictReport report)
    {
        var groups = entries
            .GroupBy(e => string.Concat(
                (e.Address ?? "").ToLowerInvariant(), "|",
                (e.PrimaryType ?? "").ToLowerInvariant()));

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count <= 1) continue;

            for (int i = 0; i < items.Count; i++)
            {
                var setI = items[i].GetNormalizedLabels();
                for (int j = i + 1; j < items.Count; j++)
                {
                    var setJ = items[j].GetNormalizedLabels();

                    if (setI.Count == setJ.Count) continue; // 大小相同不可能是真子集

                    bool iSubsetOfJ = setI.IsSubsetOf(setJ);
                    bool jSubsetOfI = setJ.IsSubsetOf(setI);

                    if (iSubsetOfJ || jSubsetOfI)
                    {
                        var subset = iSubsetOfJ ? items[i] : items[j];
                        var superset = iSubsetOfJ ? items[j] : items[i];

                        report.Warnings.Add(new ConflictEntry
                        {
                            Type = ConflictType.LabelSubsetAmbiguity,
                            Message = string.Concat(
                                "条目 [", subset.EntryId, "] 的 LabelSet 是 [", superset.EntryId,
                                "] 的子集，查询时可能意外匹配。Address='", subset.Address,
                                "', Type='", subset.PrimaryType, "'。"),
                            InvolvedEntries = new List<RuntimeAssetEntry> { subset, superset }
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// 生成归一化的 LabelSet 键（用于相等比较）。
    /// 排序 + 小写 + 逗号拼接。
    /// </summary>
    private static string GetNormalizedLabelSetKey(RuntimeAssetEntry entry)
    {
        if (entry.Labels == null || entry.Labels.Count == 0) return "";
        var sorted = entry.Labels.Select(l => l.ToLowerInvariant()).OrderBy(l => l);
        return string.Join(",", sorted);
    }

    #endregion
}
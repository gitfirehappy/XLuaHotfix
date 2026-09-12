using System;
using System.Collections.Generic;

/// <summary>
/// 内容级依赖事实 → Manifest 依赖下标的唯一换算入口。
/// </summary>
/// <remarks>
/// 依赖下标来自 Unity 构建报告或正式 Summary 回放的内容级依赖事实；预期依赖图只用于诊断。
/// 全量构建与混合复用构建最终使用同一套换算逻辑。
/// </remarks>
public static class ContentDependencyIndexResolver
{
    /// <summary>
    /// 合并两条依赖事实来源（本次 Unity 构建的内容 + 复用缓存的内容）为一份完整依赖表。
    /// </summary>
    /// <param name="rebuiltDependencies">本次交给 Unity 构建的内容 → 其直接依赖输出文件名。</param>
    /// <param name="reusedDependencies">复用缓存的内容 → 缓存中记录的直接依赖输出文件名。</param>
    /// <param name="merged">合并结果：内容名 → 升序去重的依赖输出文件名。</param>
    /// <param name="problems">冲突与缺失说明；非空表示结果不可信，调用方必须放弃复用或阻断构建。</param>
    /// <returns>两份来源互不冲突且每个内容都有依赖事实时返回 true。</returns>
    public static bool TryMergeDependencyNames(
        IReadOnlyDictionary<string, IList<string>> rebuiltDependencies,
        IReadOnlyDictionary<string, IList<string>> reusedDependencies,
        out Dictionary<string, List<string>> merged,
        out List<string> problems)
    {
        merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        problems = new List<string>();

        AppendDependencies(merged, problems, reusedDependencies, "缓存复用");
        AppendDependencies(merged, problems, rebuiltDependencies, "本次重建");

        return problems.Count == 0;
    }

    /// <summary>
    /// 建立输出文件名 → 内容下标映射（大小写不敏感，与 ManifestContentEntry.FileName 的查重口径一致）。
    /// 空名或重名（大小写不敏感）都会失败。
    /// </summary>
    public static bool TryBuildFileIndex(
        IReadOnlyList<string> orderedFileNames,
        out Dictionary<string, int> indexByFileName,
        out string failureReason)
    {
        indexByFileName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        failureReason = null;
        if (orderedFileNames == null)
        {
            failureReason = "内容文件名列表为空";
            return false;
        }

        for (int i = 0; i < orderedFileNames.Count; i++)
        {
            string fileName = orderedFileNames[i];
            if (string.IsNullOrEmpty(fileName))
            {
                failureReason = $"内容下标 {i} 的输出文件名为空";
                return false;
            }

            if (indexByFileName.ContainsKey(fileName))
            {
                failureReason = $"输出文件名重复: '{fileName}'";
                return false;
            }

            indexByFileName[fileName] = i;
        }

        return true;
    }

    /// <summary>
    /// 把单个内容的依赖文件名换算成 Manifest 依赖下标：升序去重，忽略自依赖。
    /// 依赖名在当前构建中不存在时失败（此时该内容必须重建，不得沿用旧下标）。
    /// </summary>
    public static bool TryResolveDependencyIndices(
        string contentName,
        IReadOnlyList<string> dependencyFileNames,
        IReadOnlyDictionary<string, int> indexByFileName,
        out int[] indices,
        out string failureReason)
    {
        indices = Array.Empty<int>();
        failureReason = null;
        if (dependencyFileNames == null || dependencyFileNames.Count == 0)
            return true;

        if (indexByFileName == null)
        {
            failureReason = $"内容 '{contentName}' 缺少输出文件名索引";
            return false;
        }

        var resolved = new List<int>(dependencyFileNames.Count);
        for (int i = 0; i < dependencyFileNames.Count; i++)
        {
            string dependencyFileName = dependencyFileNames[i];
            if (string.IsNullOrEmpty(dependencyFileName))
                continue;

            if (string.Equals(dependencyFileName, contentName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!indexByFileName.TryGetValue(dependencyFileName, out int dependencyIndex))
            {
                failureReason = $"内容 '{contentName}' 依赖的输出文件 '{dependencyFileName}' 不在本次构建内容集合中";
                return false;
            }

            if (!resolved.Contains(dependencyIndex))
                resolved.Add(dependencyIndex);
        }

        resolved.Sort();
        indices = resolved.ToArray();
        return true;
    }

    /// <summary>
    /// 依赖文件名是否都能在当前构建的内容集合中解析；用于复用判定阶段的保守退化。
    /// </summary>
    public static bool AreDependenciesResolvable(
        string contentName,
        IReadOnlyList<string> dependencyFileNames,
        IReadOnlyCollection<string> knownFileNames,
        out string failureReason)
    {
        failureReason = null;
        if (dependencyFileNames == null)
        {
            failureReason = $"内容 '{contentName}' 缺少依赖事实";
            return false;
        }

        if (dependencyFileNames.Count == 0 || knownFileNames == null)
            return true;

        var known = new HashSet<string>(knownFileNames, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < dependencyFileNames.Count; i++)
        {
            string dependencyFileName = dependencyFileNames[i];
            if (string.IsNullOrEmpty(dependencyFileName))
                continue;
            if (string.Equals(dependencyFileName, contentName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!known.Contains(dependencyFileName))
            {
                failureReason = $"内容 '{contentName}' 的缓存依赖 '{dependencyFileName}' 不在本次构建内容集合中";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 复用安全性裁剪：从可复用集合中移除所有被重建内容（含被移除后转为重建的内容）依赖到的内容。
    /// </summary>
    /// <remarks>
    /// Unity 只把显式列入本次构建的资产写入内容，未被显式分配的依赖资产会被复制进每个引用它的内容
    /// （Unity 手册 Asset Duplication）。因此若内容 A 重建、其依赖的内容 B 仍复用历史制品，
    /// A 会把 B 的资产复制进来，产物与全量构建不一致。
    /// </remarks>
    /// <param name="dependenciesByContent">内容名 → 它直接依赖的内容名集合。</param>
    /// <param name="reusableContents">当前允许复用的内容名集合；方法直接修改该集合。</param>
    /// <param name="rebuiltContents">本次必须重建的内容名（交给 Unity 构建的内容）。</param>
    /// <returns>被撤销复用的内容名，按撤销顺序排列，供调用方记录 Warning。</returns>
    public static List<string> DropReuseViolatingDependencyClosure(
        IReadOnlyDictionary<string, IList<string>> dependenciesByContent,
        ISet<string> reusableContents,
        IEnumerable<string> rebuiltContents)
    {
        var dropped = new List<string>();
        if (reusableContents == null || reusableContents.Count == 0 || rebuiltContents == null)
            return dropped;

        var requiresBuild = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (string name in rebuiltContents)
        {
            if (!string.IsNullOrEmpty(name) && requiresBuild.Add(name))
                queue.Enqueue(name);
        }

        while (queue.Count > 0)
        {
            string contentName = queue.Dequeue();
            if (dependenciesByContent == null
                || !dependenciesByContent.TryGetValue(contentName, out IList<string> dependencies)
                || dependencies == null)
            {
                continue;
            }

            for (int i = 0; i < dependencies.Count; i++)
            {
                string dependency = dependencies[i];
                if (string.IsNullOrEmpty(dependency) || !reusableContents.Remove(dependency))
                    continue;

                dropped.Add(dependency);
                if (requiresBuild.Add(dependency))
                    queue.Enqueue(dependency);
            }
        }

        return dropped;
    }

    private static void AppendDependencies(
        Dictionary<string, List<string>> merged,
        List<string> problems,
        IReadOnlyDictionary<string, IList<string>> source,
        string sourceLabel)
    {
        if (source == null)
            return;

        foreach (var pair in source)
        {
            string contentName = pair.Key;
            if (string.IsNullOrEmpty(contentName))
                continue;

            if (pair.Value == null)
            {
                problems.Add($"内容 '{contentName}' 在{sourceLabel}来源中缺少依赖事实");
                continue;
            }

            var normalized = Normalize(contentName, pair.Value);
            if (merged.TryGetValue(contentName, out List<string> existing))
            {
                if (!SequenceEqual(existing, normalized))
                {
                    problems.Add(
                        $"内容 '{contentName}' 的两份依赖事实不一致: [{string.Join(", ", existing)}] vs [{string.Join(", ", normalized)}]");
                }

                continue;
            }

            merged[contentName] = normalized;
        }
    }

    private static List<string> Normalize(string contentName, IList<string> dependencyFileNames)
    {
        var result = new List<string>(dependencyFileNames.Count);
        for (int i = 0; i < dependencyFileNames.Count; i++)
        {
            string dependencyFileName = dependencyFileNames[i];
            if (string.IsNullOrEmpty(dependencyFileName))
                continue;
            if (string.Equals(dependencyFileName, contentName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!result.Contains(dependencyFileName))
                result.Add(dependencyFileName);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool SequenceEqual(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}

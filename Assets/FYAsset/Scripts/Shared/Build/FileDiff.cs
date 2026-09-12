using System;
using System.Collections.Generic;

/// <summary>
/// 两份文件摘要集合的无状态比较结果：按 <see cref="FileDigest.Name"/> 配对，只比较 Hash/CRC/Size。
/// </summary>
/// <remarks>
/// 使用约束（计划 T7）：
/// 1. 只输入两份摘要或文件集合，不读 Git、PackageIndex、版本库、缓存或任何历史状态；
/// 2. 不持有生命周期，也不产生副作用，调用方决定结果如何被消费；
/// 3. 名称按 Ordinal 比较（大小写敏感）：包内文件名由构建侧统一产生，不做大小写折叠，
///    避免在区分大小写的平台把两个不同文件当成同一个；
/// 4. 只依赖 System 与 <see cref="FileDigest"/>，因此构建缓存、发布流程和纯 .NET 场景都能直接引用。
/// </remarks>
public sealed class FileDiff
{
    /// <summary>本次新增的文件（名称只出现在 current）</summary>
    public List<FileDigest> Added = new();

    /// <summary>同名但内容变化的文件（取 current 一侧的摘要）</summary>
    public List<FileDigest> Modified = new();

    /// <summary>同名且内容完全一致的文件（取 current 一侧的摘要）</summary>
    public List<FileDigest> Unchanged = new();

    /// <summary>只在 previous 中出现的文件名称</summary>
    public List<string> Removed = new();

    /// <summary>四类差异是否都为空。</summary>
    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Unchanged.Count == 0 && Removed.Count == 0;

    /// <summary>是否存在内容变化（新增或修改）。</summary>
    public bool HasContentChange => Added.Count > 0 || Modified.Count > 0;

    /// <summary>
    /// 比较两份文件摘要集合。
    /// previous 为 null 时按“没有任何历史文件”处理，current 全部记为 Added；
    /// current 为 null 时按“目标集合为空”处理，previous 全部记为 Removed。
    /// 同一次比较中忽略重复名称（保留首个出现的条目），因为包内同名文件本身已经是错误状态。
    /// </summary>
    public static FileDiff Compute(
        IReadOnlyList<FileDigest> previous,
        IReadOnlyList<FileDigest> current)
    {
        var diff = new FileDiff();
        var previousByName = IndexByName(previous);

        if (current != null)
        {
            for (int i = 0; i < current.Count; i++)
            {
                FileDigest item = current[i];
                if (string.IsNullOrEmpty(item.Name))
                    continue;

                if (!previousByName.TryGetValue(item.Name, out FileDigest baseline))
                {
                    diff.Added.Add(item);
                    continue;
                }

                // 只要 Hash/CRC/Size 任一不同即视为内容变化，避免把“大小相同但内容不同”当成未变化。
                if (string.Equals(baseline.Hash, item.Hash, StringComparison.Ordinal)
                    && baseline.CRC == item.CRC
                    && baseline.Size == item.Size)
                {
                    diff.Unchanged.Add(item);
                }
                else
                {
                    diff.Modified.Add(item);
                }
            }
        }

        if (previous != null)
        {
            for (int i = 0; i < previous.Count; i++)
            {
                FileDigest item = previous[i];
                if (string.IsNullOrEmpty(item.Name))
                    continue;
                if (ContainsName(current, item.Name))
                    continue;

                diff.Removed.Add(item.Name);
            }
        }

        return diff;
    }

    /// <summary>把新增与修改的文件名称合并成集合，供“需要重新产出的内容”判定使用。</summary>
    public HashSet<string> CollectChangedNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Added.Count; i++)
            names.Add(Added[i].Name);
        for (int i = 0; i < Modified.Count; i++)
            names.Add(Modified[i].Name);
        return names;
    }

    /// <summary>按名称建立索引；重复名称保留首个条目。</summary>
    public static Dictionary<string, FileDigest> IndexByName(IReadOnlyList<FileDigest> digests)
    {
        var index = new Dictionary<string, FileDigest>(StringComparer.Ordinal);
        if (digests == null)
            return index;

        for (int i = 0; i < digests.Count; i++)
        {
            FileDigest item = digests[i];
            if (string.IsNullOrEmpty(item.Name) || index.ContainsKey(item.Name))
                continue;

            index.Add(item.Name, item);
        }

        return index;
    }

    /// <summary>按内容 Hash + Size 建立索引，供“Hash 命中即可复用内容”的发布优化使用。</summary>
    public static Dictionary<string, FileDigest> IndexByHash(IReadOnlyList<FileDigest> digests)
    {
        var index = new Dictionary<string, FileDigest>(StringComparer.Ordinal);
        if (digests == null)
            return index;

        for (int i = 0; i < digests.Count; i++)
        {
            FileDigest item = digests[i];
            if (string.IsNullOrEmpty(item.Hash))
                continue;

            string key = HashKey(item.Hash, item.Size);
            if (!index.ContainsKey(key))
                index.Add(key, item);
        }

        return index;
    }

    /// <summary>Hash 复用索引键：同一内容可能以不同名称出现，Hash 与大小共同确定内容身份。</summary>
    public static string HashKey(string hash, long size) => hash + ":" + size;

    private static bool ContainsName(IReadOnlyList<FileDigest> digests, string name)
    {
        if (digests == null)
            return false;

        for (int i = 0; i < digests.Count; i++)
        {
            if (string.Equals(digests[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

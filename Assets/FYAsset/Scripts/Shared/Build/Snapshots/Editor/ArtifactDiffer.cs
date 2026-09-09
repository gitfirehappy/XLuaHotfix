#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 纯 Diff 计算器，只按 BuildDiffEntry.Name 配对并比较 Hash，不访问 Unity API，也不产生副作用。
/// </summary>
public static class ArtifactDiffer
{
    /// <summary>
    /// 按区分大小写的 Name 配对，只比较 Hash。返回条目引用输入对象；Size/CRC 不影响结果。
    /// </summary>
    public static ArtifactDelta Diff(IReadOnlyList<BuildDiffEntry> from, IReadOnlyList<BuildDiffEntry> to)
    {
        var delta = new ArtifactDelta();
        var fromByName = new Dictionary<string, BuildDiffEntry>();
        var toNames = new HashSet<string>();

        if (from != null)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i];
                if (item == null || string.IsNullOrEmpty(item.Name))
                    continue;
                if (!fromByName.ContainsKey(item.Name))
                    fromByName.Add(item.Name, item);
            }
        }

        if (to != null)
        {
            for (int i = 0; i < to.Count; i++)
            {
                var item = to[i];
                if (item == null || string.IsNullOrEmpty(item.Name))
                    continue;

                toNames.Add(item.Name);
                if (!fromByName.TryGetValue(item.Name, out var oldItem))
                {
                    delta.Added.Add(item);
                    continue;
                }

                if (!string.Equals(item.Hash, oldItem.Hash, System.StringComparison.Ordinal))
                    delta.Modified.Add(item);
            }
        }

        foreach (var item in fromByName)
        {
            if (!toNames.Contains(item.Key))
                delta.Removed.Add(item.Key);
        }

        return delta;
    }
}
#endif

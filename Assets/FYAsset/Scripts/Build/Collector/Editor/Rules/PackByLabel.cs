using System;
using System.Collections.Generic;

/// <summary>
/// 按标签打包规则 —— 相同标签组合的资源打入同一 Bundle。
/// packKey = 标签排序后以 "--" 连接；无标签时返回 "__orphan__"。
/// </summary>
public sealed class PackByLabel : IPackRule
{
    private const string OrphanSentinel = "__orphan__";

    #region Public Methods

    public string GetPackKey(PackRuleContext ctx)
    {
        if (ctx.Labels == null || ctx.Labels.Count == 0)
            return OrphanSentinel;

        List<string> sorted = new List<string>(ctx.Labels.Count);
        for (int i = 0; i < ctx.Labels.Count; i++)
        {
            string label = ctx.Labels[i];
            if (!string.IsNullOrEmpty(label))
                sorted.Add(label.ToLowerInvariant());
        }

        if (sorted.Count == 0)
            return OrphanSentinel;

        sorted.Sort(StringComparer.Ordinal);
        return string.Join("--", sorted);
    }

    #endregion
}

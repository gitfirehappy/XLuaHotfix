using System.IO;

/// <summary>
/// 默认打包规则 —— 同一 Collector.CollectPath 下的资源归为同一个 pack key。
/// </summary>
public sealed class PackByCollectPath : IPackRule
{
    private const string FallbackPackKey = "default";

    #region Public Methods

    /// <summary>返回 CollectPath 末段目录名作为分组键</summary>
    public string GetPackKey(PackRuleContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.CollectPath))
            return FallbackPackKey;

        string normalizedPath = ctx.CollectPath.Replace('\\', '/').TrimEnd('/');
        string lastSegment = Path.GetFileName(normalizedPath);
        return string.IsNullOrEmpty(lastSegment) ? FallbackPackKey : lastSegment;
    }

    #endregion
}

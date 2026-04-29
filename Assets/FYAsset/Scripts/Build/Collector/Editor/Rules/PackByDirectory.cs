using System;
using System.IO;

/// <summary>
/// 按子目录打包规则 —— 同一子目录下的资源打入同一 Bundle。
/// packKey = CollectPath 下的第一级子目录名；CollectPath 根级资源回退到 CollectPath 末段。
/// </summary>
public sealed class PackByDirectory : IPackRule
{
    #region Public Methods

    /// <summary>返回资源所在的第一级子目录名作为分组键；根级资源回退到 CollectPath 末段</summary>
    public string GetPackKey(PackRuleContext ctx)
    {
        string assetDir = Path.GetDirectoryName(ctx.AssetPath);
        if (string.IsNullOrEmpty(assetDir))
            return Fallback(ctx);

        string assetDirNorm = assetDir.Replace('\\', '/');
        string collectDirNorm = (ctx.CollectPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        // 资源直接在 CollectPath 根下 → 回退到 CollectPath 末段
        if (string.Equals(assetDirNorm, collectDirNorm, StringComparison.OrdinalIgnoreCase))
            return Fallback(ctx);

        if (assetDirNorm.Length <= collectDirNorm.Length + 1)
            return Fallback(ctx);

        // 取 CollectPath 下的第一级子目录
        string relative = assetDirNorm.Substring(collectDirNorm.Length + 1);
        int slashIndex = relative.IndexOf('/');
        return slashIndex >= 0 ? relative.Substring(0, slashIndex) : relative;
    }

    #endregion

    #region Private Methods

    private static string Fallback(PackRuleContext ctx)
    {
        return new PackByCollectPath().GetPackKey(ctx);
    }

    #endregion
}

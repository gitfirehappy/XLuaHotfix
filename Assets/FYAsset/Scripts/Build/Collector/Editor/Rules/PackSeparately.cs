using System.IO;

/// <summary>
/// 逐文件打包规则 —— 每个资源独立打包，packKey = 文件名（不含扩展名）。
/// </summary>
public sealed class PackSeparately : IPackRule
{
    #region Public Methods

    /// <summary>返回文件名（不含扩展名）作为分组键，每个资源独立打包</summary>
    public string GetPackKey(PackRuleContext ctx)
    {
        return Path.GetFileNameWithoutExtension(ctx.AssetPath);
    }

    #endregion
}

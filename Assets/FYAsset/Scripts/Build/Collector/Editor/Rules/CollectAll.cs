using System;

/// <summary>
/// 默认过滤规则 —— 收集所有有效资源，排除脚本、程序集定义、元文件和 Editor 目录。
/// </summary>
public sealed class CollectAll : IFilterRule
{
    #region 私有字段

    private static readonly string[] ExcludedExtensions =
    {
        ".meta",
        ".cs",
        ".dll",
        ".asmdef",
        ".asmref",
        ".gitignore"
    };

    #endregion

    #region 公共方法

    /// <summary>排除脚本、程序集定义、元文件、Editor 目录，其余全部收集</summary>
    public bool IsCollectable(FilterRuleContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.AssetPath))
            return false;

        if (HasExcludedExtension(ctx.Extension))
            return false;

        return !ContainsEditorDirectory(ctx.AssetPath);
    }

    #endregion

    #region 私有方法

    private static bool HasExcludedExtension(string extension)
    {
        for (int i = 0; i < ExcludedExtensions.Length; i++)
        {
            if (string.Equals(extension, ExcludedExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsEditorDirectory(string assetPath)
    {
        // 用 IndexOf 逐段匹配，避免 Split 产生临时字符串数组
        string normalizedPath = assetPath.Replace('\\', '/');
        int start = 0;
        int len = normalizedPath.Length;

        while (start < len)
        {
            int slash = normalizedPath.IndexOf('/', start);
            int end = slash < 0 ? len : slash;
            int segLen = end - start;

            if (segLen == 6 &&
                string.Compare(normalizedPath, start, "Editor", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            start = end + 1;
        }

        return false;
    }

    #endregion
}

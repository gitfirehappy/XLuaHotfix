using System.Text;

/// <summary>
/// Bundle 逻辑名组装工具 —— 将 PackRule 输出的分组键组装为标准化三段式名称。
/// 输出不含 Hash 和 .bundle 扩展名，这些由 E5 TaskBuildBundles 追加。
/// </summary>
public static class BundleNameBuilder
{
    private const string FallbackSegment = "default";

    #region Public Methods

    /// <summary>
    /// 组装标准化 Bundle 逻辑名：{packageName}_{groupName}_{packKey}（全小写）。
    /// </summary>
    public static string Build(string packageName, string groupName, string packKey)
    {
        string safePkg = SanitizeSegment(packageName);
        string safeGroup = SanitizeSegment(groupName);
        string safeKey = SanitizeSegment(packKey);
        return string.Concat(safePkg, "_", safeGroup, "_", safeKey);
    }

    #endregion

    #region Private Methods

    private static string SanitizeSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return FallbackSegment;

        StringBuilder sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c >= 'A' && c <= 'Z')
                c = (char)(c + 32); // ToLowerInvariant for ASCII
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }

        string result = sb.ToString();
        return result.Length > 0 ? result : FallbackSegment;
    }

    #endregion
}

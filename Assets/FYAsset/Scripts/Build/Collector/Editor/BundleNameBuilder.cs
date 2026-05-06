/// <summary>
/// Bundle 逻辑名组装工具 —— 将 PackRule 输出的分组键组装为标准化三段式名称。
/// 输出不含 Hash 和 .bundle 扩展名，这些由 TaskBuildBundles 追加。
/// </summary>
public static class BundleNameBuilder
{
    #region Public Methods

    /// <summary>
    /// 校验 PackageName / GroupName / Label 是否包含保留字符。返回 null 表示合法。
    /// </summary>
    public static string ValidateSegment(string segment)
    {
        return ValidateAgainst(segment, SystemIdentifiers.ReservedChars);
    }

    /// <summary>
    /// 校验 PackKey 是否包含保留字符（允许 ~ 标签连接符）。返回 null 表示合法。
    /// </summary>
    public static string ValidatePackKey(string packKey)
    {
        return ValidateAgainst(packKey, SystemIdentifiers.PackKeyReservedChars);
    }

    private static string ValidateAgainst(string value, char[] blacklist)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            for (int j = 0; j < blacklist.Length; j++)
            {
                if (c == blacklist[j])
                    return string.Concat("'", value, "' contains reserved character '", c, "' at position ", i.ToString());
            }
        }

        return null;
    }

    /// <summary>
    /// 组装标准化 Bundle 逻辑名：{package}_{group}_{packKey}（全小写）。
    /// </summary>
    public static string Build(string packageName, string groupName, string packKey)
    {
        string safePkg = SanitizeSegment(packageName);
        string safeGroup = SanitizeSegment(groupName);
        string safeKey = SanitizeSegment(packKey);
        return string.Concat(safePkg, SystemIdentifiers.SegmentSeparator, safeGroup, SystemIdentifiers.SegmentSeparator, safeKey);
    }

    #endregion

    #region Private Methods

    private static string SanitizeSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return SystemIdentifiers.DefaultPackKey;

        string lowered = raw.ToLowerInvariant();
        return lowered.Length > 0 ? lowered : SystemIdentifiers.DefaultPackKey;
    }

    #endregion
}

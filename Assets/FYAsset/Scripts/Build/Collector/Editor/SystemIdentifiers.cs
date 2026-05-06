/// <summary>
/// 系统保留标识符统一管理。
/// 所有系统生成的特殊名称（哨兵值、保留 Group 名等）以 "$" 为前缀，
/// 用户定义的 CollectorGroup/CollectPath/PackKey 等不得以 "$" 开头。
/// </summary>
public static class SystemIdentifiers
{
    /// <summary>系统保留标识符前缀</summary>
    public const string Prefix = "$";

    /// <summary>PackByLabel 空 Labels 的哨兵 packKey</summary>
    public const string OrphanPackKey = "$orphan";

    /// <summary>隐式依赖共享 Bundle 的保留 GroupName</summary>
    public const string SharedGroupName = "$shared";

    /// <summary>PackRule / BundleNameBuilder 的默认回退 packKey</summary>
    public const string DefaultPackKey = "default";

    /// <summary>Bundle 名顶层段分隔符（package_group_packKey）</summary>
    public const char SegmentSeparator = '_';

    /// <summary>PackByLabel 标签连接符</summary>
    public const char LabelSeparator = '~';

    /// <summary>段值中不允许出现的保留字符（PackageName / GroupName / Labels 使用）</summary>
    public static readonly char[] ReservedChars =
        { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '~', '$', '_' };

    /// <summary>PackKey 中不允许出现的保留字符（不含 ~，因为它是有意使用的标签连接符）</summary>
    public static readonly char[] PackKeyReservedChars =
        { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '$', '_' };

    /// <summary>检查给定值是否为系统保留标识符（以 $ 开头）</summary>
    public static bool IsSystemReserved(string value)
    {
        return value != null && value.StartsWith(Prefix);
    }
}

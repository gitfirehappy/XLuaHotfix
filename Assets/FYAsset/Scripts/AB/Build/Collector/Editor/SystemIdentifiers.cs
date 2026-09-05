/// <summary>
/// 系统保留标识符统一管理。
/// 所有系统生成的特殊名称（哨兵值、保留 Group 名等）以 "$" 为前缀，
/// 用户定义的 Group/Label/BundleKey 等不得以 "$" 开头。
/// </summary>
public static class SystemIdentifiers
{
    /// <summary>系统保留标识符前缀</summary>
    public const string Prefix = "$";

    /// <summary>PackTogetherByLabel 空 Labels 的系统哨兵 BundleKey</summary>
    public const string UnlabeledBundleKey = "$unlabeled";

    /// <summary>隐式依赖共享 Bundle 的保留 GroupName</summary>
    public const string SharedGroupName = "$shared";

    /// <summary>BundleNameBuilder 的默认回退 BundleKey</summary>
    public const string DefaultBundleKey = "default";

    /// <summary>Bundle 名顶层段分隔符</summary>
    public const char SegmentSeparator = '_';

    /// <summary>BundleKey 内部连接符</summary>
    public const char LabelSeparator = '~';

    /// <summary>段值中不允许出现的保留字符（PackageName / GroupName / Labels 使用）</summary>
    public static readonly char[] ReservedChars =
        { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '~', '$', '_', '#' };

    /// <summary>BundleKey 中不允许出现的保留字符（不含 ~，因为它是有意使用的连接符）</summary>
    public static readonly char[] BundleKeyReservedChars =
        { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '$', '_', '#' };

}

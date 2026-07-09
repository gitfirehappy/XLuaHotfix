using System;
using System.IO;

/// <summary>
/// Address 自动生成与覆写策略（仅编辑器/构建期使用）。
/// 
/// 规则：
/// 1. 自动 Address 由项目级 AssetAddressStyle 决定
/// 2. Address 允许重复；可解析性由 Address + PrimaryType + Labels 决定
/// 3. 自动项可重建；手动覆写项保持锁定，除非显式切回 Auto
/// </summary>
public static class AssetAddressGenerator
{
    /// <summary>
    /// 类型后缀分隔符。
    /// 显式 NameType 格式：{Filename}#{Type}，从后向前解析最后一段为类型后缀。
    /// </summary>
    private const char TypeSuffixSeparator = '#';

    /// <summary>
    /// 从资源路径生成默认短名（文件名去扩展）。
    /// </summary>
    private static string GenerateShortName(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            throw new ArgumentException("Asset path 不能为 null 或空。", nameof(assetPath));

        return Path.GetFileNameWithoutExtension(assetPath);
    }

    /// <summary>
    /// 生成带类型后缀的升级 Address。
    /// 格式：{shortName}_{primaryType}
    /// 示例：player-idle -> player-idle_Sprite
    /// </summary>
    private static string GenerateTypeSuffixAddress(string shortName, string primaryType)
    {
        if (string.IsNullOrEmpty(shortName))
            throw new ArgumentException("Short name 不能为 null 或空。", nameof(shortName));
        if (string.IsNullOrEmpty(primaryType))
            throw new ArgumentException("Primary type 不能为 null 或空。", nameof(primaryType));

        return string.Concat(shortName, TypeSuffixSeparator, primaryType);
    }

    /// <summary>
    /// 按项目级样式生成单个资源 Address。
    /// </summary>
    public static string GenerateAddress(string assetPath, string primaryType, AssetAddressStyle style)
    {
        switch (style)
        {
            case AssetAddressStyle.LongAssetPathWithoutExtension:
                return GenerateLongAssetPath(assetPath);
            case AssetAddressStyle.NameType:
                return GenerateNameTypeAddress(assetPath, primaryType);
            case AssetAddressStyle.ShortName:
            default:
                return GenerateShortName(assetPath);
        }
    }

    /// <summary>
    /// 生成 Unity 资产路径形式的 Address，保留 Assets/... 前缀和文件扩展名。
    /// </summary>
    private static string GenerateLongAssetPath(string assetPath)
    {
        string normalized = FYAssetPathUtility.NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Asset path 不能为 null 或空。", nameof(assetPath));

        return normalized;
    }

    /// <summary>
    /// 生成显式 Name#Type Address。
    /// </summary>
    private static string GenerateNameTypeAddress(string assetPath, string primaryType)
    {
        return GenerateTypeSuffixAddress(GenerateShortName(assetPath), primaryType);
    }

}

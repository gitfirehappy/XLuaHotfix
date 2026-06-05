using System;
using System.Collections.Generic;
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
    public const char TypeSuffixSeparator = '#';

    /// <summary>
    /// 从资源路径生成默认短名（文件名去扩展）。
    /// </summary>
    public static string GenerateShortName(string assetPath)
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
    public static string GenerateTypeSuffixAddress(string shortName, string primaryType)
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
    /// 生成 Unity 资产路径形式的 Address，保留 Assets/... 前缀并去掉扩展名。
    /// </summary>
    public static string GenerateLongAssetPath(string assetPath)
    {
        string normalized = FYAssetPathUtility.NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Asset path 不能为 null 或空。", nameof(assetPath));

        string extension = Path.GetExtension(normalized);
        return string.IsNullOrEmpty(extension)
            ? normalized
            : normalized.Substring(0, normalized.Length - extension.Length);
    }

    /// <summary>
    /// 生成显式 Name#Type Address。
    /// </summary>
    public static string GenerateNameTypeAddress(string assetPath, string primaryType)
    {
        return GenerateTypeSuffixAddress(GenerateShortName(assetPath), primaryType);
    }

    /// <summary>
    /// 生成单个资源的默认 Address。
    /// 兼容旧调用：false 表示 ShortName，true 表示显式 Name#Type。
    /// </summary>
    public static string GenerateShortAddress(string assetPath, string primaryType, bool useTypeSuffix = false)
    {
        return useTypeSuffix
            ? GenerateNameTypeAddress(assetPath, primaryType)
            : GenerateShortName(assetPath);
    }

    /// <summary>
    /// 从升级后的 Address 中解析出原始短名和类型后缀。
    /// 返回 (shortName, typeSuffix)；如果没有后缀，typeSuffix 为 null。
    /// </summary>
    public static (string shortName, string typeSuffix) ParseTypeSuffixAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
            return (address, null);

        int lastSep = address.LastIndexOf(TypeSuffixSeparator);
        if (lastSep <= 0 || lastSep >= address.Length - 1)
            return (address, null);

        return (address.Substring(0, lastSep), address.Substring(lastSep + 1));
    }

    /// <summary>
    /// 为一组条目批量生成自动 Address。
    /// 仅修改 AutoAddress = true 的条目；手动覆写项保持不变，不做冲突驱动重写。
    /// </summary>
    public static void GenerateAddresses(IList<RuntimeAssetEntry> entries, AssetAddressStyle style)
    {
        foreach (var entry in entries)
        {
            if (!entry.AutoAddress)
                continue;

            entry.Address = GenerateAddress(entry.SourcePath, entry.PrimaryType, style);
        }
    }

    /// <summary>
    /// 兼容旧批量入口；默认使用短名。
    /// </summary>
    public static void GenerateAddresses(IList<RuntimeAssetEntry> entries)
    {
        GenerateAddresses(entries, AssetAddressStyle.ShortName);
    }
}

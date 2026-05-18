using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Address 自动生成与覆写策略（仅编辑器/构建期使用）。
/// 
/// 规则：
/// 1. 自动短名 = 文件名去扩展（如 "Player.prefab" → "Player"）
/// 2. 短名冲突时升级为 Filename_Type 格式（如 "Player_Prefab"）
/// 3. 自动项可重建；手动覆写项保持锁定，除非显式切回 Auto
/// </summary>
public static class AssetAddressGenerator
{
    /// <summary>
    /// 类型后缀分隔符。
    /// 升级格式：{Filename}_{Type}，从后向前解析最后一段为类型后缀。
    /// </summary>
    public const char TypeSuffixSeparator = '_';

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
    /// 示例：player-idle → player-idle_Sprite
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
    /// 生成单个资源的默认 Address。
    /// 约定：先取文件短名；仅当调用方确认存在同名冲突时，再升级为 Filename_Type 形式。
    /// AddressByFileName 复用这个入口，保持地址命名规则统一。
    /// </summary>
    public static string GenerateShortAddress(string assetPath, string primaryType, bool useTypeSuffix = false)
    {
        string shortName = GenerateShortName(assetPath);
        if (!useTypeSuffix)
            return shortName;

        return GenerateTypeSuffixAddress(shortName, primaryType);
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
    /// 为一组条目批量生成自动 Address，处理冲突升级。
    /// 仅修改 AutoAddress = true 的条目；手动覆写项保持不变。
    /// </summary>
    public static void GenerateAddresses(IList<RuntimeAssetEntry> entries)
    {
        // 第一轮：为所有自动条目生成短名
        foreach (var entry in entries)
        {
            if (!entry.AutoAddress) continue;
            entry.Address = GenerateShortName(entry.SourcePath);
        }

        // 第二轮：检测短名冲突，升级为 Filename_Type
        var addressGroups = entries
            .Where(e => e.AutoAddress)
            .GroupBy(e => e.Address, StringComparer.OrdinalIgnoreCase);

        foreach (var group in addressGroups)
        {
            var items = group.ToList();
            if (items.Count <= 1) continue;

            // 同名但不同 PrimaryType → 全部升级为 Filename_Type
            var distinctTypes = items.Select(e => e.PrimaryType).Distinct().ToList();
            if (distinctTypes.Count > 1)
            {
                foreach (var entry in items)
                {
                    entry.Address = GenerateTypeSuffixAddress(
                        GenerateShortName(entry.SourcePath),
                        entry.PrimaryType
                    );
                }
            }
            // 同名且同 PrimaryType → 不自动升级
            // （由 AssetConflictRules 校验工具报告警告）
        }
    }
}

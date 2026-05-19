using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 核心解析逻辑：将查询参数映射到唯一 RuntimeAssetEntry。
///
/// 所有方法均为静态，操作 IAssetIndex。
/// 运行时热路径 — 不使用 LINQ，全部使用 for 循环和手动过滤。
/// </summary>
public static class AssetResolver
{
    #region ByAddress

    /// <summary>
    /// 通过 Address + 请求类型 T 解析。
    /// 默认使用可赋值类型匹配（T 可从 PrimaryType 赋值）。
    /// </summary>
    public static ResolveResult ResolveByAddress<T>(IAssetIndex index, string address) where T : UnityEngine.Object
    {
        return ResolveByAddressInternal(index, address, typeof(T).Name, exactType: false);
    }

    /// <summary>
    /// 通过 Address + 精确类型匹配解析。
    /// </summary>
    public static ResolveResult ResolveByAddressExact<T>(IAssetIndex index, string address) where T : UnityEngine.Object
    {
        return ResolveByAddressInternal(index, address, typeof(T).Name, exactType: true);
    }

    private static ResolveResult ResolveByAddressInternal(
        IAssetIndex index, string address, string requestedType, bool exactType)
    {
        IReadOnlyList<RuntimeAssetEntry> entries;
        try
        {
            entries = index.GetEntriesByAddress(address);
        }
        catch (NotSupportedException)
        {
            return ResolveResult.IndexNotSupported(index.GetType().Name);
        }

        if (entries == null || entries.Count == 0)
            return ResolveResult.NotFound(string.Concat("Address='", address, "'"));

        // 按类型过滤
        var matched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsTypeMatch(entries[i].PrimaryType, requestedType, exactType))
            {
                matched.Add(entries[i]);
            }
        }

        if (matched.Count == 0)
            return ResolveResult.TypeMismatch(
                string.Concat("Address='", address, "'"), requestedType, entries[0].PrimaryType);

        if (matched.Count == 1)
            return ResolveResult.Hit(matched[0]);

        // 多条匹配 — 冲突
        return ResolveResult.Conflict(
            string.Concat("Address='", address, "', Type='", requestedType, "'"), matched);
    }

    #endregion

    #region ByTypeKey

    /// <summary>
    /// 通过 PrimaryType + Key + 可选 Labels 解析。
    /// 不传 Labels 时，多条匹配直接报错。
    /// </summary>
    public static ResolveResult ResolveByTypeKey<T>(
        IAssetIndex index, string key, IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        string requestedType = typeof(T).Name;

        IReadOnlyList<RuntimeAssetEntry> entries;
        try
        {
            entries = index.GetEntriesByAddressAndType(key, requestedType);
        }
        catch (NotSupportedException)
        {
            return ResolveResult.IndexNotSupported(index.GetType().Name);
        }

        if (entries == null || entries.Count == 0)
            return ResolveResult.NotFound(
                string.Concat("TypeKey: Type='", requestedType, "', Key='", key, "'"));

        if (entries.Count == 1)
            return ResolveResult.Hit(entries[0]);

        // 多条匹配 — 尝试 Labels 消歧
        if (labels == null || labels.Count == 0)
            return ResolveResult.Conflict(
                string.Concat("TypeKey: Type='", requestedType, "', Key='", key, "' (未提供 Labels)"),
                entries);

        var filtered = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasAllLabels(labels))
            {
                filtered.Add(entries[i]);
            }
        }

        if (filtered.Count == 0)
            return ResolveResult.NotFound(
                string.Concat("TypeKey: Type='", requestedType, "', Key='", key,
                    "', Labels=[", JoinStrings(labels), "]"));

        if (filtered.Count == 1)
            return ResolveResult.Hit(filtered[0]);

        return ResolveResult.Conflict(
            string.Concat("TypeKey: Type='", requestedType, "', Key='", key,
                "', Labels=[", JoinStrings(labels), "]"),
            filtered);
    }

    #endregion

    #region 批量解析

    /// <summary>
    /// 批量解析多个 Address。每个 Address 返回一个 ResolveResult。
    /// </summary>
    public static List<ResolveResult> ResolveMany<T>(
        IAssetIndex index, IReadOnlyList<string> addresses) where T : UnityEngine.Object
    {
        var results = new List<ResolveResult>(addresses.Count);
        for (int i = 0; i < addresses.Count; i++)
        {
            results.Add(ResolveByAddress<T>(index, addresses[i]));
        }

        return results;
    }

    /// <summary>
    /// 解析所有匹配给定 Labels 的条目（AND 逻辑 — 条目必须包含所有 Labels）。
    /// </summary>
    public static List<RuntimeAssetEntry> ResolveByLabels<T>(
        IAssetIndex index, IReadOnlyList<string> labels) where T : UnityEngine.Object
    {
        string requestedType = typeof(T).Name;

        IReadOnlyList<RuntimeAssetEntry> allEntries;
        try
        {
            allEntries = index.GetAllEntries();
        }
        catch (NotSupportedException)
        {
            Debug.LogWarning(string.Concat(
                "[AssetResolver] 索引 ", index.GetType().Name, " 不支持 GetAllEntries。"));
            return new List<RuntimeAssetEntry>();
        }

        var matched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < allEntries.Count; i++)
        {
            var entry = allEntries[i];
            if (!IsTypeMatch(entry.PrimaryType, requestedType, exactType: false))
                continue;
            if (!entry.HasAllLabels(labels))
                continue;
            matched.Add(entry);
        }

        return matched;
    }

    #endregion

    #region 类型匹配

    /// <summary>
    /// 字符串优先的热路径类型匹配。
    /// Exact：PrimaryType 必须等于 requestedType（不区分大小写）。
    /// 非 Exact：同名匹配，或 requestedType 为 "Object"。
    /// 完整 assignable 判断需解析 System.Type；如后续需要，应在字符串快路径后追加缓存化 Type 匹配。
    /// </summary>
    private static bool IsTypeMatch(string primaryType, string requestedType, bool exactType)
    {
        if (string.IsNullOrEmpty(primaryType) || string.IsNullOrEmpty(requestedType))
            return false;

        if (string.Equals(primaryType, requestedType, StringComparison.OrdinalIgnoreCase))
            return true;

        if (exactType)
            return false;

        // 可赋值兜底：请求 UnityEngine.Object 时匹配所有类型
        if (string.Equals(requestedType, "Object", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 手动拼接字符串列表（避免运行时 LINQ / string.Join 分配）。
    /// </summary>
    private static string JoinStrings(IReadOnlyList<string> items)
    {
        if (items == null || items.Count == 0) return "";
        if (items.Count == 1) return items[0] ?? "";

        var sb = new System.Text.StringBuilder();
        sb.Append(items[0] ?? "");
        for (int i = 1; i < items.Count; i++)
        {
            sb.Append(',');
            sb.Append(items[i] ?? "");
        }

        return sb.ToString();
    }

    #endregion
}
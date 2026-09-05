using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 AB 查询参数解析为唯一 RuntimeAssetEntry（Address / Type / Label）。
/// </summary>
public static class AssetResolver
{
    #region ByAddress

    public static ResolveResult ResolveByAddress<T>(ABAssetIndex index, string address)
        where T : UnityEngine.Object
    {
        IReadOnlyList<RuntimeAssetEntry> entries = index.GetEntriesByAddress(address);
        if (entries == null || entries.Count == 0)
            return ResolveResult.NotFound(string.Concat("Address='", address, "'"));

        string requestedType = typeof(T).Name;
        var exactMatches = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].PrimaryType, requestedType, StringComparison.OrdinalIgnoreCase))
                exactMatches.Add(entries[i]);
        }

        if (exactMatches.Count == 1)
            return ResolveResult.Hit(exactMatches[0]);
        if (exactMatches.Count > 1)
            return ResolveResult.Conflict(
                string.Concat("Address='", address, "', Type='", requestedType, "'"), exactMatches);

        if (entries.Count > 1)
            return ResolveResult.Conflict(
                string.Concat("Address='", address, "', Type='", requestedType, "'"), entries);

        if (typeof(T) == typeof(UnityEngine.Object) || typeof(T) == typeof(ScriptableObject))
            return ResolveResult.Hit(entries[0]);

        return ResolveResult.TypeMismatch(
            string.Concat("Address='", address, "'"), requestedType, entries[0].PrimaryType);
    }

    #endregion

    #region ByTypeKey

    public static ResolveResult ResolveByTypeKey<T>(
        ABAssetIndex index,
        string key,
        IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        string requestedType = typeof(T).Name;
        IReadOnlyList<RuntimeAssetEntry> entries = index.GetEntriesByAddressAndType(key, requestedType);

        if (entries == null || entries.Count == 0)
            return ResolveResult.NotFound(
                string.Concat("TypeKey: Type='", requestedType, "', Key='", key, "'"));

        if (entries.Count == 1)
            return ResolveResult.Hit(entries[0]);

        if (labels == null || labels.Count == 0)
            return ResolveResult.Conflict(
                string.Concat("TypeKey: Type='", requestedType, "', Key='", key, "' (未提供 Labels)"),
                entries);

        var filtered = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasAllLabels(labels))
                filtered.Add(entries[i]);
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

    #region RawFile

    public static ResolveResult ResolveRawByAddress(
        ABAssetIndex index,
        string address,
        IReadOnlyList<string> labels = null)
    {
        IReadOnlyList<RuntimeAssetEntry> entries = index.GetEntriesByAddress(address);
        if (entries == null || entries.Count == 0)
            return ResolveResult.NotFound(string.Concat("RawFile Address='", address, "'"));

        var labelMatched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasAllLabels(labels))
                labelMatched.Add(entries[i]);
        }

        if (labelMatched.Count == 0)
            return ResolveResult.NotFound(
                string.Concat("RawFile Address='", address, "', Labels=[", JoinStrings(labels), "]"));

        var rawMatched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < labelMatched.Count; i++)
        {
            if (labelMatched[i].PayloadKind == EPayloadKind.RawFile)
                rawMatched.Add(labelMatched[i]);
        }

        if (rawMatched.Count == 0)
            return ResolveResult.InvalidPayloadKind(
                string.Concat("RawFile Address='", address, "'"),
                EPayloadKind.RawFile,
                labelMatched[0].PayloadKind);

        if (rawMatched.Count == 1)
            return ResolveResult.Hit(rawMatched[0]);

        return ResolveResult.Conflict(
            string.Concat("RawFile Address='", address, "', Labels=[", JoinStrings(labels), "]"),
            rawMatched);
    }

    #endregion

    #region Batch Queries

    public static List<ResolveResult> ResolveMany<T>(
        ABAssetIndex index,
        IReadOnlyList<string> addresses) where T : UnityEngine.Object
    {
        var results = new List<ResolveResult>(addresses.Count);
        for (int i = 0; i < addresses.Count; i++)
            results.Add(ResolveByAddress<T>(index, addresses[i]));
        return results;
    }

    public static List<RuntimeAssetEntry> ResolveByLabels<T>(
        ABAssetIndex index,
        IReadOnlyList<string> labels) where T : UnityEngine.Object
    {
        string requestedType = typeof(T).Name;
        IReadOnlyList<RuntimeAssetEntry> allEntries = index.GetAllEntries();
        var matched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < allEntries.Count; i++)
        {
            var entry = allEntries[i];
            if (!string.Equals(entry.PrimaryType, requestedType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.HasAllLabels(labels))
                continue;
            matched.Add(entry);
        }

        return matched;
    }

    #endregion

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
}

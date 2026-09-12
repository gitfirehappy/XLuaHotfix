using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 AB 公共 Address 解析为唯一条目，并按内容类型校验请求形态。
/// </summary>
/// <remarks>
/// 公共 Address 在单包内唯一且大小写不敏感，因此这里不做 Type 消歧，也没有 Object 回退分支；
/// 请求类型与条目 PrimaryType 的匹配在加载阶段由 Bundle 提取结果体现。
/// </remarks>
public static class AssetResolver
{

    /// <summary>
    /// 解析公共 Address 指向的唯一 SerializedObject 条目。
    /// </summary>
    public static ResolveResult ResolveByAddress(ABAssetIndex index, string address)
    {
        ResolveResult result = ResolveEntry(index, address, "Address");
        if (!result.IsSuccess) return result;

        RuntimeAssetEntry entry = result.Entry;
        if (entry.ContentType == AssetContentType.SerializedObject)
            return result;

        return ResolveResult.InvalidPayloadKind(
            string.Concat("Address='", address, "'"),
            AssetContentType.SerializedObject,
            entry.ContentType);
    }

    /// <summary>
    /// 解析公共 Address 指向的唯一 RawFile 条目；内容类型不是 RawFile 时返回结构化错误。
    /// </summary>
    public static ResolveResult ResolveRawByAddress(ABAssetIndex index, string address)
    {
        ResolveResult result = ResolveEntry(index, address, "RawFile Address");
        if (!result.IsSuccess) return result;

        RuntimeAssetEntry entry = result.Entry;
        if (entry.ContentType == AssetContentType.RawFile)
            return result;

        return ResolveResult.InvalidPayloadKind(
            string.Concat("RawFile Address='", address, "'"),
            AssetContentType.RawFile,
            entry.ContentType);
    }

    /// <summary>
    /// 解析公共 Address 指向的唯一 Scene 条目；内容类型不是 Scene 时返回结构化错误。
    /// </summary>
    public static ResolveResult ResolveSceneByAddress(ABAssetIndex index, string address)
    {
        ResolveResult result = ResolveEntry(index, address, "Scene Address");
        if (!result.IsSuccess) return result;

        RuntimeAssetEntry entry = result.Entry;
        if (entry.ContentType == AssetContentType.Scene)
            return result;

        return ResolveResult.InvalidPayloadKind(
            string.Concat("Scene Address='", address, "'"),
            AssetContentType.Scene,
            entry.ContentType);
    }

    /// <summary>
    /// 批量解析公共 Address，逐项返回成功或失败，不做整体失败。
    /// </summary>
    public static List<ResolveResult> ResolveMany(ABAssetIndex index, IReadOnlyList<string> addresses)
    {
        if (addresses == null) return new List<ResolveResult>(0);

        var results = new List<ResolveResult>(addresses.Count);
        for (int i = 0; i < addresses.Count; i++)
            results.Add(ResolveByAddress(index, addresses[i]));
        return results;
    }

    /// <summary>
    /// 解析公共 Address。索引不可用、Address 为空或未命中都返回结构化失败。
    /// </summary>
    private static ResolveResult ResolveEntry(ABAssetIndex index, string address, string queryLabel)
    {
        if (index == null)
            return ResolveResult.NotFound(string.Concat(queryLabel, "='", address ?? "", "' (索引缺失)"));

        if (index.BuildError != null)
            return ResolveResult.NotFound(string.Concat(
                queryLabel, "='", address ?? "", "' (索引不可用: ", index.BuildError.Message, ")"));

        RuntimeAssetEntry entry = index.GetEntryByAddress(address);
        if (entry == null)
            return ResolveResult.NotFound(string.Concat(queryLabel, "='", address ?? "", "'"));

        return ResolveResult.Hit(entry);
    }
}

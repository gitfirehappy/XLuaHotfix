using System.Collections.Generic;

/// <summary>将公共 Address 解析为唯一 ManifestAssetEntry，并校验内容类型。</summary>
public static class AssetResolver
{
    public static ResolveResult ResolveByAddress(ABAssetIndex index, string address)
        => ResolveContentType(index, address, AssetContentType.SerializedObject, "Address");

    public static ResolveResult ResolveRawByAddress(ABAssetIndex index, string address)
        => ResolveContentType(index, address, AssetContentType.RawFile, "RawFile Address");

    public static ResolveResult ResolveSceneByAddress(ABAssetIndex index, string address)
        => ResolveContentType(index, address, AssetContentType.Scene, "Scene Address");

    public static List<ResolveResult> ResolveMany(ABAssetIndex index, IReadOnlyList<string> addresses)
    {
        var results = new List<ResolveResult>(addresses?.Count ?? 0);
        if (addresses == null)
            return results;
        for (int i = 0; i < addresses.Count; i++)
            results.Add(ResolveByAddress(index, addresses[i]));
        return results;
    }

    private static ResolveResult ResolveContentType(
        ABAssetIndex index,
        string address,
        AssetContentType expected,
        string queryLabel)
    {
        if (index == null || index.BuildError != null)
            return ResolveResult.NotFound(string.Concat(queryLabel, "='", address ?? string.Empty, "'"));

        ManifestAssetEntry entry = index.GetEntryByAddress(address);
        if (entry == null)
            return ResolveResult.NotFound(string.Concat(queryLabel, "='", address ?? string.Empty, "'"));

        ManifestContentEntry content = index.GetManifest().GetContentForAsset(entry);
        if (content == null)
            return ResolveResult.NotFound(string.Concat(queryLabel, "='", address ?? string.Empty, "' (Content missing)"));
        if (content.ContentType != expected)
            return ResolveResult.InvalidPayloadKind(
                string.Concat(queryLabel, "='", address, "'"), expected, content.ContentType);

        return ResolveResult.Hit(entry);
    }
}

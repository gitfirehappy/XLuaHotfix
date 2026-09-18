using System;
using System.Collections.Generic;
using System.Text;

/// <summary>验证依赖分析完成后的 Content 成员是否合法，保证物理构建只接收完整且一致的输入。</summary>
internal static class ABContentAnalysisValidator
{
    public static BuildTaskResult Validate(IReadOnlyList<CollectedAssetInfo> assets)
    {
        if (assets == null || assets.Count == 0)
            return BuildTaskResult.Ok();

        var groups = new Dictionary<string, List<CollectedAssetInfo>>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            string contentName = asset?.ContentName;
            if (string.IsNullOrEmpty(contentName))
                continue;

            if (!groups.TryGetValue(contentName, out List<CollectedAssetInfo> members))
            {
                members = new List<CollectedAssetInfo>();
                groups.Add(contentName, members);
            }
            members.Add(asset);
        }

        foreach (KeyValuePair<string, List<CollectedAssetInfo>> group in groups)
        {
            BuildTaskResult result = ValidateGroup(group.Key, group.Value);
            if (!result.Success)
                return result;
        }

        return BuildTaskResult.Ok();
    }

    private static BuildTaskResult ValidateGroup(string contentName, List<CollectedAssetInfo> assets)
    {
        AssetContentType contentType = assets[0].ContentType;
        string assetType = assets[0].AssetType ?? string.Empty;
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            if (asset.ContentType != contentType)
            {
                return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle,
                    $"Content '{contentName}' 混入了多种内容类型（AssetContentType）。每个 Content 只能使用一种构建路线。", true);
            }

            if (!string.Equals(asset.AssetType ?? string.Empty, assetType, StringComparison.OrdinalIgnoreCase))
            {
                var members = new StringBuilder();
                for (int j = 0; j < assets.Count; j++)
                {
                    members.Append("\n  - ").Append(assets[j].AssetPath)
                        .Append(" [AssetType=").Append(assets[j].AssetType ?? string.Empty)
                        .Append(", Address=").Append(assets[j].Address ?? string.Empty).Append(']');
                }
                return BuildTaskResult.Fail(BuildErrorCodes.MixedAssetTypeBundle,
                    $"Content '{contentName}' 混入了多种 AssetType。每个 Content 必须按精确主类型分桶。成员:{members}", true);
            }

            if (asset.ContentType == AssetContentType.SerializedObject &&
                !AssetClassifier.CanUseAsSerializedBundleEntry(asset.AssetPath, out string reason))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.InvalidBundleEntryAsset,
                    $"Asset '{asset.AssetPath}' 不能作为 AssetBundle Serialized 入口资产: {reason}", true);
            }
        }

        if (contentType == AssetContentType.Scene && assets.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle,
                $"Content '{contentName}' 包含 {assets.Count} 个 Scene；每个 Scene 必须对应一个独立 Content 文件。", true);
        }

        if (contentType == AssetContentType.RawFile && assets.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.RawfileMultiAsset,
                $"Content '{contentName}' 包含 {assets.Count} 个 RawFile，每个 RawFile 必须独立输出。示例 Asset: '{assets[0].AssetPath}'。", true);
        }

        return BuildTaskResult.Ok();
    }
}

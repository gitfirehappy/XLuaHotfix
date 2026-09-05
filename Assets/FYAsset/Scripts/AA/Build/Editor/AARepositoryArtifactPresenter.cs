#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;

/// <summary>
/// AA artifact 展示面实现：把稳定资产 GUID 解析为 Addressables Address/AssetPath。
/// 由 AABuildPipelineWindow 注入共享 Repository 面板；Shared 面板不感知 Addressables。
/// </summary>
public sealed class AARepositoryArtifactPresenter : IRepositoryArtifactPresenter
{
    public RepositoryArtifactPresentation Present(string artifactIdentity)
    {
        string guid = artifactIdentity ?? string.Empty;
        string assetPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings != null && !string.IsNullOrEmpty(guid) ? settings.FindAssetEntry(guid) : null;
        string address = entry != null ? entry.address ?? string.Empty : string.Empty;
        bool isResolved = !string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(assetPath);

        string safeGuid = string.IsNullOrEmpty(guid) ? "-" : guid;
        string displayName;
        if (isResolved)
            displayName = $"{address} | {assetPath}";
        else if (!string.IsNullOrEmpty(address))
            displayName = $"Unresolved AA asset: {address} (GUID: {safeGuid})";
        else if (!string.IsNullOrEmpty(assetPath))
            displayName = $"Unresolved Addressables entry: {assetPath} (GUID: {safeGuid})";
        else
            displayName = $"Unresolved AA asset (GUID: {safeGuid})";

        return new RepositoryArtifactPresentation
        {
            DisplayName = displayName,
            Details = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Address", string.IsNullOrEmpty(address) ? "-" : address),
                new KeyValuePair<string, string>("Asset Path", string.IsNullOrEmpty(assetPath) ? "-" : assetPath),
                new KeyValuePair<string, string>("GUID", safeGuid)
            },
            UnresolvedWarning = isResolved
                ? null
                : "Addressable asset cannot be fully resolved from the persisted GUID."
        };
    }
}
#endif

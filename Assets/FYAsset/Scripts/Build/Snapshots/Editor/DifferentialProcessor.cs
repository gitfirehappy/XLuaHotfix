#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 旧 AA hotfix 路径的 Snapshot 状态机。
/// Diff 计算委托给 ArtifactDiffer，group 迁移委托给 LegacyAddressableHotfixGroups。
/// TODO: 后续迁移进DAG管线，构建Task封装+diff方法调用
/// </summary>
public static class DifferentialProcessor
{
    public static bool PrepareHotfix(VersionNumber currentVersion)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[DiffProcessor] AddressableAssetSettings is null.");
            return false;
        }

        var head = BuildRepositoryFacade.GetHeadCommit(currentVersion, BackendMode.AA);
        if (head == null)
        {
            Debug.LogError("[DiffProcessor] No repository HEAD found. Build a full AA package before building a hotfix.");
            return false;
        }

        var scanner = new AddressableSourceArtifactScanner(settings);
        var current = scanner.Scan();
        var baseline = head.Artifacts ?? new System.Collections.Generic.List<ArtifactDigest>();
        var delta = ArtifactDiffer.Diff(baseline, current);

        LogDelta(delta);
        if (delta.IsEmpty)
        {
            Debug.Log("[DiffProcessor] No artifact changes detected.");
            return false;
        }

        // 只有 AA legacy 路径需要移动 Addressables group；Diff 模块本身保持无副作用。
        if (!LegacyAddressableHotfixGroups.Apply(delta))
            return false;

        Debug.Log(
            $"[DiffProcessor] Hotfix diff prepared. Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}.");
        return true;
    }

    public static void RestoreOriginalGroups()
    {
        LegacyAddressableHotfixGroups.Restore();
    }

    public static void ConfirmRelease()
    {
        Debug.LogWarning("[DiffProcessor] ConfirmRelease is a placeholder. Build success commits repository HEAD; release/push is deferred.");
    }

    public static void ReBuildSnapShots(VersionNumber version)
    {
        Debug.LogWarning("[DiffProcessor] ReBuildSnapShots is deprecated. BuildProjectManager commits repository HEAD after successful build.");
    }

    private static void LogDelta(ArtifactDelta delta)
    {
        for (int i = 0; i < delta.Added.Count; i++)
            Debug.Log($"[DiffProcessor] Artifact added: {delta.Added[i].Name}");
        for (int i = 0; i < delta.Modified.Count; i++)
            Debug.Log($"[DiffProcessor] Artifact modified: {delta.Modified[i].Name}");
        for (int i = 0; i < delta.Removed.Count; i++)
            Debug.Log($"[DiffProcessor] Artifact removed: {delta.Removed[i]}");
    }
}
#endif

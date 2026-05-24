#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// AA hotfix source diff helper。
/// 只负责扫描当前 Addressables source 并和 Repository HEAD 比较，不执行 group 迁移。
/// </summary>
public static class DifferentialProcessor
{
    public static ArtifactDelta ScanAddressableHotfixDiff(VersionNumber currentVersion)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new System.InvalidOperationException("AddressableAssetSettings is null.");

        var head = BuildRepositoryFacade.GetHeadCommit(currentVersion, BackendMode.AA);
        if (head == null)
            throw new System.InvalidOperationException("No repository HEAD found. Build a full AA package before building a hotfix.");

        var scanner = new AddressableSourceArtifactScanner(settings);
        var current = scanner.Scan();
        var baseline = head.Artifacts ?? new System.Collections.Generic.List<ArtifactDigest>();
        var delta = ArtifactDiffer.Diff(baseline, current);

        LogDelta(delta);

        Debug.Log(
            $"[DiffProcessor] Hotfix diff scanned. Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}.");
        return delta;
    }

    public static void RestoreOriginalGroups()
    {
        TaskMoveAddressableHotfixGroups.Restore();
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

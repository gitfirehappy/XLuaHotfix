using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// AA Hotfix 的 source diff Task。
/// 只扫描 Addressables source 并写入 ArtifactDelta；真正移动 Hotfix Group 由后续 Task 负责。
/// </summary>
public class TaskScanAAHotfixDiff : IBuildTask
{
    public string TaskName => "TaskScanAAHotfixDiff";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);
        bool repositoryPreviewMode = ctx.Get<bool>(BuildContextKeys.RepositoryPreviewMode);

        if (buildType != BuildType.Hotfix)
        {
            var current = ScanCurrentArtifacts();
            ctx.Set(BuildContextKeys.RepositoryArtifacts, current);
            ctx.Set(BuildContextKeys.ArtifactDelta, new ArtifactDelta());
            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] Full build 不需要计算 Hotfix diff，已记录当前 Artifact 快照: {current.Count}");
            return BuildTaskResult.Ok(new List<string> { "[AA DIFF] Full build skipped, current artifacts recorded" });
        }

        try
        {
            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] 开始 AA Hotfix diff scan，对比当前 Addressables source 与 Repository HEAD。");
            var current = ScanCurrentArtifacts();
            ctx.Set(BuildContextKeys.RepositoryArtifacts, current);

            ArtifactDelta delta = ScanDiff(request, current, repositoryPreviewMode);
            ctx.Set(BuildContextKeys.ArtifactDelta, delta ?? new ArtifactDelta());

            if (delta == null || delta.IsEmpty)
            {
                Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] 未发现 Artifact 变化，Hotfix build 继续执行；后续 Group move 会自然跳过。");
                return BuildTaskResult.Ok(new List<string> { "[AA DIFF] No changes, continue build" });
            }

            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] Diff 完成: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AA DIFF] Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}"
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[{nameof(TaskScanAAHotfixDiff)}] AA Hotfix diff scan 失败: {ex}");
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, $"AA Hotfix diff scan failed: {ex.Message}", true);
        }
    }

    public static List<ArtifactDigest> ScanCurrentArtifacts()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new System.InvalidOperationException("AddressableAssetSettings is null.");
        return ScanAddressableSource(settings);
    }

    private static ArtifactDelta ScanDiff(BuildPackageRequest request, List<ArtifactDigest> current, bool repositoryPreviewMode)
    {
        var delta = ArtifactDiffer.Diff(GetBaselineArtifacts(request, repositoryPreviewMode), current);
        LogDelta(delta);
        return delta;
    }

    private static List<ArtifactDigest> GetBaselineArtifacts(BuildPackageRequest request, bool repositoryPreviewMode)
    {
        var channelKey = BuildBaselineStore.GetChannelKey(request.Version, request.BackendMode);
        BuildBaseline baseline = BuildBaselineStore.LoadLatest(channelKey);
        return baseline?.Artifacts ?? new List<ArtifactDigest>();
    }

    private static List<ArtifactDigest> ScanAddressableSource(AddressableAssetSettings settings)
    {
        var result = new List<ArtifactDigest>();
        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;
            if (group.Name == "Built In Data" || group.HasSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.PlayerDataGroupSchema>())
                continue;

            foreach (var entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.guid))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(assetPath) || !FileHelper.Exists(assetPath))
                {
                    Debug.LogWarning($"[{nameof(TaskScanAAHotfixDiff)}] Addressables entry 指向的 Asset 文件不存在，已跳过。GUID={entry.guid}, Path={assetPath}");
                    continue;
                }

                string metaPath = assetPath + ".meta";
                long size = GetFileSize(assetPath) + GetFileSize(metaPath);
                result.Add(new ArtifactDigest
                {
                    Name = entry.guid,
                    Hash = HashGenerator.GenerateCompositeFileHash(assetPath, metaPath),
                    CRC = HashGenerator.GenerateCompositeFileCRC(assetPath, metaPath),
                    Size = size
                });
            }
        }
        return result;
    }

    private static long GetFileSize(string path)
    {
        if (string.IsNullOrEmpty(path) || !FileHelper.Exists(path))
            return 0;
        return new System.IO.FileInfo(path).Length;
    }

    private static void LogDelta(ArtifactDelta delta)
    {
        for (int i = 0; i < delta.Added.Count; i++)
            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] Artifact 新增：{delta.Added[i].Name}");
        for (int i = 0; i < delta.Modified.Count; i++)
            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] Artifact 已修改：{delta.Modified[i].Name}");
        for (int i = 0; i < delta.Removed.Count; i++)
            Debug.Log($"[{nameof(TaskScanAAHotfixDiff)}] Artifact 已移除：{delta.Removed[i]}");
    }
}

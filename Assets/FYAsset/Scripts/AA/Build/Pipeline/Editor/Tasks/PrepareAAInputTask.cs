using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA 构建管线：准备构建输入。
/// 记录当前 Addressables source 快照；Hotfix 时相对<b>最近成功 Full 包内的源快照</b>（构建事实）
/// 计算差异，并把变化资源迁入 Hotfix group。
/// Full/Standalone 不做任何临时移动，只留下可供后续比对与诊断的快照。
/// </summary>
/// <remarks>
/// 基准来自 Summary Index 作用域指向的成功 Full 包（随包交付的 AASourceScan），不依赖前一个 Hotfix 包。
/// 分组恢复（Restore / Discard）仍是 AABuildProjectManager 与维护面板调用的独立静态工具，不属于构建过程。
/// </remarks>
public class PrepareAAInputTask : IBuildTask
{
    public string TaskName => "PrepareAAInput";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        var request = ctx.Require<BuildRequest>(BuildContextKeys.BuildRequest);
        var buildType = request.BuildType;

        try
        {
            List<FileHelper.FileDigest> current = ScanCurrentArtifacts();
            ctx.Set(AABuildContextKeys.AASourceScan, current);

            if (buildType != BuildType.Hotfix)
            {
                Debug.Log($"[{nameof(PrepareAAInputTask)}] {buildType} build 不计算 Hotfix diff，已记录当前源快照: {current.Count}");
                return BuildTaskResult.Ok(new List<string>
                {
                    $"[AA INPUT] {buildType} snapshot={current.Count}"
                });
            }

            var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
            if (!HotfixBaselineResolver.TryResolve(request.BackendKey, cfg.TargetPlatform.ToString(),
                    request.Version.Channel ?? string.Empty, out string baseFullDir, out _, out string baselineError))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    $"AA Hotfix 基准 Full 解析失败: {baselineError}", true);
            }

            if (!AASourceScanFile.TryRead(baseFullDir, out List<FileHelper.FileDigest> previous, out string scanError))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    $"AA Hotfix 缺少基准 Full 的源快照，无法确定变化资源: {scanError}。"
                    + $"基准包={baseFullDir}", true);
            }

            FileHelper.ComputeDiff(previous, current,
                out List<FileHelper.FileDigest> added,
                out List<FileHelper.FileDigest> modified,
                out List<FileHelper.FileDigest> unchanged,
                out List<string> removed);
            LogDiff(added, modified, removed);

            if (!AAHotfixGroupMover.Apply(added, modified))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    "AA Hotfix Group 迁移失败，构建已中止。", true);
            }

            return BuildTaskResult.Ok(new List<string>
            {
                $"[AA INPUT] Added={added.Count}, Modified={modified.Count}, "
                + $"Unchanged={unchanged.Count}, Removed={removed.Count}"
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(PrepareAAInputTask)}] AA 输入准备失败: {ex}");
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, $"AA 输入准备失败: {ex.Message}", true);
        }
    }

    /// <summary>扫描当前 Addressables source 并生成源快照（名称为 Asset GUID）。</summary>
    public static List<FileHelper.FileDigest> ScanCurrentArtifacts()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings is null.");
        return ScanAddressableSource(settings);
    }

    private static List<FileHelper.FileDigest> ScanAddressableSource(AddressableAssetSettings settings)
    {
        var result = new List<FileHelper.FileDigest>();
        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;
            if (group.Name == "Built In Data" || group.HasSchema<PlayerDataGroupSchema>())
                continue;

            foreach (var entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.guid))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(assetPath) || !FileHelper.Exists(assetPath))
                {
                    Debug.LogWarning($"[{nameof(PrepareAAInputTask)}] Addressables entry 指向的 Asset 文件不存在，已跳过。GUID={entry.guid}, Path={assetPath}");
                    continue;
                }

                string metaPath = assetPath + ".meta";
                long size = GetFileSize(assetPath) + GetFileSize(metaPath);
                result.Add(new FileHelper.FileDigest(
                    entry.guid,
                    HashGenerator.GenerateCompositeFileHash(assetPath, metaPath),
                    HashGenerator.GenerateCompositeFileCRC(assetPath, metaPath),
                    size));
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

    private static void LogDiff(
        IReadOnlyList<FileHelper.FileDigest> added,
        IReadOnlyList<FileHelper.FileDigest> modified,
        IReadOnlyList<string> removed)
    {
        for (int i = 0; i < added.Count; i++)
            Debug.Log($"[{nameof(PrepareAAInputTask)}] Artifact 新增：{added[i].Name}");
        for (int i = 0; i < modified.Count; i++)
            Debug.Log($"[{nameof(PrepareAAInputTask)}] Artifact 已修改：{modified[i].Name}");
        for (int i = 0; i < removed.Count; i++)
            Debug.Log($"[{nameof(PrepareAAInputTask)}] Artifact 已移除：{removed[i]}");
    }
}

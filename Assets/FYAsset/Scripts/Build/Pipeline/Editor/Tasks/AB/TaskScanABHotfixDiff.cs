#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// AB bundle diff Task。
/// 消费 ABManifest 中的 bundle 输出信息，对比 Repository HEAD，并把 ArtifactDelta 写回 BuildContext。
/// </summary>
public class TaskScanABHotfixDiff : IBuildTask
{
    public string TaskName => "TaskScanABHotfixDiff";
    public string[] DependsOn => new[] { "TaskVerifyBuildResult" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.ABManifest
    };
    public string[] WriteKeys => new[] { BuildContextKeys.ArtifactDelta, BuildContextKeys.RepositoryArtifacts };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var manifest = ctx.Require<ABManifest>(BuildContextKeys.ABManifest);

        try
        {
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] 开始 AB diff scan，对比本次 Bundle 输出与 Repository HEAD。Package={request.PackageName}");
            var head = BuildRepositoryFacade.GetHeadCommit(BuildRepositoryFacade.GetChannelKey(request));
            var baseline = head != null ? head.Artifacts : new List<ArtifactDigest>();
            var current = ScanCurrentArtifacts(manifest);
            ctx.Set(BuildContextKeys.RepositoryArtifacts, current);
            var delta = ArtifactDiffer.Diff(baseline, current);
            ctx.Set(BuildContextKeys.ArtifactDelta, delta);
            LogDelta(delta);

            if (delta.IsEmpty)
            {
                Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] 未发现 Bundle 变化。Current={current.Count}, Baseline={baseline.Count}");
                return BuildTaskResult.Ok(new List<string> { "[AB DIFF] No changes" });
            }

            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Diff 完成: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AB DIFF] Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}"
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[{nameof(TaskScanABHotfixDiff)}] AB diff scan 失败: {ex}");
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, $"AB diff scan failed: {ex.Message}", true);
        }
    }

    private static List<ArtifactDigest> ScanCurrentArtifacts(ABManifest manifest)
    {
        if (manifest != null && manifest.BundleEntries != null)
            return ScanFromManifest(manifest.BundleEntries);
        Debug.LogWarning($"[{nameof(TaskScanABHotfixDiff)}] ABManifest 为空，回退扫描 preview 输出目录。这个路径只应出现在 Diff Preview/诊断场景。");
        return ScanOutputDirectory();
    }

    private static List<ArtifactDigest> ScanFromManifest(IList<ManifestBundleEntry> bundleEntries)
    {
        var result = new List<ArtifactDigest>(bundleEntries.Count);
        for (int i = 0; i < bundleEntries.Count; i++)
        {
            var entry = bundleEntries[i];
            if (entry == null || string.IsNullOrEmpty(entry.BundleName))
                continue;

            result.Add(new ArtifactDigest
            {
                Name = entry.BundleName,
                Hash = entry.FileHash,
                CRC = entry.FileCRC,
                Size = entry.FileSize
            });
        }
        return result;
    }

    private static List<ArtifactDigest> ScanOutputDirectory()
    {
        string outputDir = Path.Combine(BuildPathManager.ProjectRoot, "Temp", "BuildRepositoryPreview");
        var files = FileHelper.GetFiles(outputDir, "*", SearchOption.TopDirectoryOnly);
        var result = new List<ArtifactDigest>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            if (string.IsNullOrEmpty(path))
                continue;

            var info = new FileInfo(path);
            if (!info.Exists)
                continue;

            result.Add(new ArtifactDigest
            {
                Name = info.Name,
                Hash = HashGenerator.GenerateFileHash(path),
                CRC = HashGenerator.GenerateFileCRC(path),
                Size = info.Length
            });
        }
        return result;
    }

    private static void LogDelta(ArtifactDelta delta)
    {
        for (int i = 0; i < delta.Added.Count; i++)
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Artifact Added: {delta.Added[i].Name}");
        for (int i = 0; i < delta.Modified.Count; i++)
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Artifact Modified: {delta.Modified[i].Name}");
        for (int i = 0; i < delta.Removed.Count; i++)
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Artifact Removed: {delta.Removed[i]}");
    }
}
#endif

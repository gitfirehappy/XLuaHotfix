#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;

/// <summary>
/// AB bundle diff Task。
/// 消费 ABManifest 中的完整 bundle 输出信息：
/// 1. 对比 Repository HEAD，写入用于预览的 ArtifactDelta。
/// 2. 对比同 Major Full baseline，写入 Hotfix delivery bundle 列表。
/// </summary>
public class TaskScanABHotfixDiff : IBuildTask
{
    public string TaskName => "TaskScanABHotfixDiff";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var manifest = ctx.Require<ABManifest>(BuildContextKeys.ABManifest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);
        bool repositoryPreviewMode = ctx.Get<bool>(BuildContextKeys.RepositoryPreviewMode);
        bool deliveryPreviewMode = ctx.Get<bool>(BuildContextKeys.ABDeliveryPreviewMode);

        var current = ScanCurrentArtifacts(manifest);
        ctx.Set(BuildContextKeys.RepositoryArtifacts, current);
        if (buildType != BuildType.Hotfix)
        {
            ctx.Set(BuildContextKeys.ArtifactDelta, new ArtifactDelta());
            ctx.Set(BuildContextKeys.ABDeliveryBundles, new List<ManifestBundleEntry>());
            manifest.DeliveryBundles = new List<ManifestBundleEntry>();
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Full build 不需要计算 Hotfix delivery，已记录当前 Bundle 快照: {current.Count}");
            return BuildTaskResult.Ok(new List<string> { "[AB DIFF] Full build skipped, current artifacts recorded, delivery empty" });
        }

        try
        {
            string channelKey = BuildRepositoryFacade.GetChannelKey(request);
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] 开始 AB diff scan，对比本次 Bundle 输出与 Repository HEAD。Package={request.PackageName}");
            var head = TryGetHeadCommit(channelKey, repositoryPreviewMode);
            var baseline = head != null ? head.Artifacts : new List<ArtifactDigest>();
            var delta = ArtifactDiffer.Diff(baseline, current);
            ctx.Set(BuildContextKeys.ArtifactDelta, delta);
            LogDelta(delta);

            if (repositoryPreviewMode && !deliveryPreviewMode)
            {
                ctx.Set(BuildContextKeys.ABDeliveryBundles, new List<ManifestBundleEntry>());
                return BuildHeadDeltaResult(delta, 0);
            }

            var fullBaseline = FindFullBaseline(channelKey, request.Version);
            if (fullBaseline == null)
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    $"AB Hotfix 缺少同 Major Full baseline。Channel={channelKey}, Version={request.Version.GetReleaseVersionString()}。请先重新执行 AB Full build。", true);
            }

            var deliveryDelta = ArtifactDiffer.Diff(fullBaseline.Artifacts, current);
            var deliveryBundles = BuildDeliveryBundles(manifest, deliveryDelta);
            var validationError = ValidateFullBaselineFallback(manifest, deliveryBundles, fullBaseline.Artifacts);
            if (!string.IsNullOrEmpty(validationError))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed, validationError, true);
            }

            manifest.DeliveryBundles = deliveryBundles;
            ctx.Set(BuildContextKeys.ABDeliveryBundles, deliveryBundles);
            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] AB delivery 完成: FullBaseline={fullBaseline.Version.GetReleaseVersionString()}, DeliveryBundles={deliveryBundles.Count}, DeliverySize={SumBundleSize(deliveryBundles)}");

            if (delta.IsEmpty)
            {
                Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] 未发现 Bundle 变化。Current={current.Count}, Baseline={baseline.Count}");
                return BuildTaskResult.Ok(new List<string>
                {
                    $"[AB DIFF] HEAD no changes, delivery bundles={deliveryBundles.Count}"
                });
            }

            Debug.Log($"[{nameof(TaskScanABHotfixDiff)}] Diff 完成: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AB DIFF] Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}, Delivery={deliveryBundles.Count}"
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[{nameof(TaskScanABHotfixDiff)}] AB diff scan 失败: {ex}");
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, $"AB diff scan failed: {ex.Message}", true);
        }
    }

    private static RepositoryCommit TryGetHeadCommit(string channelKey, bool allowEmptyHead)
    {
        var status = BuildRepositoryFacade.GetStatus(channelKey);
        if (status != null && status.HasHeadError)
            throw new RepositoryHeadException(status.HeadErrorReason);
        if (status == null || !status.HasHead)
        {
            if (allowEmptyHead)
                return null;
            return null;
        }
        return BuildRepositoryFacade.GetHeadCommit(channelKey);
    }

    private static BuildTaskResult BuildHeadDeltaResult(ArtifactDelta delta, int deliveryCount)
    {
        if (delta == null || delta.IsEmpty)
        {
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AB DIFF] HEAD no changes, delivery preview skipped, delivery bundles={deliveryCount}"
            });
        }

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AB DIFF] Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}, delivery preview skipped"
        });
    }

    public static RepositoryCommit FindFullBaseline(string channelKey, VersionNumber currentVersion)
    {
        var commits = BuildRepositoryFacade.ListCommits(channelKey);
        RepositoryCommit best = null;
        for (int i = 0; i < commits.Count; i++)
        {
            var commit = commits[i];
            if (commit == null || commit.Version == null)
                continue;
            if (!string.Equals(commit.BuildType, BuildType.Full.ToString(), StringComparison.Ordinal))
                continue;
            if (currentVersion != null && commit.Version.Major != currentVersion.Major)
                continue;

            if (best == null || commit.Version.CompareTo(best.Version) > 0)
                best = commit;
        }

        return best;
    }

    public static List<ManifestBundleEntry> BuildDeliveryBundles(ABManifest manifest, ArtifactDelta deliveryDelta)
    {
        var deliveryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddArtifactNames(deliveryNames, deliveryDelta?.Added);
        AddArtifactNames(deliveryNames, deliveryDelta?.Modified);

        var result = new List<ManifestBundleEntry>(deliveryNames.Count);
        var entries = manifest?.BundleEntries;
        if (entries == null || deliveryNames.Count == 0)
            return result;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null || string.IsNullOrEmpty(entry.BundleName))
                continue;
            if (deliveryNames.Contains(entry.BundleName))
                result.Add(entry);
        }

        return result;
    }

    public static string ValidateFullBaselineFallback(
        ABManifest manifest,
        IReadOnlyList<ManifestBundleEntry> deliveryBundles,
        IReadOnlyList<ArtifactDigest> fullBaselineArtifacts)
    {
        var delivered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (deliveryBundles != null)
        {
            for (int i = 0; i < deliveryBundles.Count; i++)
            {
                if (!string.IsNullOrEmpty(deliveryBundles[i]?.BundleName))
                    delivered.Add(deliveryBundles[i].BundleName);
            }
        }

        var baselineByName = new Dictionary<string, ArtifactDigest>(StringComparer.OrdinalIgnoreCase);
        if (fullBaselineArtifacts != null)
        {
            for (int i = 0; i < fullBaselineArtifacts.Count; i++)
            {
                var artifact = fullBaselineArtifacts[i];
                if (artifact == null || string.IsNullOrEmpty(artifact.Name))
                    continue;
                if (!baselineByName.ContainsKey(artifact.Name))
                    baselineByName.Add(artifact.Name, artifact);
            }
        }

        var entries = manifest?.BundleEntries;
        if (entries == null)
            return string.Empty;

        for (int i = 0; i < entries.Count; i++)
        {
            var bundle = entries[i];
            if (bundle == null || string.IsNullOrEmpty(bundle.BundleName))
                continue;
            if (delivered.Contains(bundle.BundleName))
                continue;
            if (!baselineByName.TryGetValue(bundle.BundleName, out var baseline))
                return $"AB Hotfix fallback 校验失败：未交付 Bundle 不存在于 Full baseline: {bundle.BundleName}";
            if (!string.Equals(bundle.FileHash, baseline.Hash, StringComparison.Ordinal))
                return $"AB Hotfix fallback 校验失败：未交付 Bundle 与 Full baseline Hash 不一致: {bundle.BundleName}";
        }

        return string.Empty;
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
        string outputDir = FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "Temp", "BuildRepositoryPreview");
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

    private static void AddArtifactNames(HashSet<string> names, IList<ArtifactDigest> artifacts)
    {
        if (names == null || artifacts == null)
            return;
        for (int i = 0; i < artifacts.Count; i++)
        {
            var artifact = artifacts[i];
            if (!string.IsNullOrEmpty(artifact?.Name))
                names.Add(artifact.Name);
        }
    }

    private static long SumBundleSize(IList<ManifestBundleEntry> bundles)
    {
        long total = 0;
        if (bundles == null)
            return total;
        for (int i = 0; i < bundles.Count; i++)
            total += bundles[i] != null ? bundles[i].FileSize : 0;
        return total;
    }
}
#endif

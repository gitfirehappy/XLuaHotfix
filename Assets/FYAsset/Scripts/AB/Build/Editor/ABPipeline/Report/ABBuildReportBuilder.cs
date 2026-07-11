#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

/// <summary>
/// 从 AB 构建上下文生成 Addressables-style 构建报告。
/// </summary>
public static class ABBuildReportBuilder
{
    #region Public API

    public static ABBuildReport Build(
        BuildPackageRequest request,
        BuildResult buildResult,
        BuildContext context,
        Stopwatch stopwatch,
        BuildMessage backendError)
    {
        var report = new ABBuildReport();
        FillHeader(report, request, buildResult, stopwatch, backendError);
        FillTasks(report, buildResult);

        ABManifest manifest = context?.Get<ABManifest>(BuildContextKeys.ABManifest);
        List<ManifestBundleEntry> deliveryBundles = context?.Get<List<ManifestBundleEntry>>(BuildContextKeys.ABDeliveryBundles)
            ?? new List<ManifestBundleEntry>();
        BuildVerificationResult verification = context?.Get<BuildVerificationResult>(BuildContextKeys.BuildVerificationResult);

        FillVerificationIssues(report, verification);
        if (manifest != null)
            FillManifestData(report, manifest, deliveryBundles);

        report.Summary.GroupCount = report.Groups.Count;
        report.Summary.LabelCount = report.Labels.Count;
        report.Summary.BundleCount = report.Bundles.Count;
        report.Summary.AssetCount = report.Assets.Count;
        return report;
    }

    #endregion

    #region Header

    private static void FillHeader(
        ABBuildReport report,
        BuildPackageRequest request,
        BuildResult buildResult,
        Stopwatch stopwatch,
        BuildMessage backendError)
    {
        DateTime finishedAt = DateTime.UtcNow;
        DateTime startedAt = request?.CreatedAt ?? finishedAt;
        bool success = buildResult != null && buildResult.Success && backendError == null;

        report.Header.Backend = BackendModeNames.AB;
        report.Header.BuildType = request?.BuildType.ToString() ?? string.Empty;
        report.Header.BuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        report.Header.Version = request?.Version != null ? request.Version.GetReleaseVersionString() : string.Empty;
        report.Header.PackageName = request?.PackageName ?? string.Empty;
        report.Header.PackagePath = request?.OutputDir ?? string.Empty;
        report.Header.StartedAtUtc = startedAt.ToString("o");
        report.Header.FinishedAtUtc = finishedAt.ToString("o");
        report.Header.DurationSeconds = stopwatch != null ? stopwatch.Elapsed.TotalSeconds : 0d;
        report.Header.Success = success;

        if (backendError != null)
        {
            report.Header.ErrorCode = backendError.Code;
            report.Header.ErrorMessage = backendError.Message;
            AddIssue(report, "Error", backendError.Source, backendError.Code, "ABBuildBackend", backendError.Message);
        }
        else
        {
            BuildTaskResult firstFailure = FindFirstFailure(buildResult);
            if (firstFailure != null)
            {
                report.Header.ErrorCode = firstFailure.ErrorCode;
                report.Header.ErrorMessage = firstFailure.ErrorMessage;
            }
        }
    }

    private static BuildTaskResult FindFirstFailure(BuildResult result)
    {
        if (result?.TaskResults == null)
            return null;

        for (int i = 0; i < result.TaskResults.Count; i++)
        {
            BuildTaskResult taskResult = result.TaskResults[i];
            if (taskResult != null && !taskResult.Success)
                return taskResult;
        }

        return null;
    }

    #endregion

    #region Tasks And Issues

    private static void FillTasks(ABBuildReport report, BuildResult buildResult)
    {
        if (buildResult == null)
            return;

        report.Summary.TotalTasks = buildResult.TotalTasks;
        report.Summary.CompletedTasks = buildResult.CompletedTasks;
        report.Summary.SkippedTasks = buildResult.SkippedTasks;

        if (buildResult.TaskResults == null)
            return;

        for (int i = 0; i < buildResult.TaskResults.Count; i++)
        {
            BuildTaskResult taskResult = buildResult.TaskResults[i];
            if (taskResult == null)
                continue;

            if (!taskResult.Success)
            {
                report.Summary.FailedTasks++;
                AddIssue(report,
                    taskResult.IsFatal ? "Error" : "Warning",
                    "BuildPipelineRunner",
                    taskResult.ErrorCode,
                    "TaskResult",
                    taskResult.ErrorMessage);
            }

            if (taskResult.Warnings == null)
                continue;

            for (int warningIndex = 0; warningIndex < taskResult.Warnings.Count; warningIndex++)
            {
                string warning = taskResult.Warnings[warningIndex];
                if (string.IsNullOrEmpty(warning))
                    continue;

                report.Summary.WarningCount++;
                AddIssue(report, "Warning", "BuildPipelineRunner", string.Empty, "TaskWarning", warning);
            }
        }
    }

    private static void FillVerificationIssues(ABBuildReport report, BuildVerificationResult verification)
    {
        if (verification == null)
            return;

        report.Summary.VerificationErrorCount = verification.ErrorCount;
        report.Summary.VerificationWarningCount = verification.WarningCount;

        if (verification.Issues == null)
            return;

        for (int i = 0; i < verification.Issues.Count; i++)
        {
            VerificationIssue issue = verification.Issues[i];
            if (issue == null)
                continue;

            AddIssue(report,
                issue.Level == IssueLevel.Error ? "Error" : "Warning",
                "TaskVerifyBuildResult",
                issue.CheckName,
                issue.BundleName,
                issue.Message);
        }
    }

    private static void AddIssue(
        ABBuildReport report,
        string severity,
        string source,
        string code,
        string subject,
        string message)
    {
        report.Issues.Add(new ABBuildReportIssue
        {
            Severity = severity ?? string.Empty,
            Source = source ?? string.Empty,
            Code = code ?? string.Empty,
            Subject = subject ?? string.Empty,
            Message = message ?? string.Empty
        });
    }

    #endregion

    #region Manifest Data

    private static void FillManifestData(
        ABBuildReport report,
        ABManifest manifest,
        List<ManifestBundleEntry> deliveryBundles)
    {
        var delivered = BuildDeliverySet(deliveryBundles);
        var bundleNames = BuildBundleNameList(manifest);
        var assetCountByBundle = new int[bundleNames.Count];
        var groupStats = new Dictionary<string, AggregateStats>(StringComparer.Ordinal);
        var labelStats = new Dictionary<string, AggregateStats>(StringComparer.OrdinalIgnoreCase);

        FillAssetRows(report, manifest, delivered, bundleNames, assetCountByBundle, groupStats, labelStats);
        FillBundleRows(report, manifest, delivered, bundleNames, assetCountByBundle, groupStats, labelStats);
        FillReferencedBy(report.Bundles);
        FillAggregateRows(report, groupStats, labelStats);
    }

    internal static void FillReferencedBy(List<ABBuildReportBundle> bundles)
    {
        var byName = new Dictionary<string, ABBuildReportBundle>(StringComparer.Ordinal);
        for (int i = 0; i < bundles.Count; i++)
        {
            ABBuildReportBundle bundle = bundles[i];
            if (bundle != null && !string.IsNullOrEmpty(bundle.BundleName))
                byName[bundle.BundleName] = bundle;
        }

        for (int i = 0; i < bundles.Count; i++)
        {
            ABBuildReportBundle bundle = bundles[i];
            if (bundle?.Dependencies == null)
                continue;

            for (int dependencyIndex = 0; dependencyIndex < bundle.Dependencies.Count; dependencyIndex++)
            {
                if (byName.TryGetValue(bundle.Dependencies[dependencyIndex], out ABBuildReportBundle dependency))
                    dependency.ReferencedBy.Add(bundle.BundleName);
            }
        }
    }

    private static HashSet<string> BuildDeliverySet(List<ManifestBundleEntry> deliveryBundles)
    {
        var delivered = new HashSet<string>(StringComparer.Ordinal);
        if (deliveryBundles == null)
            return delivered;

        for (int i = 0; i < deliveryBundles.Count; i++)
        {
            string bundleName = deliveryBundles[i]?.BundleName;
            if (!string.IsNullOrEmpty(bundleName))
                delivered.Add(bundleName);
        }

        return delivered;
    }

    private static List<string> BuildBundleNameList(ABManifest manifest)
    {
        var names = new List<string>();
        int count = manifest.BundleEntries != null ? manifest.BundleEntries.Count : 0;
        for (int i = 0; i < count; i++)
            names.Add(manifest.BundleEntries[i]?.BundleName ?? string.Empty);
        return names;
    }

    private static void FillAssetRows(
        ABBuildReport report,
        ABManifest manifest,
        HashSet<string> delivered,
        List<string> bundleNames,
        int[] assetCountByBundle,
        Dictionary<string, AggregateStats> groupStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        int assetCount = manifest.AssetEntries != null ? manifest.AssetEntries.Count : 0;
        for (int i = 0; i < assetCount; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null)
                continue;

            string bundleName = GetBundleName(bundleNames, asset.BundleIndex);
            bool isDelivered = delivered.Contains(bundleName);
            if (asset.BundleIndex >= 0 && asset.BundleIndex < assetCountByBundle.Length)
                assetCountByBundle[asset.BundleIndex]++;

            report.Assets.Add(new ABBuildReportAsset
            {
                EntryId = asset.EntryId,
                SourcePath = asset.SourcePath,
                Address = asset.Address,
                PrimaryType = asset.PrimaryType,
                Group = asset.Group,
                Labels = JoinList(asset.Labels),
                BundleName = bundleName,
                Delivered = isDelivered
            });

            GetStats(groupStats, NormalizeGroup(asset.Group)).AssetCount++;
            if (asset.Labels == null)
                continue;

            for (int labelIndex = 0; labelIndex < asset.Labels.Count; labelIndex++)
            {
                string label = asset.Labels[labelIndex];
                if (string.IsNullOrEmpty(label))
                    continue;
                GetStats(labelStats, label).AssetCount++;
            }
        }
    }

    private static void FillBundleRows(
        ABBuildReport report,
        ABManifest manifest,
        HashSet<string> delivered,
        List<string> bundleNames,
        int[] assetCountByBundle,
        Dictionary<string, AggregateStats> groupStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        int bundleCount = manifest.BundleEntries != null ? manifest.BundleEntries.Count : 0;
        for (int i = 0; i < bundleCount; i++)
        {
            ManifestBundleEntry bundle = manifest.BundleEntries[i];
            if (bundle == null)
                continue;

            bool isDelivered = delivered.Contains(bundle.BundleName);
            if (isDelivered)
            {
                report.Summary.DeliveryBundleCount++;
                report.Summary.DeliveryBundleSize += bundle.FileSize;
            }

            report.Summary.TotalBundleSize += bundle.FileSize;

            List<string> dependencies = BuildDependencyNames(bundleNames, bundle.DependBundleIndices);
            List<string> assets = BuildBundleAssetPaths(manifest, i);
            string groupName = InferBundleGroup(manifest, i);

            report.Bundles.Add(new ABBuildReportBundle
            {
                BundleName = bundle.BundleName,
                FileHash = bundle.FileHash,
                FileCRC = bundle.FileCRC,
                FileSize = bundle.FileSize,
                BundleType = bundle.BundleType,
                Tags = JoinList(bundle.Tags),
                Group = groupName,
                AssetCount = i < assetCountByBundle.Length ? assetCountByBundle[i] : assets.Count,
                DependencyCount = dependencies.Count,
                Delivered = isDelivered,
                Dependencies = dependencies,
                Assets = assets
            });

            AggregateStats group = GetStats(groupStats, NormalizeGroup(groupName));
            group.BundleCount++;
            group.BundleNames.Add(bundle.BundleName);
            group.TotalSize += bundle.FileSize;

            AddBundleToLabelStats(manifest, i, bundle, labelStats);
        }
    }

    private static void AddBundleToLabelStats(
        ABManifest manifest,
        int bundleIndex,
        ManifestBundleEntry bundle,
        Dictionary<string, AggregateStats> labelStats)
    {
        if (manifest.AssetEntries == null)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null || asset.BundleIndex != bundleIndex || asset.Labels == null)
                continue;

            for (int labelIndex = 0; labelIndex < asset.Labels.Count; labelIndex++)
            {
                string label = asset.Labels[labelIndex];
                if (string.IsNullOrEmpty(label) || !seen.Add(label))
                    continue;

                AggregateStats stats = GetStats(labelStats, label);
                stats.BundleCount++;
                stats.BundleNames.Add(bundle.BundleName);
                stats.TotalSize += bundle.FileSize;
            }
        }
    }

    private static void FillAggregateRows(
        ABBuildReport report,
        Dictionary<string, AggregateStats> groupStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        foreach (var pair in groupStats)
        {
            report.Groups.Add(new ABBuildReportGroup
            {
                Group = pair.Key,
                AssetCount = pair.Value.AssetCount,
                BundleCount = pair.Value.BundleCount,
                TotalSize = pair.Value.TotalSize
            });
        }

        report.Groups.Sort((left, right) => string.Compare(left.Group, right.Group, StringComparison.Ordinal));

        foreach (var pair in labelStats)
        {
            report.Labels.Add(new ABBuildReportLabel
            {
                Label = pair.Key,
                AssetCount = pair.Value.AssetCount,
                BundleCount = pair.Value.BundleCount,
                TotalSize = pair.Value.TotalSize
            });
        }

        report.Labels.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> BuildDependencyNames(List<string> bundleNames, int[] dependencyIndices)
    {
        var result = new List<string>();
        if (dependencyIndices == null)
            return result;

        for (int i = 0; i < dependencyIndices.Length; i++)
        {
            string dependencyName = GetBundleName(bundleNames, dependencyIndices[i]);
            if (!string.IsNullOrEmpty(dependencyName))
                result.Add(dependencyName);
        }

        return result;
    }

    private static List<string> BuildBundleAssetPaths(ABManifest manifest, int bundleIndex)
    {
        var result = new List<string>();
        if (manifest.AssetEntries == null)
            return result;

        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null || asset.BundleIndex != bundleIndex)
                continue;
            result.Add(string.IsNullOrEmpty(asset.SourcePath) ? asset.Address : asset.SourcePath);
        }

        return result;
    }

    private static string InferBundleGroup(ABManifest manifest, int bundleIndex)
    {
        if (manifest.AssetEntries == null)
            return string.Empty;

        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset != null && asset.BundleIndex == bundleIndex && !string.IsNullOrEmpty(asset.Group))
                return asset.Group;
        }

        return string.Empty;
    }

    private static string GetBundleName(List<string> bundleNames, int index)
    {
        if (bundleNames == null || index < 0 || index >= bundleNames.Count)
            return string.Empty;
        return bundleNames[index];
    }

    private static AggregateStats GetStats(Dictionary<string, AggregateStats> map, string key)
    {
        key = string.IsNullOrEmpty(key) ? "(None)" : key;
        if (!map.TryGetValue(key, out AggregateStats stats))
        {
            stats = new AggregateStats();
            map[key] = stats;
        }

        return stats;
    }

    private static string NormalizeGroup(string group)
    {
        return string.IsNullOrEmpty(group) ? "(None)" : group;
    }

    private static string JoinList(List<string> values)
    {
        if (values == null || values.Count == 0)
            return string.Empty;
        return string.Join(", ", values);
    }

    private sealed class AggregateStats
    {
        public int AssetCount;
        public int BundleCount;
        public long TotalSize;
        public readonly HashSet<string> BundleNames = new(StringComparer.Ordinal);
    }

    #endregion
}
#endif

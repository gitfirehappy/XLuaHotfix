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
    public static ABBuildReport Build(
        BuildPackageRequest request,
        BuildRunResult runResult,
        BuildContext context,
        Stopwatch stopwatch,
        BuildMessage backendError)
    {
        var report = new ABBuildReport();
        FillHeader(report, request, runResult, stopwatch, backendError);
        FillTasks(report, runResult);

        ABManifest manifest = context?.Get<ABManifest>(ABBuildContextKeys.ABManifest);
        List<ManifestContentEntry> deliveryContents = context?.Get<List<ManifestContentEntry>>(ABBuildContextKeys.ABDeliveryContents)
            ?? new List<ManifestContentEntry>();
        BuildVerificationResult verification = context?.Get<BuildVerificationResult>(BuildContextKeys.BuildVerificationResult);

        FillVerificationIssues(report, verification);
        if (manifest != null)
            FillManifestData(report, manifest, deliveryContents);

        report.Summary.ContentTypeCount = report.ContentTypes.Count;
        report.Summary.LabelCount = report.Labels.Count;
        report.Summary.BundleCount = report.Bundles.Count;
        report.Summary.AssetCount = report.Assets.Count;
        return report;
    }

    private static void FillHeader(
        ABBuildReport report,
        BuildPackageRequest request,
        BuildRunResult runResult,
        Stopwatch stopwatch,
        BuildMessage backendError)
    {
        DateTime finishedAt = DateTime.UtcNow;
        DateTime startedAt = request?.CreatedAt ?? finishedAt;
        bool success = runResult != null && runResult.Success && backendError == null;

        report.Header.Backend = "AB";
        report.Header.BuildType = request?.BuildType.ToString() ?? string.Empty;
        report.Header.BuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        report.Header.Version = request?.Version != null ? request.Version.GetReleaseVersionString() : string.Empty;
        report.Header.PackageName = request?.PackageName ?? string.Empty;
        // 报表记录最终交付路径（attempt 布局下 OutputDir 是中间状态目录，交付后会被移走）。
        report.Header.PackagePath = request?.DeliveryOutputDir ?? string.Empty;
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
            BuildTaskResult firstFailure = FindFirstFailure(runResult);
            if (firstFailure != null)
            {
                report.Header.ErrorCode = firstFailure.ErrorCode;
                report.Header.ErrorMessage = firstFailure.ErrorMessage;
            }
        }
    }

    private static BuildTaskResult FindFirstFailure(BuildRunResult result)
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

    private static void FillTasks(ABBuildReport report, BuildRunResult runResult)
    {
        if (runResult == null)
            return;

        report.Summary.TotalTasks = runResult.TotalTasks;
        report.Summary.CompletedTasks = runResult.CompletedTasks;
        report.Summary.SkippedTasks = runResult.SkippedTasks;

        if (runResult.TaskResults == null)
            return;

        for (int i = 0; i < runResult.TaskResults.Count; i++)
        {
            BuildTaskResult taskResult = runResult.TaskResults[i];
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
                "VerifyABContent",
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

    private static void FillManifestData(
        ABBuildReport report,
        ABManifest manifest,
        List<ManifestContentEntry> deliveryContents)
    {
        var delivered = BuildDeliverySet(deliveryContents);
        var contentNames = BuildContentNameList(manifest);
        var assetCountByContent = new int[contentNames.Count];
        var contentTypeStats = new Dictionary<string, AggregateStats>(StringComparer.Ordinal);
        var labelStats = new Dictionary<string, AggregateStats>(StringComparer.OrdinalIgnoreCase);

        FillAssetRows(report, manifest, delivered, contentNames, assetCountByContent, contentTypeStats, labelStats);
        FillContentRows(report, manifest, delivered, contentNames, assetCountByContent, contentTypeStats, labelStats);
        FillReferencedBy(report.Bundles);
        FillAggregateRows(report, contentTypeStats, labelStats);
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

    private static HashSet<string> BuildDeliverySet(List<ManifestContentEntry> deliveryContents)
    {
        var delivered = new HashSet<string>(StringComparer.Ordinal);
        if (deliveryContents == null)
            return delivered;

        for (int i = 0; i < deliveryContents.Count; i++)
        {
            string fileName = deliveryContents[i]?.FileName;
            if (!string.IsNullOrEmpty(fileName))
                delivered.Add(fileName);
        }

        return delivered;
    }

    private static List<string> BuildContentNameList(ABManifest manifest)
    {
        var names = new List<string>();
        int count = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;
        for (int i = 0; i < count; i++)
            names.Add(manifest.ContentEntries[i]?.FileName ?? string.Empty);
        return names;
    }

    private static void FillAssetRows(
        ABBuildReport report,
        ABManifest manifest,
        HashSet<string> delivered,
        List<string> contentNames,
        int[] assetCountByContent,
        Dictionary<string, AggregateStats> contentTypeStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        int assetCount = manifest.AssetEntries != null ? manifest.AssetEntries.Count : 0;
        for (int i = 0; i < assetCount; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null)
                continue;

            string contentName = GetContentName(contentNames, asset.ContentIndex);
            bool isDelivered = delivered.Contains(contentName);
            if (asset.ContentIndex >= 0 && asset.ContentIndex < assetCountByContent.Length)
                assetCountByContent[asset.ContentIndex]++;

            report.Assets.Add(new ABBuildReportAsset
            {
                EntryId = asset.EntryId,
                SourcePath = asset.SourcePath,
                Address = asset.Address,
                PrimaryType = asset.PrimaryType,
                IsPublic = asset.IsPublic,
                ContentType = asset.ContentType.ToString(),
                Labels = JoinList(asset.Labels),
                BundleName = contentName,
                Delivered = isDelivered
            });

            GetStats(contentTypeStats, asset.ContentType.ToString()).AssetCount++;
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

    private static void FillContentRows(
        ABBuildReport report,
        ABManifest manifest,
        HashSet<string> delivered,
        List<string> contentNames,
        int[] assetCountByContent,
        Dictionary<string, AggregateStats> contentTypeStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        int contentCount = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;
        for (int i = 0; i < contentCount; i++)
        {
            ManifestContentEntry content = manifest.ContentEntries[i];
            if (content == null)
                continue;

            bool isDelivered = delivered.Contains(content.FileName);
            if (isDelivered)
            {
                report.Summary.DeliveryBundleCount++;
                report.Summary.DeliveryBundleSize += content.FileSize;
            }

            report.Summary.TotalBundleSize += content.FileSize;

            List<string> dependencies = BuildDependencyNames(contentNames, content.DependencyIndices);
            List<string> assets = BuildContentAssetPaths(manifest, i);

            report.Bundles.Add(new ABBuildReportBundle
            {
                BundleName = content.FileName,
                FileHash = content.FileHash,
                FileCRC = content.FileCRC,
                FileSize = content.FileSize,
                ContentType = content.ContentType.ToString(),
                AssetCount = i < assetCountByContent.Length ? assetCountByContent[i] : assets.Count,
                DependencyCount = dependencies.Count,
                Delivered = isDelivered,
                Dependencies = dependencies,
                Assets = assets
            });

            AggregateStats contentStats = GetStats(contentTypeStats, content.ContentType.ToString());
            contentStats.BundleCount++;
            contentStats.BundleNames.Add(content.FileName);
            contentStats.TotalSize += content.FileSize;

            AddContentToLabelStats(manifest, i, content, labelStats);
        }
    }

    private static void AddContentToLabelStats(
        ABManifest manifest,
        int contentIndex,
        ManifestContentEntry content,
        Dictionary<string, AggregateStats> labelStats)
    {
        if (manifest.AssetEntries == null)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null || asset.ContentIndex != contentIndex || asset.Labels == null)
                continue;

            for (int labelIndex = 0; labelIndex < asset.Labels.Count; labelIndex++)
            {
                string label = asset.Labels[labelIndex];
                if (string.IsNullOrEmpty(label) || !seen.Add(label))
                    continue;

                AggregateStats stats = GetStats(labelStats, label);
                stats.BundleCount++;
                stats.BundleNames.Add(content.FileName);
                stats.TotalSize += content.FileSize;
            }
        }
    }

    private static void FillAggregateRows(
        ABBuildReport report,
        Dictionary<string, AggregateStats> contentTypeStats,
        Dictionary<string, AggregateStats> labelStats)
    {
        foreach (var pair in contentTypeStats)
        {
            report.ContentTypes.Add(new ABBuildReportContentType
            {
                ContentType = pair.Key,
                AssetCount = pair.Value.AssetCount,
                BundleCount = pair.Value.BundleCount,
                TotalSize = pair.Value.TotalSize
            });
        }

        report.ContentTypes.Sort((left, right) => string.Compare(left.ContentType, right.ContentType, StringComparison.Ordinal));

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

    private static List<string> BuildDependencyNames(List<string> contentNames, int[] dependencyIndices)
    {
        var result = new List<string>();
        if (dependencyIndices == null)
            return result;

        for (int i = 0; i < dependencyIndices.Length; i++)
        {
            string dependencyName = GetContentName(contentNames, dependencyIndices[i]);
            if (!string.IsNullOrEmpty(dependencyName))
                result.Add(dependencyName);
        }

        return result;
    }

    private static List<string> BuildContentAssetPaths(ABManifest manifest, int contentIndex)
    {
        var result = new List<string>();
        if (manifest.AssetEntries == null)
            return result;

        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry asset = manifest.AssetEntries[i];
            if (asset == null || asset.ContentIndex != contentIndex)
                continue;
            result.Add(string.IsNullOrEmpty(asset.SourcePath) ? asset.Address : asset.SourcePath);
        }

        return result;
    }

    private static string GetContentName(List<string> contentNames, int index)
    {
        if (contentNames == null || index < 0 || index >= contentNames.Count)
            return string.Empty;
        return contentNames[index];
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
}
#endif

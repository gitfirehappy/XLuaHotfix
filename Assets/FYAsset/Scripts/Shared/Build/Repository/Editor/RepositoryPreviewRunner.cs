#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diff Preview 的 pipeline 执行器。
/// 只运行到 diff Task，读取 BuildContext 中的 ArtifactDelta；不会写 HEAD、objects 或 PackageIndex。
/// </summary>
public static class RepositoryPreviewRunner
{
    public static ArtifactDelta RunAAPreview(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.AA));
        if (config == null)
            throw new InvalidOperationException("AA BuildPipelineConfig is null.");

        var previewContext = CreatePreviewContext(request, BuildType.Hotfix);
        var whitelist = new HashSet<string>(StringComparer.Ordinal)
        {
            "TaskScanAddressableHotfixDiff"
        };

        Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AA Diff Preview start，Pipeline stop-after=TaskScanAddressableHotfixDiff。Package={request.PackageName}");
        previewContext.Set(BuildContextKeys.RepositoryPreviewMode, true);
        BuildResult result = BuildPipelineRunner.Execute(config, previewContext, null, "TaskScanAddressableHotfixDiff", whitelist);
        if (!result.Success)
            throw new InvalidOperationException(FormatPreviewFailure("AA", result));

        ArtifactDelta delta = RequireDelta(previewContext, "AA");
        Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AA Diff Preview done: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
        return delta;
    }

    public static ArtifactDelta RunABPreview(BuildPackageRequest request)
    {
        return RunABPreviewDetailed(request).HeadDelta;
    }

    public static ABRepositoryPreviewResult RunABPreviewDetailed(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string previewRoot = FYAssetPathUtility.JoinFilePath(
            BuildPathManager.ProjectRoot,
            "Temp",
            "BuildRepositoryPreview",
            Guid.NewGuid().ToString("N"));
        string previewBuildRoot = FYAssetPathUtility.JoinFilePath(previewRoot, "build");

        FileHelper.TryDeleteDirectory(previewRoot, true);
        FileHelper.EnsureDirectory(previewBuildRoot);

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.ABManifest));
        if (config == null)
            throw new InvalidOperationException("BuildPipelineConfig is null.");

        var previewContext = CreatePreviewContext(request, BuildType.Hotfix);

        try
        {
            previewContext.Set(BuildContextKeys.RepositoryPreviewOutput, previewBuildRoot);
            previewContext.Set(BuildContextKeys.RepositoryPreviewMode, true);
            var whitelist = new HashSet<string>(StringComparer.Ordinal)
            {
                "TaskPrepareContext",
                "TaskCollectAssets",
                "TaskCollectBuiltins",
                "TaskAnalyzeDependencies",
                "TaskBuildBundles",
                "TaskGenerateManifest",
                "TaskVerifyBuildResult",
                "TaskScanABHotfixDiff"
            };
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Diff Preview start，临时输出目录: {previewBuildRoot}");
            BuildResult result = BuildPipelineRunner.Execute(config, previewContext, null, "TaskScanABHotfixDiff", whitelist);
            if (!result.Success)
                throw new InvalidOperationException(FormatPreviewFailure("AB", result));

            ArtifactDelta delta = RequireDelta(previewContext, "AB");
            var deliveryBundles = previewContext.Get<List<ManifestBundleEntry>>(BuildContextKeys.ABDeliveryBundles)
                ?? new List<ManifestBundleEntry>();
            bool deliveryAvailable = previewContext.Get<bool>(BuildContextKeys.ABDeliveryPreviewMode);
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Diff Preview done: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}, Delivery={deliveryBundles.Count}");
            return new ABRepositoryPreviewResult
            {
                HeadDelta = delta,
                DeliveryBundles = deliveryBundles,
                DeliverySizeBytes = SumDeliverySize(deliveryBundles),
                DeliveryAvailable = deliveryAvailable,
                DeliveryMessage = deliveryAvailable
                    ? "Hotfix Delivery is loaded."
                    : "Hotfix Delivery is not loaded. Use Preview Delivery to calculate current output vs Full baseline."
            };
        }
        finally
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Diff Preview 临时目录已清理: {previewRoot}");
        }
    }

    private static BuildContext CreatePreviewContext(BuildPackageRequest request, BuildType buildType)
    {
        // Preview 使用新的 request，避免正式构建的输出路径被临时 pipeline 污染。
        var previewContext = new BuildContext();
        var previewRequest = BuildPackageRequest.Create(request.Version, buildType, request.BackendMode);
        previewContext.Set(BuildContextKeys.BuildPackageRequest, previewRequest);
        previewContext.Set(BuildContextKeys.BuildType, buildType);
        return previewContext;
    }

    private static ArtifactDelta RequireDelta(BuildContext context, string backendLabel)
    {
        var delta = context.Get<ArtifactDelta>(BuildContextKeys.ArtifactDelta);
        if (delta == null)
            throw new InvalidOperationException($"{backendLabel} diff preview did not produce ArtifactDelta.");
        return delta;
    }

    public static ABRepositoryPreviewResult RunABDeliveryPreview(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string previewRoot = FYAssetPathUtility.JoinFilePath(
            BuildPathManager.ProjectRoot,
            "Temp",
            "BuildRepositoryDeliveryPreview",
            Guid.NewGuid().ToString("N"));
        string previewBuildRoot = FYAssetPathUtility.JoinFilePath(previewRoot, "build");

        FileHelper.TryDeleteDirectory(previewRoot, true);
        FileHelper.EnsureDirectory(previewBuildRoot);

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.ABManifest));
        if (config == null)
            throw new InvalidOperationException("BuildPipelineConfig is null.");

        var previewContext = CreatePreviewContext(request, BuildType.Hotfix);

        try
        {
            previewContext.Set(BuildContextKeys.RepositoryPreviewOutput, previewBuildRoot);
            previewContext.Set(BuildContextKeys.RepositoryPreviewMode, true);
            previewContext.Set(BuildContextKeys.ABDeliveryPreviewMode, true);
            var whitelist = new HashSet<string>(StringComparer.Ordinal)
            {
                "TaskPrepareContext",
                "TaskCollectAssets",
                "TaskCollectBuiltins",
                "TaskAnalyzeDependencies",
                "TaskBuildBundles",
                "TaskGenerateManifest",
                "TaskVerifyBuildResult",
                "TaskScanABHotfixDiff"
            };
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Delivery Preview start，临时输出目录: {previewBuildRoot}");
            BuildResult result = BuildPipelineRunner.Execute(config, previewContext, null, "TaskScanABHotfixDiff", whitelist);
            if (!result.Success)
                throw new InvalidOperationException(FormatPreviewFailure("AB Delivery", result));

            ArtifactDelta delta = RequireDelta(previewContext, "AB");
            var deliveryBundles = previewContext.Get<List<ManifestBundleEntry>>(BuildContextKeys.ABDeliveryBundles)
                ?? new List<ManifestBundleEntry>();
            return new ABRepositoryPreviewResult
            {
                HeadDelta = delta,
                DeliveryBundles = deliveryBundles,
                DeliverySizeBytes = SumDeliverySize(deliveryBundles),
                DeliveryAvailable = true,
                DeliveryMessage = "Hotfix Delivery is loaded."
            };
        }
        finally
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Delivery Preview 临时目录已清理: {previewRoot}");
        }
    }

    private static long SumDeliverySize(IReadOnlyList<ManifestBundleEntry> deliveryBundles)
    {
        long total = 0;
        if (deliveryBundles == null)
            return total;
        for (int i = 0; i < deliveryBundles.Count; i++)
            total += deliveryBundles[i] != null ? deliveryBundles[i].FileSize : 0;
        return total;
    }

    private static string FormatPreviewFailure(string backendLabel, BuildResult result)
    {
        if (result == null)
            return $"{backendLabel} preview pipeline failed: result is null.";

        BuildTaskResult failed = null;
        if (result.TaskResults != null)
        {
            for (int i = 0; i < result.TaskResults.Count; i++)
            {
                var item = result.TaskResults[i];
                if (item != null && !item.Success)
                {
                    failed = item;
                    break;
                }
            }
        }

        if (failed == null)
            return $"{backendLabel} preview pipeline failed. Completed={result.CompletedTasks}, Skipped={result.SkippedTasks}.";

        string taskName = string.IsNullOrEmpty(failed.TaskName) ? "<unknown>" : failed.TaskName;
        string code = string.IsNullOrEmpty(failed.ErrorCode) ? "<no-code>" : failed.ErrorCode;
        string message = string.IsNullOrEmpty(failed.ErrorMessage) ? "<no-message>" : failed.ErrorMessage;
        return $"{backendLabel} preview pipeline failed at {taskName}: [{code}] {message} Fatal={failed.IsFatal}, Skipped={result.SkippedTasks}.";
    }
}
#endif

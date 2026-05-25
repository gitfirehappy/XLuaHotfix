#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diff Preview 的 DAG 执行器。
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

        Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AA Diff Preview start，DAG stop-after=TaskScanAddressableHotfixDiff。Package={request.PackageName}");
        BuildResult result = DAGScheduler.Execute(config, previewContext, null, "TaskScanAddressableHotfixDiff", whitelist);
        if (!result.Success)
            throw new InvalidOperationException("AA diff preview pipeline failed.");

        ArtifactDelta delta = RequireDelta(previewContext, "AA");
        Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AA Diff Preview done: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
        return delta;
    }

    public static ArtifactDelta RunABPreview(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string previewRoot = Path.Combine(BuildPathManager.ProjectRoot, "Temp", "BuildRepositoryPreview", Guid.NewGuid().ToString("N"));
        string previewBuildRoot = Path.Combine(previewRoot, "build");

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
            BuildResult result = DAGScheduler.Execute(config, previewContext, null, "TaskScanABHotfixDiff", whitelist);
            if (!result.Success)
                throw new InvalidOperationException("AB diff preview pipeline failed.");

            ArtifactDelta delta = RequireDelta(previewContext, "AB");
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Diff Preview done: Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}");
            return delta;
        }
        finally
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
            Debug.Log($"[{nameof(RepositoryPreviewRunner)}] AB Diff Preview 临时目录已清理: {previewRoot}");
        }
    }

    private static BuildContext CreatePreviewContext(BuildPackageRequest request, BuildType buildType)
    {
        // Preview 使用新的 request，避免正式构建的输出路径被临时 DAG 污染。
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
}
#endif

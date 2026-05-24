#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// Diff Preview 的临时执行器。
/// 只用于获取当前 artifacts，不写入 HEAD / objects / PackageIndex。
/// </summary>
public static class RepositoryPreviewRunner
{
    public static ArtifactDelta RunAAPreview(BuildPackageRequest request)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings is null.");

        var scanner = new AddressableSourceArtifactScanner(settings);
        return BuildRepositoryFacade.DiffHead(request, scanner);
    }

    public static ArtifactDelta RunABPreview(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string previewRoot = Path.Combine(BuildPathManager.ProjectRoot, "Temp", "BuildRepositoryPreview", Guid.NewGuid().ToString("N"));
        string previewBuildRoot = Path.Combine(previewRoot, "build");

        FileHelper.TryDeleteDirectory(previewRoot, true);
        FileHelper.EnsureDirectory(previewBuildRoot);

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (config == null)
            throw new InvalidOperationException("BuildPipelineConfig is null.");

        var previewContext = new BuildContext();
        var previewRequest = BuildPackageRequest.Create(request.Version, request.BuildType, request.BackendMode);
        previewContext.Set(BuildContextKeys.BuildPackageRequest, previewRequest);
        previewContext.Set(BuildContextKeys.BuildType, request.BuildType);

        try
        {
            string oldOutput = Environment.GetEnvironmentVariable("BUILD_REPOSITORY_PREVIEW_OUTPUT");
            Environment.SetEnvironmentVariable("BUILD_REPOSITORY_PREVIEW_OUTPUT", previewBuildRoot);
            try
            {
                var whitelist = new HashSet<string>(StringComparer.Ordinal)
                {
                    "TaskPrepareContext",
                    "TaskCollectAssets",
                    "TaskCollectBuiltins",
                    "TaskAnalyzeDependencies",
                    "TaskBuildBundles",
                    "TaskGenerateManifest",
                    "TaskVerifyBuildResult"
                };
                BuildResult result = DAGScheduler.Execute(config, previewContext, null, "TaskVerifyBuildResult", whitelist);
                if (!result.Success)
                    throw new InvalidOperationException("AB preview pipeline failed.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("BUILD_REPOSITORY_PREVIEW_OUTPUT", oldOutput);
            }

            var manifest = previewContext.Get<ABManifest>(BuildContextKeys.ABManifest);
            if (manifest == null)
                throw new InvalidOperationException("AB preview did not produce ABManifest.");

            var scanner = new AbBundleOutputArtifactScanner(manifest.BundleEntries);
            return BuildRepositoryFacade.DiffHead(request, scanner);
        }
        finally
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
        }
    }
}
#endif

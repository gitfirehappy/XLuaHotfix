#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AA 构建后端。
/// 只负责把 BuildPackageRequest 交给 AA Pipeline；Addressables build 与 Manifest 由 Task 列表处理，发布指针由编排层在 Repository commit 后写入。
/// </summary>
public class AABuildBackend : IBuildBackend, IBaselinePackageHandler
{
    public IBaselinePackageHandler BaselineHandler => this;

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetAASettings.Instance.BuildPipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 AA BuildPipelineConfig。", nameof(AABuildBackend))));

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));

            var context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            context.Set(BuildContextKeys.DeferPackagePublication, true);
            context.Set(BuildContextKeys.BaselinePackageHandler, this);
            Debug.Log($"[{nameof(AABuildBackend)}] 启动 AA Pipeline。BuildType={request.BuildType}, Package={request.PackageName}");
            BuildResult result = BuildPipelineRunner.Execute(config, context, options, AAPipelineBackbone.BackboneTaskNames);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AA 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", nameof(AABuildBackend))));
            }

            var artifacts = context.Get<List<ArtifactDigest>>(BuildContextKeys.RepositoryArtifacts);
            Debug.Log($"[{nameof(AABuildBackend)}] AA Pipeline 完成。Completed={result.CompletedTasks}/{result.TotalTasks}, RepositoryArtifacts={(artifacts != null ? artifacts.Count : 0)}");
            return Task.FromResult(BuildBackendResult.Ok(
                artifacts, result, request, string.Empty,
                context.Get<ArtifactDelta>(BuildContextKeys.ArtifactDelta)));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(AABuildBackend)}] AA Pipeline 异常: {ex}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, nameof(AABuildBackend))));
        }
    }

    // --- IBaselinePackageHandler ---

    public IReadOnlyList<string> RequiredManifestFileNames { get; } = new[]
    {
        FYAssetSettings.AA_MANIFEST_FILE_NAME,
        FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN
    };

    public void StageBaselineFiles(BuildPackageRequest request, string stageRoot)
    {
        Debug.Log("[AABuildBackend] 正在暂存 AA baseline package...");
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
    }

    public IReadOnlyList<BundleDownloadItem> LoadStagedBaselineBundles(string stageRoot)
    {
        string aaJson = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string aaBin = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        if (!FileHelper.Exists(aaJson) && !FileHelper.Exists(aaBin))
            throw new FileNotFoundException($"Staged AAManifest missing: {aaJson} or {aaBin}", aaJson);
        string aaCatalog = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        if (!FileHelper.Exists(aaCatalog))
            throw new FileNotFoundException($"Staged catalog missing: {aaCatalog}", aaCatalog);

        AAManifest manifest = AAManifestLoader.LoadFromDirectory(stageRoot);
        return ToBundleItems(manifest?.Bundles);
    }

    public void ApplyStagedBaseline(string stageRoot)
    {
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        // 启动数据区单后端独占：清理 AB 侧遗留 manifest。
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN));
    }

    private static void StageFileIfExists(string sourceDir, string stageRoot, string fileName)
    {
        string sourcePath = FYAssetPathUtility.JoinFilePath(sourceDir, fileName);
        string targetPath = FYAssetPathUtility.JoinFilePath(stageRoot, fileName);
        if (FileHelper.Exists(sourcePath))
            FileHelper.CopyFile(sourcePath, targetPath, true);
    }

    private static void ApplyFileOrDelete(string sourceDir, string targetDir, string fileName)
    {
        string sourcePath = FYAssetPathUtility.JoinFilePath(sourceDir, fileName);
        string targetPath = FYAssetPathUtility.JoinFilePath(targetDir, fileName);
        if (FileHelper.Exists(sourcePath))
        {
            FileHelper.CopyFile(sourcePath, targetPath, true);
            return;
        }

        FileHelper.TryDelete(targetPath);
    }

    private static IReadOnlyList<BundleDownloadItem> ToBundleItems(IReadOnlyList<BundleInfo> bundles)
    {
        if (bundles == null)
            return null;

        var result = new List<BundleDownloadItem>(bundles.Count);
        for (int i = 0; i < bundles.Count; i++)
        {
            BundleInfo bundle = bundles[i];
            result.Add(bundle == null ? default : new BundleDownloadItem
            {
                BundleName = bundle.BundleName,
                FileHash = bundle.FileHash,
                FileCRC = bundle.FileCRC,
                FileSize = bundle.FileSize
            });
        }
        return result;
    }

    /// <summary>
    /// 遍历 BuildResult 中所有失败 Task 并输出 Warning 日志。
    /// </summary>
    private static void LogBuildResultErrors(BuildResult result)
    {
        if (result?.TaskResults == null)
            return;

        foreach (var taskResult in result.TaskResults)
        {
            if (taskResult == null || taskResult.Success)
                continue;

            Debug.LogWarning($"[{nameof(AABuildBackend)}] Pipeline Task 失败: Code={taskResult.ErrorCode}, Message={taskResult.ErrorMessage}");
        }
    }
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// ABManifest 新管线构建后端。
/// 只负责把 BuildPackageRequest 交给 AB Pipeline；最终包目录与 Manifest 由 Task 列表写入，发布指针由编排层在 Repository commit 后写入。
/// </summary>
public class ABBuildBackend : IBuildBackend, IBaselinePackageHandler
{
    public IBaselinePackageHandler BaselineHandler => this;

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        BuildContext context = null;
        BuildResult result = null;

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetABSettings.Instance.BuildPipelineConfigPath);
        if (config == null)
        {
            var error = BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, context, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));
            context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            context.Set(BuildContextKeys.DeferPackagePublication, true);
            context.Set(BuildContextKeys.BaselinePackageHandler, this);
            Debug.Log($"[{nameof(ABBuildBackend)}] 启动 AB Pipeline。BuildType={request.BuildType}, Package={request.PackageName}");
            result = BuildPipelineRunner.Execute(config, context, options, ABPipelineBackbone.BackboneTaskNames);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                var error = BuildMessage.Error(BuildErrorCodes.BuildFailed,
                    $"AB 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", nameof(ABBuildBackend));
                string reportPath = TryWriteReport(request, result, context, stopwatch, error);
                return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
            }

            var artifacts = context.Get<List<ArtifactDigest>>(BuildContextKeys.RepositoryArtifacts);
            Debug.Log($"[{nameof(ABBuildBackend)}] AB Pipeline 完成。Completed={result.CompletedTasks}/{result.TotalTasks}, RepositoryArtifacts={(artifacts != null ? artifacts.Count : 0)}");
            string successReportPath = TryWriteReport(request, result, context, stopwatch, null);
            return Task.FromResult(BuildBackendResult.Ok(
                artifacts, result, request, successReportPath,
                context.Get<ArtifactDelta>(BuildContextKeys.ArtifactDelta)));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildBackend)}] AB Pipeline 异常: {ex}");
            var error = BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, context, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }
    }

    /// <summary>
    /// 遍历 BuildResult 中所有失败 Task 并输出 Error 日志。
    /// </summary>
    private static void LogBuildResultErrors(BuildResult result)
    {
        if (result?.TaskResults == null)
            return;

        foreach (var taskResult in result.TaskResults)
        {
            if (taskResult == null || taskResult.Success)
                continue;

            Debug.LogWarning($"[{nameof(ABBuildBackend)}] Pipeline Task 失败: Code={taskResult.ErrorCode}, Message={taskResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Best-effort 写入 AB 构建报告。报告失败不能覆盖原始构建结果。
    /// </summary>
    private static string TryWriteReport(
        BuildPackageRequest request,
        BuildResult result,
        BuildContext context,
        Stopwatch stopwatch,
        BuildMessage error)
    {
        if (request == null)
            return string.Empty;

        try
        {
            ABBuildReport report = ABBuildReportBuilder.Build(request, result, context, stopwatch, error);

            string path = ABBuildReportStore.CreateReportPath(request);
            ABBuildReportStore.Write(report, path);
            Debug.Log($"[{nameof(ABBuildBackend)}] AB 构建报告已写入: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(ABBuildBackend)}] AB 构建报告写入失败: {ex.Message}");
            return string.Empty;
        }
    }

    // --- IBaselinePackageHandler ---

    public IReadOnlyList<string> RequiredManifestFileNames { get; } = new[]
    {
        FYAssetSettings.MANIFEST_FILE_NAME,
        FYAssetSettings.MANIFEST_FILE_NAME_BIN
    };

    public void StageBaselineFiles(BuildPackageRequest request, string stageRoot)
    {
        Debug.Log("[ABBuildBackend] 正在暂存 AB baseline package...");
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
    }

    public IReadOnlyList<BundleDownloadItem> LoadStagedBaselineBundles(string stageRoot)
    {
        string json = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
        string bin = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        if (!FileHelper.Exists(json) && !FileHelper.Exists(bin))
            throw new FileNotFoundException($"Staged ABManifest missing: {json} or {bin}", json);

        ABManifest manifest = SerializationUtility.ReadFromFile<ABManifest>(
            FileHelper.Exists(bin) ? bin : json);
        return ToBundleItems(manifest?.BundleEntries);
    }

    public void ApplyStagedBaseline(string stageRoot)
    {
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME);
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        // 启动数据区单后端独占：清理 AA 侧遗留 manifest。
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
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

    private static IReadOnlyList<BundleDownloadItem> ToBundleItems(IReadOnlyList<ManifestBundleEntry> bundles)
    {
        if (bundles == null)
            return null;

        var result = new List<BundleDownloadItem>(bundles.Count);
        for (int i = 0; i < bundles.Count; i++)
        {
            ManifestBundleEntry bundle = bundles[i];
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
}
#endif

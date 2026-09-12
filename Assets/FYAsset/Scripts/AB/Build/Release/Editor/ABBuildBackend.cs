#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// 执行 AB Task 管线并产出构建报告。
/// 同时提供 AB 内置包（StreamingAssets 启动数据）文件的暂存与安装操作；最终交付由 BuildProjectRunner 编排。
/// </summary>
public class ABBuildBackend : IBuildBackend, IBuiltInPackageHandler
{
    public IBuiltInPackageHandler BuiltInPackageHandler => this;

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        BuildRunResult result = null;

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetABSettings.Instance.BuildPipelineConfigPath);
        if (config == null)
        {
            var error = BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, null, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));

            // 一次性把旧的“主干顺序列表”升级成“自定义 Task + 槽位”；已升级配置保持原样。
            ABPipelineConfigUpgrade.TryUpgrade(config);

            // 主干固定 6 段，自定义 Task 只能插入到合法槽位；组装失败一律致命。
            IReadOnlyList<IBuildTask> tasks = ABPipelineBackbone.ComposeTasks(config);

            var runRequest = new BuildRequest(request, options, new EditorBuildRunEnvironment());
            Debug.Log($"[{nameof(ABBuildBackend)}] 启动 AB Pipeline。BuildType={request.BuildType}, Package={request.PackageName}, Tasks={tasks.Count}");
            result = BuildPipelineRunner.Run(runRequest, tasks);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                var error = BuildMessage.Error(BuildErrorCodes.BuildFailed, FirstFailureMessage(result), nameof(ABBuildBackend));
                string reportPath = TryWriteReport(request, result, result.Context, stopwatch, error);
                return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
            }

            Debug.Log($"[{nameof(ABBuildBackend)}] AB Pipeline 完成。Completed={result.CompletedTasks}/{result.TotalTasks}");
            string successReportPath = TryWriteReport(request, result, result.Context, stopwatch, null);
            return Task.FromResult(BuildBackendResult.Ok(
                result, request, successReportPath,
                result.Context.Get<CompleteBuildSummary>(BuildContextKeys.BuildSummary)));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildBackend)}] AB Pipeline 异常: {ex}");
            var error = BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, result?.Context, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }
    }

    /// <summary>
    /// 把失败 Task 结果写成 Warning。
    /// </summary>
    private static void LogBuildResultErrors(BuildRunResult result)
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
    /// 首个失败 Task 的错误码与消息。Runner 首个失败即停，因此它就是本次构建的根因；
    /// 后端只做展示，不做裁剪或重试。
    /// </summary>
    private static string FirstFailureMessage(BuildRunResult result)
    {
        if (result?.TaskResults != null)
        {
            for (int i = 0; i < result.TaskResults.Count; i++)
            {
                var taskResult = result.TaskResults[i];
                if (taskResult == null || taskResult.Success)
                    continue;

                return $"AB 管线构建失败于 {taskResult.TaskName}: [{taskResult.ErrorCode}] {taskResult.ErrorMessage}"
                    + $"（已完成 {result.CompletedTasks}/{result.TotalTasks}）";
            }
        }

        return $"AB 管线构建失败。已完成: {result?.CompletedTasks ?? 0}/{result?.TotalTasks ?? 0}";
    }

    /// <summary>
    /// Best-effort 写入 AB 构建报告。报告失败不能覆盖原始构建结果。
    /// </summary>
    private static string TryWriteReport(
        BuildPackageRequest request,
        BuildRunResult result,
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

    /// <summary>包根必须存在的 AB 清单文件；解析规则由 ABPackageManifestReader 统一持有。</summary>
    public IReadOnlyList<string> RequiredManifestFileNames => ABPackageManifestReader.ResolveRequiredFileNames();

    public void StageBuiltInFiles(BuildPackageRequest request, string stageRoot)
    {
        Debug.Log("[ABBuildBackend] 正在暂存 AB 内置包清单...");
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
    }

    public IReadOnlyList<BundleDownloadItem> LoadStagedBundles(string stageRoot)
    {
        string json = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
        string bin = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        if (!FileHelper.Exists(json) && !FileHelper.Exists(bin))
            throw new FileNotFoundException($"Staged ABManifest missing: {json} or {bin}", json);

        ABManifest manifest = SerializationUtility.ReadFromFile<ABManifest>(
            FileHelper.Exists(bin) ? bin : json);
        return ToBundleItems(manifest?.ContentEntries);
    }

    public void ApplyStagedBuiltIn(string stageRoot)
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

    private static IReadOnlyList<BundleDownloadItem> ToBundleItems(IReadOnlyList<ManifestContentEntry> contents)
    {
        if (contents == null)
            return null;

        var result = new List<BundleDownloadItem>(contents.Count);
        for (int i = 0; i < contents.Count; i++)
        {
            ManifestContentEntry content = contents[i];
            result.Add(content == null ? default : new BundleDownloadItem
            {
                BundleName = content.FileName,
                FileHash = content.FileHash,
                FileCRC = content.FileCRC,
                FileSize = content.FileSize
            });
        }
        return result;
    }
}
#endif

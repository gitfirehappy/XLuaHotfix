#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 使用 Addressables 执行 AA Task 管线。
/// 同时提供 AA 内置包（StreamingAssets 启动数据）文件的暂存与安装操作；最终交付由 BuildProjectRunner 编排。
/// </summary>
public class AABuildBackend : IBuildBackend, IBuiltInPackageHandler
{
    public IBuiltInPackageHandler BuiltInPackageHandler => this;

    public Task<BuildResult> BuildAsync(BuildRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetAASettings.Instance.BuildPipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 AA BuildPipelineConfig。", nameof(AABuildBackend))));

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));

            // 确保配置中的自定义 Task 已映射到插入槽位；升级操作必须幂等。
            AAPipelineConfigUpgrade.TryUpgrade(config);

            // 主干固定 5 段，自定义 Task 只能插入到合法槽位；组装失败一律致命。
            IReadOnlyList<IBuildTask> tasks = AAPipelineBackbone.ComposeTasks(config);

            var runRequest = new BuildPipelineRequest(request, options, new EditorBuildRunEnvironment());
            Debug.Log($"[{nameof(AABuildBackend)}] 启动 AA Pipeline。BuildType={request.BuildType}, Package={request.PackageName}, Tasks={tasks.Count}");
            BuildRunResult result = BuildPipelineRunner.Run(runRequest, tasks);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        FirstFailureMessage(result), nameof(AABuildBackend)),
                    result, request, string.Empty));
            }

            Debug.Log($"[{nameof(AABuildBackend)}] AA Pipeline 完成。Completed={result.CompletedTasks}/{result.TotalTasks}");
            return Task.FromResult(BuildResult.Ok(
                result, request, string.Empty,
                result.Context.Get<CompleteBuildSummary>(BuildContextKeys.BuildSummary)));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(AABuildBackend)}] AA Pipeline 异常: {ex}");
            return Task.FromResult(BuildResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, nameof(AABuildBackend))));
        }
    }

    /// <summary>包根必须存在的 AA 清单与 catalog 文件；解析规则由 AAPackageManifestReader 统一持有。</summary>
    public IReadOnlyList<string> RequiredManifestFileNames => AAPackageManifestReader.ResolveRequiredFileNames();

    public void StageBuiltInFiles(BuildRequest request, string stageRoot)
    {
        Debug.Log("[AABuildBackend] 正在暂存 AA 内置包清单...");
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        StageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
    }

    public IReadOnlyList<BundleDownloadItem> LoadStagedBundles(string stageRoot)
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

    public void ApplyStagedBuiltIn(string stageRoot)
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

            Debug.LogWarning($"[{nameof(AABuildBackend)}] Pipeline Task 失败: Code={taskResult.ErrorCode}, Message={taskResult.ErrorMessage}");
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

                return $"AA 管线构建失败于 {taskResult.TaskName}: [{taskResult.ErrorCode}] {taskResult.ErrorMessage}"
                    + $"（已完成 {result.CompletedTasks}/{result.TotalTasks}）";
            }
        }

        return $"AA 管线构建失败。已完成: {result?.CompletedTasks ?? 0}/{result?.TotalTasks ?? 0}";
    }
}
#endif

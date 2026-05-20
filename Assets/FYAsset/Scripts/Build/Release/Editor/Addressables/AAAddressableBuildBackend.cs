#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AA Addressables 构建后端。
/// 通过 AA BuildPipelineConfig 驱动 Task 图执行 Addressables 构建与最终输出。
///
/// 构建流程：DAGScheduler.Execute -> Task 图直接写入最终 AA 包目录 -> 后端 post 方法只做兼容校验。
/// </summary>
public class AAAddressableBuildBackend : IBuildBackend
{
    private BuildContext _context;
    private AAManifest _manifest;
    private string _finalOutputDir;
    private BuildPackageRequest _request;

    /// <summary>
    /// 便捷重载，无额外执行选项。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType)
    {
        return BuildAsync(version, buildType, null);
    }

    /// <summary>
    /// 加载 AA BuildPipelineConfig -> 创建 BuildContext -> DAGScheduler.Execute 执行管线。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType, BuildExecutionOptions options)
    {
        var request = BuildPackageRequest.Create(version, buildType, BackendMode.AAAddressable);
        return BuildAsync(request, options);
    }

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.AAPipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 AA BuildPipelineConfig。", "AAAddressableBuildBackend")));
        BuildPipelineConfigRepair.EnsureAABackboneTasks(config);

        try
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _context = new BuildContext();
            _context.Set(BuildContextKeys.BuildPackageRequest, _request);
            BuildResult result = DAGScheduler.Execute(config, _context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AA 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", "AAAddressableBuildBackend")));
            }

            _manifest = _context.Require<AAManifest>(BuildContextKeys.AAManifest);
            _finalOutputDir = _context.Require<string>(BuildContextKeys.OutputPath);
            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, "AAAddressableBuildBackend")));
        }
    }

    /// <summary>
    /// AA 最终输出已由 Task 图完成；此方法保留为 IBuildBackend 兼容入口并校验输出目录。
    /// </summary>
    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);
        ValidateFinalOutputReady(outputDir);

        Debug.Log($"[AAAddressableBuildBackend] Output 已由 Task 图整理完毕: {outputDir}, Bundles: {_manifest.Bundles.Count}");
    }

    /// <summary>
    /// AA Manifest 已由 TaskWriteAAPackageManifest 写入；此方法保留为 IBuildBackend 兼容入口并校验输出目录。
    /// </summary>
    public void GeneratePackageManifest(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);
        ValidateFinalOutputReady(outputDir);

        ManifestOutputFormat outputFormat = FYAssetSettings.Instance.ManifestOutputFormat;
        Debug.Log($"[AAAddressableBuildBackend] Package Manifest 已由 Task 图生成: {outputDir}, Format: {outputFormat}");
    }

    private void ValidateRequestOutput(string outputDir)
    {
        if (_request == null)
            throw new InvalidOperationException("AA 构建请求尚未就绪，请先调用 BuildAsync。");
        if (!string.Equals(_request.OutputDir, outputDir, StringComparison.Ordinal))
            throw new InvalidOperationException($"AA 输出目录必须来自 BuildPackageRequest。Expected: {_request.OutputDir}, Actual: {outputDir}");
    }

    private void ValidateFinalOutputReady(string outputDir)
    {
        if (string.IsNullOrEmpty(_finalOutputDir))
            throw new InvalidOperationException("AA Task 图输出尚未就绪，请先调用 BuildAsync。");
        if (!string.Equals(_finalOutputDir, outputDir, StringComparison.Ordinal))
            throw new InvalidOperationException($"AA Task 图输出目录不匹配。Expected: {outputDir}, Actual: {_finalOutputDir}");
        if (!FileHelper.DirectoryExists(outputDir))
            throw new InvalidOperationException($"AA 最终输出目录不存在: {outputDir}");
        if (_manifest == null)
            throw new InvalidOperationException("AA Manifest 尚未就绪，请先调用 BuildAsync。");
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

            Debug.LogWarning($"[AAAddressableBuildBackend] {taskResult.ErrorCode}: {taskResult.ErrorMessage}");
        }
    }
}
#endif

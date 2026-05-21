#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 新管线构建后端。
/// 通过 DAGScheduler 驱动 Task 在 BuildProjectManager 统一入口导出
///
/// 构建流程：DAGScheduler.Execute -> Task 图直接写入最终 AB 包目录。
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", "ABBuildBackend")));

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));
            var context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            BuildResult result = DAGScheduler.Execute(config, context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AB 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", "ABBuildBackend")));
            }

            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", "ABBuildBackend")));
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

            Debug.LogWarning($"[ABBuildBackend] {taskResult.ErrorCode}: {taskResult.ErrorMessage}");
        }
    }
}
#endif

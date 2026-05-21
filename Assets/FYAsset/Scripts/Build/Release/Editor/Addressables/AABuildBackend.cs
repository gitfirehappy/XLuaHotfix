#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AA 构建后端。
/// 通过 AA BuildPipelineConfig 驱动 Task 图执行构建与最终输出。
///
/// 构建流程：DAGScheduler.Execute -> Task 图直接写入最终 AA 包目录。
/// </summary>
public class AABuildBackend : IBuildBackend
{
    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.AAPipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 AA BuildPipelineConfig。", nameof(AABuildBackend))));

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
                        $"AA 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", nameof(AABuildBackend))));
            }

            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, nameof(AABuildBackend))));
        }
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

            Debug.LogWarning($"[{nameof(AABuildBackend)}] {taskResult.ErrorCode}: {taskResult.ErrorMessage}");
        }
    }
}
#endif

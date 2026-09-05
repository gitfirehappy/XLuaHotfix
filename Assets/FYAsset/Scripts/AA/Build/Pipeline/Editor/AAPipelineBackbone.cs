using System.Collections.Generic;

/// <summary>
/// AA 构建管线主干定义：主干 Task 名单（同时定义编辑器展示顺序）、
/// 默认 TaskEntry 列表创建与主干完整性校验。通用机制来自 Shared 的 BuildTaskListUtility。
/// </summary>
public static class AAPipelineBackbone
{
    private static readonly string[] BackboneTaskNameArray =
    {
        "TaskScanAAHotfixDiff",
        "TaskMoveAAHotfixGroups",
        "TaskBuildAAContent",
        "TaskOrganizeAAOutput",
        "TaskWriteAAPackageManifest",
        "TaskWritePackageIndex",
        "TaskExportLocalBuildData",
    };

    /// <summary>AA 主干 Task 名单，作为主干校验与编辑器展示顺序的唯一来源。</summary>
    public static IReadOnlyList<string> BackboneTaskNames => BackboneTaskNameArray;

    /// <summary>创建 AA 默认主干 TaskEntry 列表。</summary>
    public static List<TaskEntry> CreateDefaultTasks()
    {
        return BuildTaskListUtility.CreateTasks(BackboneTaskNameArray);
    }

    /// <summary>校验当前配置是否缺少 AA 主干 Task。返回空列表表示通过。</summary>
    public static List<string> GetMissingRequiredTasks(BuildPipelineConfig config)
    {
        return BuildTaskListUtility.GetMissingRequiredTasks(config, BackboneTaskNameArray);
    }
}

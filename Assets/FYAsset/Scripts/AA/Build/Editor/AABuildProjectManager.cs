#if UNITY_EDITOR
/// <summary>
/// AA 具体构建入口。
/// </summary>
public static class AABuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildFullPackage(
            "AA",
            () => new AABuildBackend(),
            options);
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildHotfix(
            "AA",
            () => new AABuildBackend(),
            options);
    }

    public static void ResetGroupsToOriginal()
    {
        RestoreGroupsToOriginal();
    }

    public static HotfixGroupRestoreStatus GetHotfixGroupRestoreStatus()
    {
        return TaskMoveAAHotfixGroups.GetRestoreStatus();
    }

    public static HotfixGroupRestoreResult RestoreGroupsToOriginal()
    {
        HotfixGroupRestoreStatus status = TaskMoveAAHotfixGroups.GetRestoreStatus();
        if (status.PendingCount == 0)
        {
            UnityEngine.Debug.Log("[AABuildProjectManager] 没有待恢复的 AA Hotfix Group 移动记录。");
            return new HotfixGroupRestoreResult
            {
                Message = "No pending AA hotfix group moves to restore."
            };
        }

        bool confirm = UnityEditor.EditorUtility.DisplayDialog("重置分组",
            $"将尝试还原 {status.RestorableCount} 个资源到原分组。\n" +
            $"{status.DefaultGroupFallbackCount} 个资源将在原分组不存在时回退到 DefaultGroup。\n" +
            $"{status.UnrestorableCount} 条无法恢复的记录将保留。\n\n" +
            "此操作通常在构建新的整包前或放弃本次热更时使用。",
            "确定重置", "取消");

        if (!confirm)
            return new HotfixGroupRestoreResult
            {
                InitialPendingCount = status.PendingCount,
                RemainingCount = status.PendingCount,
                Cancelled = true,
                Message = "Restore was cancelled."
            };

        return TaskMoveAAHotfixGroups.Restore();
    }

    public static HotfixGroupRestoreResult DiscardUnrestorableGroupRecords()
    {
        return TaskMoveAAHotfixGroups.DiscardUnrestorableRecords();
    }
}
#endif

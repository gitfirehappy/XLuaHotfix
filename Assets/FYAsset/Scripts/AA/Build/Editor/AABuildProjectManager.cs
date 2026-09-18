#if UNITY_EDITOR
/// <summary>
/// AA 具体构建入口。
/// </summary>
/// <remarks>
/// AA 与 AB 一样使用 attempt 布局：任务链只写 attempt 目录，成功后由 Runner 提升到正式出口。
/// 正式出口是 Packages 下按包名隔离的独立包目录；Hotfix 构建不得改写既有包目录，
/// 否则本次基准解析读到的是被本次构建覆盖过的构建事实。
/// </remarks>
public static class AABuildProjectManager
{
    public static BuildResult BuildFullPackage(BuildExecutionOptions options = null)
        => BuildProjectRunner.BuildFullPackage("AA", () => new AABuildBackend(), options);

    public static BuildResult BuildHotfix(BuildExecutionOptions options = null)
        => BuildProjectRunner.BuildHotfix("AA", () => new AABuildBackend(), options);

    public static void ResetGroupsToOriginal()
    {
        RestoreGroupsToOriginal();
    }

    public static HotfixGroupRestoreStatus GetHotfixGroupRestoreStatus()
    {
        return AAHotfixGroupMover.GetRestoreStatus();
    }

    public static HotfixGroupRestoreResult RestoreGroupsToOriginal()
    {
        HotfixGroupRestoreStatus status = AAHotfixGroupMover.GetRestoreStatus();
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

        return AAHotfixGroupMover.Restore();
    }

    public static HotfixGroupRestoreResult DiscardUnrestorableGroupRecords()
    {
        return AAHotfixGroupMover.DiscardUnrestorableRecords();
    }
}
#endif

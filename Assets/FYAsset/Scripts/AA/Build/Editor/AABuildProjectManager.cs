#if UNITY_EDITOR
/// <summary>
/// AA concrete build entrypoint.
/// </summary>
public static class AABuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildFullPackage(
            BackendMode.AA,
            () => new AABuildBackend(),
            options);
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildHotfix(
            BackendMode.AA,
            () => new AABuildBackend(),
            options);
    }

    public static void ResetGroupsToOriginal()
    {
        RestoreGroupsToOriginal();
    }

    public static HotfixGroupRestoreStatus GetHotfixGroupRestoreStatus()
    {
        return TaskMoveAddressableHotfixGroups.GetRestoreStatus();
    }

    public static HotfixGroupRestoreResult RestoreGroupsToOriginal()
    {
        return BuildProjectRunner.ResetGroupsToOriginal(BackendMode.AA);
    }

    public static HotfixGroupRestoreResult DiscardUnrestorableGroupRecords()
    {
        return TaskMoveAddressableHotfixGroups.DiscardUnrestorableRecords();
    }
}
#endif

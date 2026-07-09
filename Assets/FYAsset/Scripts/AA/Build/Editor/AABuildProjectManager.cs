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
        BuildProjectRunner.ResetGroupsToOriginal(BackendMode.AA);
    }
}
#endif

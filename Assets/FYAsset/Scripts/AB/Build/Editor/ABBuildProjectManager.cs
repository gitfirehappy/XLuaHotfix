#if UNITY_EDITOR

/// <summary>
/// AB concrete build entrypoint.
/// </summary>
public static class ABBuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildFullPackage(
            BackendMode.ABManifest,
            () => new ABBuildBackend(),
            options);
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildHotfix(
            BackendMode.ABManifest,
            () => new ABBuildBackend(),
            options);
    }

    public static void ResetGroupsToOriginal()
    {
        BuildProjectRunner.ResetGroupsToOriginal(BackendMode.ABManifest);
    }
}
#endif

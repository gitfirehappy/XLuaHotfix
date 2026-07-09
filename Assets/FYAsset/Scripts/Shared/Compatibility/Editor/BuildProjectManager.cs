#if UNITY_EDITOR

/// <summary>
/// Compatibility facade for old editor and CLI callers.
/// New backend-specific code should call AABuildProjectManager or ABBuildProjectManager directly.
/// </summary>
public static class BuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            ABBuildProjectManager.BuildFullPackage(options);
            LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
            return;
        }

        AABuildProjectManager.BuildFullPackage(options);
        LastBuildSuccess = AABuildProjectManager.LastBuildSuccess;
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            ABBuildProjectManager.BuildHotfix(options);
            LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
            return;
        }

        AABuildProjectManager.BuildHotfix(options);
        LastBuildSuccess = AABuildProjectManager.LastBuildSuccess;
    }

    public static void ResetGroupsToOriginal()
    {
        if (FYAssetSettings.Instance.UseABBackend)
            ABBuildProjectManager.ResetGroupsToOriginal();
        else
            AABuildProjectManager.ResetGroupsToOriginal();
    }
}
#endif

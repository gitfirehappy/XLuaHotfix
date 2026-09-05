#if UNITY_EDITOR

/// <summary>
/// 旧 Editor 和 CLI 调用方的 compatibility facade。
/// 新增 backend 专用代码应直接调用 AABuildProjectManager 或 ABBuildProjectManager。
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

    public static void BuildStandalonePackage(BuildExecutionOptions options = null)
    {
        // Standalone 当前仅支持 AB 管线
        ABBuildProjectManager.BuildStandalonePackage(options);
        LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
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

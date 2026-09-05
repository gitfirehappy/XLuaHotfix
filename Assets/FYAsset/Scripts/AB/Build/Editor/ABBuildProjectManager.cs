#if UNITY_EDITOR

/// <summary>
/// AB 具体构建入口。
/// </summary>
public static class ABBuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildFullPackage(
            "AB",
            () => new ABBuildBackend(),
            options);
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildHotfix(
            "AB",
            () => new ABBuildBackend(),
            options);
    }

    public static void BuildStandalonePackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = BuildProjectRunner.BuildStandalone(
            "AB",
            () => new ABBuildBackend(),
            options);
    }

    public static void ResetGroupsToOriginal()
    {
        UnityEngine.Debug.LogWarning("[ABBuildProjectManager] ResetGroupsToOriginal 仅适用于 AA 构建链路，AB backend 下已跳过。");
    }
}
#endif

#if UNITY_EDITOR

/// <summary>
/// AB 具体构建入口。
/// </summary>
public static class ABBuildProjectManager
{
    public static BuildResult BuildFullPackage(BuildExecutionOptions options = null)
        => BuildProjectRunner.BuildFullPackage("AB", () => new ABBuildBackend(), options);

    public static BuildResult BuildHotfix(BuildExecutionOptions options = null)
        => BuildProjectRunner.BuildHotfix("AB", () => new ABBuildBackend(), options);

    public static BuildResult BuildStandalonePackage(BuildExecutionOptions options = null)
        => BuildProjectRunner.BuildStandalone("AB", () => new ABBuildBackend(), options);

    public static void ResetGroupsToOriginal()
    {
        UnityEngine.Debug.LogWarning("[ABBuildProjectManager] ResetGroupsToOriginal 仅适用于 AA 构建链路，AB backend 下已跳过。");
    }
}
#endif

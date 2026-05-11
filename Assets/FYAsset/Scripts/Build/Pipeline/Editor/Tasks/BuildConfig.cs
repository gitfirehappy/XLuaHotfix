using UnityEditor;

/// <summary>
/// 构建管线运行环境的不可变快照。
/// TaskPrepareContext 是唯一构建者，下游 Task 只读消费。
/// 通过单 key 收口，DAG W-W 校验自然保护数据来源唯一性。
/// </summary>
public readonly struct BuildConfig
{
    public readonly BackendMode BackendMode;
    public readonly VersionNumber Version;
    public readonly string BuildVersionString;
    public readonly string OutputRoot;
    public readonly BuildTarget TargetPlatform;

    public BuildConfig(BackendMode mode, VersionNumber version, string buildVersionString,
                       string outputRoot, BuildTarget platform)
    {
        BackendMode = mode;
        Version = version;
        BuildVersionString = buildVersionString;
        OutputRoot = outputRoot;
        TargetPlatform = platform;
    }
}

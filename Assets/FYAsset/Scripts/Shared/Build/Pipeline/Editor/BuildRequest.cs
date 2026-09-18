#if UNITY_EDITOR
using System;

/// <summary>
/// 单次构建请求。任务只写入临时目录；BuildProjectRunner 在所有任务成功后将其交付到最终目录。
/// </summary>
public sealed class BuildRequest
{
    public VersionNumber Version { get; }
    public BuildType BuildType { get; }
    public string BackendKey { get; }
    public string PackageName { get; }
    public string TemporaryOutputDir { get; }
    public string FinalOutputDir { get; }
    public string BundlesDir { get; }
    public DateTime CreatedAt { get; }

    private BuildRequest(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        string packageName,
        string temporaryOutputDir,
        string finalOutputDir,
        string bundlesDir,
        DateTime createdAt)
    {
        Version = version;
        BuildType = buildType;
        BackendKey = backendKey;
        PackageName = packageName;
        TemporaryOutputDir = temporaryOutputDir;
        FinalOutputDir = finalOutputDir;
        BundlesDir = bundlesDir;
        CreatedAt = createdAt;
    }

    public static BuildRequest Create(VersionNumber version, BuildType buildType, string backendKey)
    {
        var createdAt = DateTime.UtcNow;
        string packageName = PackageBuildIdentity.CreatePackageName(version, createdAt);
        string finalOutputDir = ResolveFinalOutputDir(buildType, packageName);
        string temporaryOutputDir = BuildPathManager.GetTemporaryPackageDir(packageName);
        return new BuildRequest(
            version,
            buildType,
            backendKey,
            packageName,
            temporaryOutputDir,
            finalOutputDir,
            BuildPathManager.GetBundlesDir(temporaryOutputDir),
            createdAt);
    }

    private static string ResolveFinalOutputDir(BuildType buildType, string packageName)
    {
        return buildType == BuildType.Standalone
            ? BuildPathManager.StandalonePackageDir
            : BuildPathManager.GetPackageDir(packageName);
    }
}
#endif

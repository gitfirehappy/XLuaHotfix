#if UNITY_EDITOR
using System;

/// <summary>
/// 单次构建包请求。
/// BuildProjectManager 是正式 release flow 的权威创建者；后端和 Task 只消费，不重新计算包名或最终输出路径。
/// </summary>
public sealed class BuildPackageRequest
{
    public const string PackageTimestampFormat = "yyyyMMddHHmmss";

    public VersionNumber Version { get; }
    public BuildType BuildType { get; }
    public string BackendKey { get; }
    public string PackageName { get; }
    public string OutputDir { get; }
    public string BundlesDir { get; }
    public string PackageIndexPath { get; }
    public DateTime CreatedAt { get; }

    private BuildPackageRequest(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        string packageName,
        string outputDir,
        string bundlesDir,
        string packageIndexPath,
        DateTime createdAt)
    {
        Version = version;
        BuildType = buildType;
        BackendKey = backendKey;
        PackageName = packageName;
        OutputDir = outputDir;
        BundlesDir = bundlesDir;
        PackageIndexPath = packageIndexPath;
        CreatedAt = createdAt;
    }

    public static BuildPackageRequest Create(VersionNumber version, BuildType buildType, string backendKey)
    {
        var createdAt = DateTime.UtcNow;
        string packageName = CreatePackageName(version, createdAt);
        string outputDir = buildType == BuildType.Standalone
            ? BuildPathManager.StandalonePackageDir
            : BuildPathManager.GetPackageDir(packageName);
        return new BuildPackageRequest(
            version,
            buildType,
            backendKey,
            packageName,
            outputDir,
            BuildPathManager.GetBundlesDir(outputDir),
            BuildPathManager.PackageIndexPath,
            createdAt);
    }

    public static string CreatePackageName(VersionNumber version, DateTime createdAt)
    {
        return $"Build_{createdAt.ToString(PackageTimestampFormat)}_{version.GetReleaseVersionString()}";
    }
}
#endif

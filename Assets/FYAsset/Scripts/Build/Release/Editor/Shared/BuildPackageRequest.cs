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
    public BackendMode BackendMode { get; }
    public string PackageName { get; }
    public string OutputDir { get; }
    public string BundlesDir { get; }
    public string PackageIndexPath { get; }
    public DateTime CreatedAt { get; }

    private BuildPackageRequest(
        VersionNumber version,
        BuildType buildType,
        BackendMode backendMode,
        string packageName,
        string outputDir,
        string bundlesDir,
        string packageIndexPath,
        DateTime createdAt)
    {
        Version = version;
        BuildType = buildType;
        BackendMode = backendMode;
        PackageName = packageName;
        OutputDir = outputDir;
        BundlesDir = bundlesDir;
        PackageIndexPath = packageIndexPath;
        CreatedAt = createdAt;
    }

    public static BuildPackageRequest Create(VersionNumber version, BuildType buildType, BackendMode backendMode)
    {
        var createdAt = DateTime.UtcNow;
        string packageName = CreatePackageName(version, createdAt);
        string outputDir = BuildPathManager.GetPackageDir(packageName);
        return new BuildPackageRequest(
            version,
            buildType,
            backendMode,
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

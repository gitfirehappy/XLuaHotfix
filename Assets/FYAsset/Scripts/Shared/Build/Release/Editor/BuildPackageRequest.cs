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
    /// <summary>
    /// 任务链的写入目录。attempt 布局下是 attempt 目录（仅 Runner finalize 可读它并 promote）；
    /// 非 attempt 布局（AA 现状）保持最终包目录语义不变。
    /// </summary>
    public string OutputDir { get; }
    public string BundlesDir { get; }
    public string PackageIndexPath { get; }
    public DateTime CreatedAt { get; }

    /// <summary>attempt 布局：任务产物尚未交付，live 目录应保持零变化。</summary>
    public bool IsAttemptLayout { get; }

    /// <summary>
    /// 本次交付的最终输出目录（Runner finalize promote 的落地路径）。
    /// 非 attempt 布局等于 <see cref="OutputDir"/>。Standalone 恒为 StreamingAssets/Standalone；
    /// Full/Hotfix 为 HotfixOutput 下该包名的最终目录。
    /// </summary>
    public string DeliveryOutputDir { get; }

    private BuildPackageRequest(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        string packageName,
        string outputDir,
        string bundlesDir,
        string packageIndexPath,
        DateTime createdAt,
        bool attemptLayout,
        string deliveryOutputDir)
    {
        Version = version;
        BuildType = buildType;
        BackendKey = backendKey;
        PackageName = packageName;
        OutputDir = outputDir;
        BundlesDir = bundlesDir;
        PackageIndexPath = packageIndexPath;
        CreatedAt = createdAt;
        IsAttemptLayout = attemptLayout;
        DeliveryOutputDir = deliveryOutputDir;
    }

    /// <summary>
    /// 派生一份“已交付”请求：OutputDir 指向最终出口、不再是 attempt 布局。
    /// 仅 Runner finalize 在 promote 成功后使用，供本地数据导出 / 报表等消费者读取最终路径。
    /// </summary>
    public BuildPackageRequest WithPromotedOutput()
    {
        if (!IsAttemptLayout)
            return this;
        return new BuildPackageRequest(
            Version,
            BuildType,
            BackendKey,
            PackageName,
            DeliveryOutputDir,
            BuildPathManager.GetBundlesDir(DeliveryOutputDir),
            PackageIndexPath,
            CreatedAt,
            attemptLayout: false,
            DeliveryOutputDir);
    }

    /// <param name="attemptLayout">true 时任务链所有产物只写入 attempt 目录；最终出口由 Runner finalize 决定。</param>
    public static BuildPackageRequest Create(VersionNumber version, BuildType buildType, string backendKey, bool attemptLayout = false)
    {
        var createdAt = DateTime.UtcNow;
        string packageName = CreatePackageName(version, createdAt);
        string deliveryOutputDir = buildType == BuildType.Standalone
            ? BuildPathManager.StandalonePackageDir
            : BuildPathManager.GetPackageDir(packageName);
        string outputDir = attemptLayout
            ? BuildPathManager.GetAttemptPackageDir(packageName)
            : deliveryOutputDir;
        return new BuildPackageRequest(
            version,
            buildType,
            backendKey,
            packageName,
            outputDir,
            BuildPathManager.GetBundlesDir(outputDir),
            BuildPathManager.PackageIndexPath,
            createdAt,
            attemptLayout,
            deliveryOutputDir);
    }

    public static string CreatePackageName(VersionNumber version, DateTime createdAt)
    {
        return $"Build_{createdAt.ToString(PackageTimestampFormat)}_{version.GetReleaseVersionString()}";
    }
}
#endif

#if UNITY_EDITOR
using System;

/// <summary>
/// 单次构建包请求。包名与最终输出路径只在创建时计算一次；后端和 Task 只消费，不重新计算。
/// </summary>
/// <remarks>
/// 输出语义（计划 T2）：
/// 1. Full / Hotfix / Standalone 都交付到按包名隔离的独立不可变目录，包名即目录名；
/// 2. Hotfix 包内是“完整目标 Manifest + 相对作用域最近成功 Full 的变化内容”，不依赖前一个 Hotfix 包；
/// 3. 构建不写 PackageIndex：PackageIndex 由发布器在发布事务最后生成并上传。
/// </remarks>
public sealed class BuildPackageRequest
{
    public const string PackageTimestampFormat = "yyyyMMddHHmmss";

    public VersionNumber Version { get; }
    public BuildType BuildType { get; }
    public string BackendKey { get; }
    public string PackageName { get; }

    /// <summary>
    /// 任务链的写入目录。attempt 布局下仅 Runner finalize 可读它并 promote；
    /// 非 attempt 布局时该目录即最终包目录。
    /// </summary>
    public string OutputDir { get; }

    public string BundlesDir { get; }
    public DateTime CreatedAt { get; }

    /// <summary>attempt 布局：任务产物尚未交付，live 目录应保持零变化。</summary>
    public bool IsAttemptLayout { get; }

    /// <summary>
    /// 本次交付的最终输出目录（Runner finalize promote 的落地路径）。
    /// 非 attempt 布局等于 <see cref="OutputDir"/>。Standalone 恒为 StreamingAssets/Standalone；
    /// Full 与 Hotfix 都是 Packages 下该包名的独立目录。
    /// 它同时是发布流程的源包目录。
    /// </summary>
    public string DeliveryOutputDir { get; }

    /// <summary>发布源目录：都是本包名的独立包目录。</summary>
    public string PublishSourceDir => DeliveryOutputDir;

    private BuildPackageRequest(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        string packageName,
        string outputDir,
        string bundlesDir,
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
            CreatedAt,
            attemptLayout: false,
            DeliveryOutputDir);
    }

    /// <param name="attemptLayout">true 时任务链所有产物只写入 attempt 目录；最终出口由 Runner finalize 决定。</param>
    public static BuildPackageRequest Create(VersionNumber version, BuildType buildType, string backendKey,
        bool attemptLayout = false)
    {
        var createdAt = DateTime.UtcNow;
        string packageName = CreatePackageName(version, createdAt);
        string deliveryOutputDir = ResolveDeliveryOutputDir(buildType, backendKey, packageName);
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
            createdAt,
            attemptLayout,
            deliveryOutputDir);
    }

    /// <summary>按构建类型解析最终出口；Full 与 Hotfix 都是独立包目录。</summary>
    public static string ResolveDeliveryOutputDir(BuildType buildType, string backendKey, string packageName)
    {
        return buildType == BuildType.Standalone
            ? BuildPathManager.StandalonePackageDir
            : BuildPathManager.GetPackageDir(packageName);
    }

    public static string CreatePackageName(VersionNumber version, DateTime createdAt)
    {
        return $"Build_{createdAt.ToString(PackageTimestampFormat)}_{version.GetReleaseVersionString()}";
    }
}
#endif

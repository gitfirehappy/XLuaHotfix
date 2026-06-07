/// <summary>
/// BuildContext 键名常量 —— Task 代码中不出现裸字符串，全部引用此静态类。
/// 供 Editor Task 和 Runtime 消费方共同使用，放 Runtime 程序集。
/// </summary>
public static class BuildContextKeys
{
    public const string BuildConfig = "BuildConfig";
    public const string BuildPackageRequest = "BuildPackageRequest";
    public const string BuildType = "BuildType";
    public const string CollectedAssets = "CollectedAssets";
    public const string SharePolicies = "SharePolicies";
    public const string BundleDependencyGraph = "BundleDependencyGraph";
    public const string BundleBuildResults = "BundleBuildResults";
    public const string ABManifest = "ABManifest";
    public const string AAManifest = "AAManifest";
    public const string AAServerDataPath = "AAServerDataPath";
    public const string OutputPath = "OutputPath";
    public const string BuildVerificationResult = "BuildVerificationResult";
    public const string ArtifactDelta = "ArtifactDelta";
    public const string RepositoryArtifacts = "RepositoryArtifacts";
    public const string ABDeliveryBundles = "ABDeliveryBundles";
    public const string RepositoryPreviewOutput = "RepositoryPreviewOutput";
    public const string RepositoryPreviewMode = "RepositoryPreviewMode";
    public const string ABDeliveryPreviewMode = "ABDeliveryPreviewMode";
}

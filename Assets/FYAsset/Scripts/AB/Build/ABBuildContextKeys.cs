/// <summary>
/// AB 构建管线专属的 BuildContext 键名。
/// 中性键名统一在 Shared 的 BuildContextKeys；本类只放 AB 私有契约键。
/// </summary>
public static class ABBuildContextKeys
{
    public const string ABManifest = "ABManifest";
    public const string CollectedAssets = "CollectedAssets";
    public const string SharePolicies = "SharePolicies";
    public const string BundleDependencyGraph = "BundleDependencyGraph";
    public const string BundleBuildResults = "BundleBuildResults";
    public const string ABDeliveryBundles = "ABDeliveryBundles";
    public const string ABDeliveryPreviewMode = "ABDeliveryPreviewMode";
}

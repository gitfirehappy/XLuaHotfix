/// <summary>
/// AB 构建管线专属的 BuildContext 键名。
/// 中性键名统一在 Shared 的 BuildContextKeys；本类只放 AB 私有契约键。
/// </summary>
public static class ABBuildContextKeys
{
    public const string ABManifest = "ABManifest";
    public const string CollectedAssets = "CollectedAssets";
    public const string SharePolicy = "SharePolicy";
    public const string BundleDependencyGraph = "BundleDependencyGraph";
    public const string BundleBuildResults = "BundleBuildResults";

    /// <summary>本次 Hotfix 交付的内容集合；由 ExportABOutputTask 写入，供输出组织与发布读取</summary>
    public const string ABDeliveryContents = "ABDeliveryContents";

    public const string ABDeliveryPreviewMode = "ABDeliveryPreviewMode";

    /// <summary>本次构建的构建配方指纹（ABBuildContentFingerprint.ComputeRecipeFingerprint）；导出阶段写入正式 Summary</summary>
    public const string BuildRecipeFingerprint = "BuildRecipeFingerprint";
}

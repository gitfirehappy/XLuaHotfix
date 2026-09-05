using System;
using System.IO;

/// <summary>
/// 精确包检查结果，不回退到 StreamingAssets。
/// </summary>
public sealed class HotfixPackageInspection
{
    public HotfixVersionInfo VersionInfo { get; }
    public bool IsComplete { get; }
    public string FailureReason { get; }

    private HotfixPackageInspection(HotfixVersionInfo versionInfo, bool isComplete, string failureReason)
    {
        VersionInfo = versionInfo;
        IsComplete = isComplete;
        FailureReason = failureReason ?? string.Empty;
    }

    public static HotfixPackageInspection Inspect(
        string packageRoot,
        PackageIndex expectedIndex,
        HotfixVersionInfo versionInfo,
        bool requiredMetadataPresent,
        string metadataFailureReason,
        bool requirePackageDirectoryMatch = true)
    {
        if (versionInfo == null || versionInfo.Version == null)
            return Incomplete(versionInfo, "包 manifest 缺失或无效。");
        if (expectedIndex == null || string.IsNullOrEmpty(expectedIndex.LatestPackage))
            return Incomplete(versionInfo, "本地 PackageIndex 缺失或无效。");
        if (requirePackageDirectoryMatch
            && !string.Equals(Path.GetFileName(packageRoot), expectedIndex.LatestPackage, StringComparison.Ordinal))
            return Incomplete(versionInfo, "包目录与 PackageIndex.LatestPackage 不匹配。");
        if (expectedIndex.LatestVersion == null || versionInfo.Version != expectedIndex.LatestVersion)
            return Incomplete(versionInfo, "Manifest 版本与 PackageIndex.LatestVersion 不匹配。");
        if (!requiredMetadataPresent)
            return Incomplete(versionInfo, metadataFailureReason);

        var bundles = versionInfo.Bundles;
        if (bundles == null)
            return Incomplete(versionInfo, "Manifest 缺少 Bundle 列表。");

        string bundleRoot = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        if (!HotfixPackageValidator.TryValidateBundleFiles(bundleRoot, bundles, out string error))
            return Incomplete(versionInfo, error);

        return new HotfixPackageInspection(versionInfo, true, string.Empty);
    }

    public static HotfixPackageInspection Incomplete(HotfixVersionInfo versionInfo, string reason)
    {
        return new HotfixPackageInspection(versionInfo, false, reason);
    }
}

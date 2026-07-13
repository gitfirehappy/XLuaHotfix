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
        string metadataFailureReason)
    {
        if (versionInfo == null || versionInfo.Version == null)
            return Incomplete(versionInfo, "包 manifest 缺失或无效。");
        if (expectedIndex == null || string.IsNullOrEmpty(expectedIndex.LatestPackage))
            return Incomplete(versionInfo, "本地 PackageIndex 缺失或无效。");
        if (!string.Equals(Path.GetFileName(packageRoot), expectedIndex.LatestPackage, StringComparison.Ordinal))
            return Incomplete(versionInfo, "包目录与 PackageIndex.LatestPackage 不匹配。");
        if (expectedIndex.LatestVersion == null || versionInfo.Version != expectedIndex.LatestVersion)
            return Incomplete(versionInfo, "Manifest 版本与 PackageIndex.LatestVersion 不匹配。");
        if (!requiredMetadataPresent)
            return Incomplete(versionInfo, metadataFailureReason);

        var bundles = versionInfo.Bundles;
        if (bundles == null)
            return Incomplete(versionInfo, "Manifest 缺少 Bundle 列表。");

        string bundleRoot = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        for (int i = 0; i < bundles.Count; i++)
        {
            BundleDownloadItem bundle = bundles[i];
            if (string.IsNullOrEmpty(bundle.BundleName))
                return Incomplete(versionInfo, $"索引 {i} 的 Bundle 名称为空。");

            string path = FYAssetPathUtility.JoinFilePath(bundleRoot, bundle.BundleName);
            if (!FileHelper.Exists(path))
                return Incomplete(versionInfo, $"缺少 Bundle：{bundle.BundleName}");
            if (bundle.FileSize >= 0 && new FileInfo(path).Length != bundle.FileSize)
                return Incomplete(versionInfo, $"Bundle 大小不匹配：{bundle.BundleName}");
            if (bundle.FileCRC != 0 && HashGenerator.GenerateFileCRC(path) != bundle.FileCRC)
                return Incomplete(versionInfo, $"Bundle CRC 不匹配：{bundle.BundleName}");
        }

        return new HotfixPackageInspection(versionInfo, true, string.Empty);
    }

    public static HotfixPackageInspection Incomplete(HotfixVersionInfo versionInfo, string reason)
    {
        return new HotfixPackageInspection(versionInfo, false, reason);
    }
}

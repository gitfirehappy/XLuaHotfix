using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 构建输出组织 Task — 按 BuildPackageRequest 输出最终 AB 包目录、拷贝 bundle、生成构建摘要、清理临时产物。
/// Full 以 ABManifest.BundleEntries 为拷贝数据源；Hotfix 只拷贝 ABDeliveryBundles。
/// 在 TaskScanABHotfixDiff 之后、TaskWriteABPackageManifest 之前执行。
/// </summary>
public class TaskOrganizeOutput : IBuildTask
{
    public string TaskName => "TaskOrganizeOutput";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);
        var manifest = ctx.Require<ABManifest>(ABBuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<BundleBuildInfo>>(ABBuildContextKeys.BundleBuildResults);
        string outputRoot = cfg.OutputRoot;
        string buildVersion = cfg.BuildVersionString;
        var platform = cfg.TargetPlatform;
        var backendMode = cfg.BackendMode;

        string tempDir = FYAssetPathUtility.JoinFilePath(outputRoot, "_temp");
        string outputDir = request.OutputDir;
        string bundleOutputDir = request.BundlesDir;

        // ① 重建最终输出目录
        if (FileHelper.DirectoryExists(outputDir))
            FileHelper.TryDeleteDirectory(outputDir, true);
        FileHelper.EnsureDirectory(outputDir);
        FileHelper.EnsureDirectory(bundleOutputDir);

        // ② Full 拷贝全量 Bundle；Hotfix 只拷贝 Full-baseline delivery 列表。
        var bundlesToCopy = buildType == BuildType.Hotfix
            ? ctx.Require<List<ManifestBundleEntry>>(ABBuildContextKeys.ABDeliveryBundles)
            : manifest.BundleEntries;
        var copiedFiles = new List<string>();
        foreach (var bundle in bundlesToCopy)
        {
            string srcPath = FYAssetPathUtility.JoinFilePath(tempDir, bundle.BundleName);
            string destPath = FYAssetPathUtility.JoinFilePath(bundleOutputDir, bundle.BundleName);
            if (!FileHelper.Exists(srcPath))
                return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"AB 最终输出缺少 Bundle 文件: '{srcPath}'。", true);

            FileHelper.CopyFile(srcPath, destPath, true);
            copiedFiles.Add(bundle.BundleName);
        }

        // ③ 生成构建摘要
        long totalSize = 0;
        foreach (var b in buildResults)
            totalSize += b.Size;

        var summary = new StringBuilder();
        summary.AppendLine($"Build Version: {buildVersion}");
        summary.AppendLine($"Timestamp: {manifest.BuildTimestamp}");
        summary.AppendLine($"Platform: {platform}");
        summary.AppendLine($"Backend Mode: {backendMode}");
        summary.AppendLine($"Bundles: {manifest.BundleEntries.Count}");
        summary.AppendLine($"Delivery Bundles: {bundlesToCopy.Count}");
        summary.AppendLine($"Total Size: {totalSize} bytes ({totalSize / 1024.0 / 1024.0:F2} MB)");
        summary.AppendLine($"Assets: {manifest.AssetEntries.Count}");
        summary.AppendLine($"Files Copied: {copiedFiles.Count}");

        var verification = ctx.Get<BuildVerificationResult>(BuildContextKeys.BuildVerificationResult);
        if (verification != null)
        {
            summary.AppendLine($"Verification Errors: {verification.ErrorCount}");
            summary.AppendLine($"Verification Warnings: {verification.WarningCount}");
            if (verification.Issues.Count > 0)
            {
                summary.AppendLine("--- Issues ---");
                foreach (var issue in verification.Issues)
                {
                    string scope = issue.BundleName != null ? $" [{issue.BundleName}]" : "";
                    summary.AppendLine($"[{issue.Level}] {issue.CheckName}{scope}: {issue.Message}");
                }
            }
        }

        string summaryPath = FYAssetPathUtility.JoinFilePath(outputDir, "build_summary.txt");
        FileHelper.WriteAllTextAtomic(summaryPath, summary.ToString(), Encoding.UTF8);

        // ④ 清理临时构建产物
        if (FileHelper.DirectoryExists(tempDir))
        {
            try { FileHelper.TryDeleteDirectory(tempDir, true); }
            catch (IOException) { /* best-effort */ }
        }

        // ⑤ 写入 OutputPath；Standalone 的 request 已直接指向 StreamingAssets/Standalone。
        ctx.Set(BuildContextKeys.OutputPath, outputDir);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[ORGANIZE] {copiedFiles.Count}/{manifest.BundleEntries.Count} bundles → {bundleOutputDir}"
        });
    }
}

using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 构建输出组织 Task — 拷贝 bundle、序列化 ABManifest、生成构建摘要、清理临时产物。
/// 以 ABManifest.BundleEntries 为拷贝数据源（不依赖文件扩展名）。
/// 在 TaskVerifyBuildResult 之后执行。
/// </summary>
public class TaskOrganizeOutput : IBuildTask
{
    public string TaskName => "TaskOrganizeOutput";
    public string[] DependsOn => new[] { "TaskVerifyBuildResult" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildConfig,
        BuildContextKeys.ABManifest,
        BuildContextKeys.BundleBuildResults
    };
    public string[] WriteKeys => new[] { BuildContextKeys.OutputPath };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var manifest = ctx.Require<ABManifest>(BuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<BundleBuildInfo>>(BuildContextKeys.BundleBuildResults);
        string outputRoot = cfg.OutputRoot;
        string buildVersion = cfg.BuildVersionString;
        var platform = cfg.TargetPlatform;
        var backendMode = cfg.BackendMode;

        string tempDir = Path.Combine(outputRoot, "_temp");
        string outputDir = Path.Combine(outputRoot, buildVersion);

        // ① 创建输出目录
        if (!FileHelper.DirectoryExists(outputDir))
            FileHelper.EnsureDirectory(outputDir);

        // ② 以 ABManifest.BundleEntries 为源拷贝所有输出文件
        var copiedFiles = new List<string>();
        foreach (var bundle in manifest.BundleEntries)
        {
            string srcPath = Path.Combine(tempDir, bundle.BundleName);
            string destPath = Path.Combine(outputDir, bundle.BundleName);
            if (File.Exists(srcPath))
            {
                FileHelper.CopyFile(srcPath, destPath, true);
                copiedFiles.Add(bundle.BundleName);
            }
        }

        // ③ 序列化 ABManifest
        string manifestPath = Path.Combine(outputDir, "ABManifest.json");
        FileHelper.WriteAllTextAtomic(manifestPath, manifest.SerializeToJson(), Encoding.UTF8);

        // ④ 生成构建摘要
        long totalSize = 0;
        foreach (var b in buildResults)
            totalSize += b.Size;

        var summary = new StringBuilder();
        summary.AppendLine($"Build Version: {buildVersion}");
        summary.AppendLine($"Timestamp: {manifest.BuildTimestamp}");
        summary.AppendLine($"Platform: {platform}");
        summary.AppendLine($"Backend Mode: {backendMode}");
        summary.AppendLine($"Bundles: {manifest.BundleEntries.Count}");
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

        string summaryPath = Path.Combine(outputDir, "build_summary.txt");
        FileHelper.WriteAllTextAtomic(summaryPath, summary.ToString(), Encoding.UTF8);

        // ⑤ 清理临时构建产物
        if (FileHelper.DirectoryExists(tempDir))
        {
            try { FileHelper.TryDeleteDirectory(tempDir, true); }
            catch (IOException) { /* best-effort */ }
        }

        // ⑥ 写入 OutputPath
        ctx.Set(BuildContextKeys.OutputPath, outputDir);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[ORGANIZE] {copiedFiles.Count} files → {outputDir}"
        });
    }
}

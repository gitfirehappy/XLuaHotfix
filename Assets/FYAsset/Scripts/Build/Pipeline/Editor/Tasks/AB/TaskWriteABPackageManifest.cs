using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// AB Manifest 发布 Task — 按 ManifestOutputFormat 写入最终包目录中的 JSON / Binary manifest。
/// 在 TaskOrganizeOutput 之后执行。
/// </summary>
public class TaskWriteABPackageManifest : IBuildTask
{
    public string TaskName => "TaskWriteABPackageManifest";
    public string[] DependsOn => new[] { "TaskOrganizeOutput" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.ABManifest,
        BuildContextKeys.OutputPath
    };
    public string[] WriteKeys => new string[0];

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var manifest = ctx.Require<ABManifest>(BuildContextKeys.ABManifest);
        string outputPath = ctx.Require<string>(BuildContextKeys.OutputPath);
        if (!string.Equals(outputPath, request.OutputDir, System.StringComparison.Ordinal))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AB Manifest 输出目录必须来自 BuildPackageRequest。Expected: {request.OutputDir}, Actual: {outputPath}", true);

        long totalSize = 0;
        for (int i = 0; i < manifest.BundleEntries.Count; i++)
            totalSize += manifest.BundleEntries[i].FileSize;

        if (!HotfixPackageSizeGuard.ValidateOrAbort(totalSize, request.BackendMode, nameof(TaskWriteABPackageManifest)))
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                "AB 热更包大小超过阈值，Manifest 发布已中止。", true);

        ManifestOutputFormat outputFormat = FYAssetBuildSettingsProvider.GetManifestOutputFormat(request.BackendMode);
        string manifestPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.MANIFEST_FILE_NAME);
        string manifestBinPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN);

        FileHelper.EnsureDirectory(request.OutputDir);

        if (outputFormat != ManifestOutputFormat.BinaryOnly)
        {
            FileHelper.WriteAllTextAtomic(manifestPath, manifest.SerializeToJson(), Encoding.UTF8);
        }
        else
        {
            FileHelper.TryDelete(manifestPath);
        }

        if (outputFormat != ManifestOutputFormat.JsonOnly)
        {
            SerializationUtility.WriteToFile(manifestBinPath, manifest, "binary", false);
        }
        else
        {
            FileHelper.TryDelete(manifestBinPath);
        }

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AB MANIFEST] JSON: {outputFormat != ManifestOutputFormat.BinaryOnly}, Binary: {outputFormat != ManifestOutputFormat.JsonOnly}"
        });
    }
}

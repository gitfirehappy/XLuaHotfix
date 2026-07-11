using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// AA Manifest 发布 Task — 扫描最终包目录中的 bundles，写入 AAManifest JSON / Binary。
/// </summary>
public class TaskWriteAAPackageManifest : IBuildTask
{
    public string TaskName => "TaskWriteAAPackageManifest";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        string outputPath = ctx.Require<string>(BuildContextKeys.OutputPath);
        if (!string.Equals(outputPath, request.OutputDir, StringComparison.Ordinal))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AA Manifest 输出目录必须来自 BuildPackageRequest。Expected: {request.OutputDir}, Actual: {outputPath}", true);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return BuildTaskResult.Fail(BuildErrorCodes.SettingNull,
                "AddressableAssetSettings 为空。", true);

        var manifest = new AAManifest
        {
            Version = request.Version,
            Bundles = new List<BundleInfo>()
        };

        var indexData = AAAssetIndexBuilder.Build(settings);
        manifest.AssetEntries = indexData.AssetEntries;
        manifest.KeysByType = indexData.KeysByType;
        manifest.KeysByLabel = indexData.KeysByLabel;

        string bundlesDir = request.BundlesDir;
        if (!FileHelper.DirectoryExists(bundlesDir))
            return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                $"AA bundles 目录不存在: '{bundlesDir}'。", true);

        string[] files = FileHelper.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            if (!file.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileInfo = new FileInfo(file);
            var bundleInfo = new BundleInfo
            {
                BundleName = Path.GetFileName(file),
                FileHash = HashGenerator.GenerateFileHash(file),
                FileCRC = HashGenerator.GenerateFileCRC(file),
                FileSize = fileInfo.Length
            };

            manifest.Bundles.Add(bundleInfo);
            manifest.TotalSize += bundleInfo.FileSize;
        }

        if (!HotfixPackageSizeGuard.ValidateOrAbort(manifest.TotalSize, request.BackendMode, nameof(TaskWriteAAPackageManifest)))
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                "AA 热更包大小超过阈值，Manifest 发布已中止。", true);

        string jsonSavePath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binSavePath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        string tempManifestPath = jsonSavePath + ".tmp";

        FileHelper.TryDelete(tempManifestPath);
        SerializationUtility.WriteToFile(tempManifestPath, manifest);
        manifest.FileHash = HashGenerator.GenerateFileHash(tempManifestPath);
        FileHelper.TryDelete(tempManifestPath);

        ManifestOutputFormat outputFormat = FYAssetBuildSettingsProvider.GetManifestOutputFormat(request.BackendMode);
        if (outputFormat != ManifestOutputFormat.BinaryOnly)
            SerializationUtility.WriteToFile(jsonSavePath, manifest);
        else
            FileHelper.TryDelete(jsonSavePath);

        if (outputFormat != ManifestOutputFormat.JsonOnly)
            SerializationUtility.WriteToFile(binSavePath, manifest, "binary", false);
        else
            FileHelper.TryDelete(binSavePath);

        ctx.Set(BuildContextKeys.AAManifest, manifest);
        Debug.Log($"[TaskWriteAAPackageManifest] AAManifest 已生成: {request.OutputDir}, Bundles: {manifest.Bundles.Count}");

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AA MANIFEST] Bundles: {manifest.Bundles.Count}, TotalSize: {manifest.TotalSize}"
        });
    }
}

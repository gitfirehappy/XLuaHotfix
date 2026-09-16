using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// AA 构建管线：生成 AA 清单。
/// 先把 Addressables 直接写入的 catalog 规范化为 catalog.json，
/// 再扫描最终包目录中的 bundle 事实，写出 AAManifest（JSON / Binary 按设置）。
/// </summary>
/// <remarks>
/// 吸收原 TaskOrganizeAAOutput 与 TaskWriteAAPackageManifest 的职责：
/// catalog 规范化与清单生成必须在同一阶段完成，否则清单可能对应未规范化的目录。
/// 文件 Hash / CRC / Size 全部取磁盘事实，不采信构建过程的中间值。
/// </remarks>
public class GenerateAAManifestTask : IBuildTask
{
    public string TaskName => "GenerateAAManifest";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildRequest>(BuildContextKeys.BuildRequest);

        try
        {
            AABuildOutputOrganizer.NormalizeBuildOutput(request.OutputDir);
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                $"AA catalog 规范化失败: {ex.Message}", true);
        }

        ctx.Set(BuildContextKeys.OutputPath, request.OutputDir);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return BuildTaskResult.Fail(BuildErrorCodes.SettingNull,
                "AddressableAssetSettings 为空。", true);

        var manifest = new AAManifest
        {
            Version = request.Version,
            Bundles = new List<BundleInfo>()
        };

        AAAssetIndexData indexData = AAAssetIndexBuilder.Build(settings);
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

        if (!FileHelper.IsWithinSizeLimit(manifest.TotalSize, FYAssetAASettings.Instance.MaxHotfixSizeBytes))
        {
            string message = $"AA 热更包大小超过阈值: {FileHelper.FormatBytes(manifest.TotalSize)} >= {FileHelper.FormatBytes(FYAssetAASettings.Instance.MaxHotfixSizeBytes)}";
            Debug.LogWarning($"[{nameof(GenerateAAManifestTask)}] {message}");
            if (Application.isBatchMode)
                throw new InvalidOperationException(message);
            EditorUtility.DisplayDialog("AA 热更包过大", message, "OK");
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                "AA 热更包大小超过阈值，Manifest 发布已中止。", true);
        }

        string jsonSavePath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binSavePath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        string tempManifestPath = jsonSavePath + ".tmp";

        FileHelper.TryDelete(tempManifestPath);
        SerializationUtility.WriteToFile(tempManifestPath, manifest);
        manifest.FileHash = HashGenerator.GenerateFileHash(tempManifestPath);
        FileHelper.TryDelete(tempManifestPath);

        ManifestOutputFormat outputFormat = FYAssetAASettings.Instance.ManifestOutputFormat;
        if (outputFormat != ManifestOutputFormat.BinaryOnly)
            SerializationUtility.WriteToFile(jsonSavePath, manifest);
        else
            FileHelper.TryDelete(jsonSavePath);

        if (outputFormat != ManifestOutputFormat.JsonOnly)
            SerializationUtility.WriteToFile(binSavePath, manifest, "binary", false);
        else
            FileHelper.TryDelete(binSavePath);

        ctx.Set(AABuildContextKeys.AAManifest, manifest);
        Debug.Log($"[{nameof(GenerateAAManifestTask)}] AAManifest 已生成: {request.OutputDir}, Bundles: {manifest.Bundles.Count}");

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AA MANIFEST] Bundles: {manifest.Bundles.Count}, TotalSize: {manifest.TotalSize}",
            $"[AA MANIFEST] Catalog normalized in {request.OutputDir}"
        });
    }
}

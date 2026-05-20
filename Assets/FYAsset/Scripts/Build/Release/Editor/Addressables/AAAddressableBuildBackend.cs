#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA Addressables 构建后端。
/// 保留 Addressables 构建链路，并生成 AA package manifest。
///
/// 构建流程：配置 AddressableAssetSettings -> AddressableAssetSettings.BuildPlayerContent ->
/// 从 ServerData 目录搬运产物 -> 生成 AAManifest.json/.bin。
/// </summary>
public class AAAddressableBuildBackend : IBuildBackend
{
    private string _serverDataPath;
    private string _lastOutputDir;
    private int _bundleCount;
    private BuildPackageRequest _request;

    /// <summary>
    /// 便捷重载，无额外执行选项。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType)
    {
        return BuildAsync(version, buildType, null);
    }

    /// <summary>
    /// 配置 AddressableAssetSettings -> 执行 Addressables BuildPlayerContent -> 记录 ServerData 路径。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType, BuildExecutionOptions options)
    {
        var request = BuildPackageRequest.Create(version, buildType, BackendMode.AAAddressable);
        return BuildAsync(request, options);
    }

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "AddressableAssetSettings 为空。", "AAAddressableBuildBackend")));

        try
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            ConfigureBasicSettings(settings);
            AssetDatabase.Refresh();

            _serverDataPath = BuildPathManager.GetServerDataDir();
            AddressablesBuildOutputOrganizer.CleanServerData(_serverDataPath);
            Debug.Log("[AAAddressableBuildBackend] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed, result.Error, "AAAddressableBuildBackend")));

            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, "AAAddressableBuildBackend")));
        }
    }

    /// <summary>
    /// 从 ServerData 目录搬运产物到目标发布目录，统计 .bundle 文件数量。
    /// </summary>
    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);

        if (string.IsNullOrEmpty(_serverDataPath))
            throw new InvalidOperationException("AA 构建输出尚未就绪，请先调用 BuildAsync。");

        AddressablesBuildOutputOrganizer.OrganizeBuildOutput(_serverDataPath, outputDir);
        _lastOutputDir = outputDir;

        string bundlesDir = BuildPathManager.GetBundlesDir(outputDir);
        _bundleCount = Directory.Exists(bundlesDir)
            ? Directory.GetFiles(bundlesDir, "*.bundle", SearchOption.TopDirectoryOnly).Length
            : 0;
        Debug.Log($"[AAAddressableBuildBackend] Output 整理完毕: {outputDir}, Bundles: {_bundleCount}");
    }

    /// <summary>
    /// 扫描输出目录生成 AAManifest.json/.bin（含 BundleName / Hash / CRC / Size 列表和 AA 资产索引）。
    /// 如热更包总大小超阈值则在 BatchMode 下抛异常，编辑器模式下弹窗警告。
    /// </summary>
    public void GeneratePackageManifest(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);

        Debug.Log("[AAAddressableBuildBackend] 正在生成 AAManifest.json...");

        var manifest = new AAManifest
        {
            Version = version,
            Bundles = new List<BundleInfo>()
        };

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var indexData = AAAssetIndexBuilder.Build(settings);
        manifest.AssetEntries = indexData.AssetEntries;
        manifest.KeysByType = indexData.KeysByType;
        manifest.KeysByLabel = indexData.KeysByLabel;

        string bundlesDir = BuildPathManager.GetBundlesDir(outputDir);
        if (Directory.Exists(bundlesDir))
        {
            var files = Directory.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
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
        }

        if (!HotfixPackageSizeGuard.ValidateOrAbort(manifest.TotalSize, "AAAddressableBuildBackend"))
            return;

        string jsonSavePath = Path.Combine(outputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binSavePath = Path.Combine(outputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        string tempManifestPath = jsonSavePath + ".tmp";

        FileHelper.TryDelete(tempManifestPath);

        SerializationUtility.WriteToFile(tempManifestPath, manifest);
        manifest.FileHash = HashGenerator.GenerateFileHash(tempManifestPath);
        FileHelper.TryDelete(tempManifestPath);

        ManifestOutputFormat outputFormat = FYAssetSettings.Instance.ManifestOutputFormat;
        if (outputFormat != ManifestOutputFormat.BinaryOnly)
            SerializationUtility.WriteToFile(jsonSavePath, manifest);
        else
            FileHelper.TryDelete(jsonSavePath);

        if (outputFormat != ManifestOutputFormat.JsonOnly)
            SerializationUtility.WriteToFile(binSavePath, manifest, "binary", false);
        else
            FileHelper.TryDelete(binSavePath);

        Debug.Log($"[AAAddressableBuildBackend] Package Manifest 已生成: {_lastOutputDir ?? outputDir}, Bundles: {_bundleCount}, JSON: {jsonSavePath}, Binary: {binSavePath}");
        Debug.Log($"[AAAddressableBuildBackend] AAManifest.json 生成完毕。Hash: {manifest.FileHash} BundleSize: {manifest.TotalSize}");
    }

    /// <summary>
    /// 配置 AddressableAssetSettings 基本参数。
    /// </summary>
    private static void ConfigureBasicSettings(AddressableAssetSettings settings)
    {
        settings.BuildRemoteCatalog = true;
        settings.OverridePlayerVersion = "addressables_content_state";

        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;

            if (group.Name == "Built In Data" || group.HasSchema<PlayerDataGroupSchema>())
            {
                if (group.HasSchema<BundledAssetGroupSchema>())
                {
                    Debug.LogWarning($"[AAAddressableBuildBackend] 修复冲突：移除 {group.Name} 中错误的 BundledAssetGroupSchema");
                    group.RemoveSchema<BundledAssetGroupSchema>();
                    EditorUtility.SetDirty(group);
                }
                continue;
            }

            var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            if (schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel)
            {
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
                EditorUtility.SetDirty(group);
            }

            if (group.Name == "LuaScripts")
                SetSchemaPathToRemote(settings, schema);
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// 将 BundledAssetGroupSchema 路径修正为 Remote。
    /// </summary>
    private static void SetSchemaPathToRemote(AddressableAssetSettings settings, BundledAssetGroupSchema schema)
    {
        bool changed = false;

        if (schema.BuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath)
        {
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            changed = true;
        }

        if (schema.LoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
        {
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            changed = true;
        }

        if (changed)
            Debug.Log($"[AAAddressableBuildBackend] 已将 Schema 路径修正为 Remote: {schema.Group.Name}");
    }

    private void ValidateRequestOutput(string outputDir)
    {
        if (_request == null)
            throw new InvalidOperationException("AA 构建请求尚未就绪，请先调用 BuildAsync。");
        if (!string.Equals(_request.OutputDir, outputDir, StringComparison.Ordinal))
            throw new InvalidOperationException($"AA 输出目录必须来自 BuildPackageRequest。Expected: {_request.OutputDir}, Actual: {outputDir}");
    }
}
#endif

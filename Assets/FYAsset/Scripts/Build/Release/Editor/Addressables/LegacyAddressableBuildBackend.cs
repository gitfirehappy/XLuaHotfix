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
/// Legacy Addressables 构建后端。
/// 保留 Addressables 构建链路，并生成 Legacy AA package manifest。
///
/// 构建流程：配置 AddressableAssetSettings -> AddressableAssetSettings.BuildPlayerContent ->
/// 从 ServerData 目录搬运产物 -> 生成 AAManifest.json/.bin。
/// </summary>
public class LegacyAddressableBuildBackend : IBuildBackend
{
    private const long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;

    private string _serverDataPath;
    private string _lastOutputDir;
    private int _bundleCount;

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
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "AddressableAssetSettings 为空。", "LegacyAddressableBuildBackend")));

        try
        {
            ConfigureBasicSettings(settings);
            AssetDatabase.Refresh();

            BuildPathCustomizer.CleanServerData();
            Debug.Log("[LegacyAddressableBuildBackend] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed, result.Error, "LegacyAddressableBuildBackend")));

            _serverDataPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "ServerData",
                EditorUserBuildSettings.activeBuildTarget.ToString());
            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, "LegacyAddressableBuildBackend")));
        }
    }

    /// <summary>
    /// 从 ServerData 目录搬运产物到目标发布目录，统计 .bundle 文件数量。
    /// </summary>
    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        if (string.IsNullOrEmpty(_serverDataPath))
            throw new InvalidOperationException("Legacy 构建输出尚未就绪，请先调用 BuildAsync。");

        BuildPathCustomizer.OrganizeBuildOutput(_serverDataPath, outputDir);
        _lastOutputDir = outputDir;

        string bundlesDir = Path.Combine(outputDir, "bundles");
        _bundleCount = Directory.Exists(bundlesDir)
            ? Directory.GetFiles(bundlesDir, "*.bundle", SearchOption.TopDirectoryOnly).Length
            : 0;
        Debug.Log($"[LegacyAddressableBuildBackend] Output 整理完毕: {outputDir}, Bundles: {_bundleCount}");
    }

    /// <summary>
    /// 扫描输出目录生成 AAManifest.json/.bin（含 BundleName / Hash / CRC / Size 列表和 AA 资产索引）。
    /// 如热更包总大小超阈值则在 BatchMode 下抛异常，编辑器模式下弹窗警告。
    /// </summary>
    public void GeneratePackageManifest(string outputDir, VersionNumber version)
    {
        Debug.Log("[LegacyAddressableBuildBackend] 正在生成 AAManifest.json...");

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

        string bundlesDir = Path.Combine(outputDir, "bundles");
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

        if (manifest.TotalSize >= MaxHotfixSizeBytes)
        {
            Debug.LogError($"[LegacyAddressableBuildBackend] 热更包大小过大，需缩减大小: {manifest.TotalSize} >= {MaxHotfixSizeBytes}");

            if (Application.isBatchMode)
            {
                Debug.LogError("[LegacyAddressableBuildBackend] BatchMode 下已阻断构建：热更包大小超过阈值。请缩减资源后重试。");
                throw new Exception("热更包大小超过阈值");
            }

            EditorUtility.DisplayDialog(
                "热更包过大",
                $"热更包大小 ({manifest.TotalSize / (1024 * 1024)} MB) 已超过阈值 ({MaxHotfixSizeBytes / (1024 * 1024)} MB)。请缩减资源大小。",
                "OK");
            return;
        }

        string jsonSavePath = Path.Combine(outputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binSavePath = Path.Combine(outputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        string tempManifestPath = jsonSavePath + ".tmp";

        if (File.Exists(tempManifestPath))
            File.Delete(tempManifestPath);

        SerializationUtility.WriteToFile(tempManifestPath, manifest);
        manifest.FileHash = HashGenerator.GenerateFileHash(tempManifestPath);
        File.Delete(tempManifestPath);

        SerializationUtility.WriteToFile(jsonSavePath, manifest);
        SerializationUtility.WriteToFile(binSavePath, manifest, "binary", false);

        Debug.Log($"[LegacyAddressableBuildBackend] Package Manifest 已生成: {_lastOutputDir ?? outputDir}, Bundles: {_bundleCount}, JSON: {jsonSavePath}, Binary: {binSavePath}");
        Debug.Log($"[LegacyAddressableBuildBackend] AAManifest.json 生成完毕。Hash: {manifest.FileHash} BundleSize: {manifest.TotalSize}");
    }

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
                    Debug.LogWarning($"[LegacyAddressableBuildBackend] 修复冲突：移除 {group.Name} 中错误的 BundledAssetGroupSchema");
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
            Debug.Log($"[LegacyAddressableBuildBackend] 已将 Schema 路径修正为 Remote: {schema.Group.Name}");
    }
}
#endif

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
/// 仅搬运原 BuildProjectManager 的 Addressables 构建逻辑，不改变运行行为。
/// </summary>
public class LegacyAddressableBuildBackend : IBuildBackend
{
    private const long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;

    private string _serverDataPath;
    private string _lastOutputDir;
    private int _bundleCount;

    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[LegacyAddressableBuildBackend] AddressableAssetSettings 为空，无法继续构建。");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "AddressableAssetSettings is null.", "LegacyAddressableBuildBackend")));
        }

        try
        {
            ConfigureBasicSettings(settings);
            AssetDatabase.Refresh();

            BuildPathCustomizer.CleanServerData();
            Debug.Log("[LegacyAddressableBuildBackend] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError($"[LegacyAddressableBuildBackend] 构建失败: {result.Error}");
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed, result.Error, "LegacyAddressableBuildBackend")));
            }

            _serverDataPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "ServerData",
                EditorUserBuildSettings.activeBuildTarget.ToString());
            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LegacyAddressableBuildBackend] 构建过程中出现异常: {ex}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, "LegacyAddressableBuildBackend")));
        }
    }

    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        if (string.IsNullOrEmpty(_serverDataPath))
            throw new InvalidOperationException("Legacy build output is not ready. Call BuildAsync first.");

        BuildPathCustomizer.OrganizeBuildOutput(_serverDataPath, outputDir);
        _lastOutputDir = outputDir;

        string bundlesDir = Path.Combine(outputDir, "bundles");
        _bundleCount = Directory.Exists(bundlesDir)
            ? Directory.GetFiles(bundlesDir, "*.bundle", SearchOption.TopDirectoryOnly).Length
            : 0;
        Debug.Log($"[LegacyAddressableBuildBackend] Output organized: {outputDir}, bundles: {_bundleCount}");
    }

    public void GenerateVersionState(string outputDir, VersionNumber version)
    {
        Debug.Log("[LegacyAddressableBuildBackend] 正在生成 version_state.json...");

        var versionState = new VersionState
        {
            Version = version,
            Bundles = new List<BundleInfo>()
        };

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
                    FileSize = fileInfo.Length
                };

                versionState.Bundles.Add(bundleInfo);
                versionState.TotalSize += bundleInfo.FileSize;
            }
        }

        if (versionState.TotalSize >= MaxHotfixSizeBytes)
        {
            Debug.LogError($"[LegacyAddressableBuildBackend] 热更包大小过大，需缩减大小: {versionState.TotalSize} >= {MaxHotfixSizeBytes}");

            if (Application.isBatchMode)
            {
                Debug.LogError("[LegacyAddressableBuildBackend] BatchMode 下已阻断构建：热更包大小超过阈值。请缩减资源后重试。");
                throw new Exception("热更包大小超过阈值");
            }

            EditorUtility.DisplayDialog(
                "热更包过大",
                $"热更包大小 ({versionState.TotalSize / (1024 * 1024)} MB) 已超过阈值 ({MaxHotfixSizeBytes / (1024 * 1024)} MB)。请缩减资源大小。",
                "OK");
            return;
        }

        string savePath = Path.Combine(outputDir, "version_state.json");
        string tempVersionStatePath = savePath + ".tmp";

        if (File.Exists(tempVersionStatePath))
            File.Delete(tempVersionStatePath);

        SerializationUtility.WriteToFile(tempVersionStatePath, versionState);
        versionState.FileHash = HashGenerator.GenerateFileHash(tempVersionStatePath);
        File.Delete(tempVersionStatePath);

        SerializationUtility.WriteToFile(savePath, versionState);
        Debug.Log($"[LegacyAddressableBuildBackend] Version state generated: {_lastOutputDir ?? outputDir}, bundles: {_bundleCount}, version_state: {savePath}");
        Debug.Log($"[LegacyAddressableBuildBackend] version_state.json 生成完毕。Hash: {versionState.FileHash} BundleSize: {versionState.TotalSize}");
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

            if (group.Name == FYAssetConstants.HELPER_BUILD_DATA_GROUP_NAME)
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

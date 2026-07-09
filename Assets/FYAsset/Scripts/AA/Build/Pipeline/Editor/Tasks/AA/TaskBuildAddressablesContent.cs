using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA 构建 Task — 配置 catalog、清理 ServerData、执行 BuildPlayerContent。
/// </summary>
public class TaskBuildAddressablesContent : IBuildTask
{
    public string TaskName => "TaskBuildAddressablesContent";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new[] { BuildContextKeys.BuildPackageRequest };
    public string[] WriteKeys => new[] { BuildContextKeys.AAServerDataPath };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return BuildTaskResult.Fail(BuildErrorCodes.SettingNull,
                "AddressableAssetSettings 为空。", true);

        try
        {
            ConfigureBasicSettings(settings);
            AssetDatabase.Refresh();

            string serverDataPath = BuildPathManager.GetServerDataDir();
            AddressablesBuildOutputOrganizer.CleanServerData(serverDataPath);
            Debug.Log("[TaskBuildAddressablesContent] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, result.Error, true);

            ctx.Set(BuildContextKeys.AAServerDataPath, serverDataPath);
            return BuildTaskResult.Ok(new System.Collections.Generic.List<string>
            {
                $"[AA BUILD] ServerData: {serverDataPath}"
            });
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"Addressables 构建异常: {ex.Message}", true);
        }
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
                    Debug.LogWarning($"[TaskBuildAddressablesContent] 修复冲突：移除 {group.Name} 中错误的 BundledAssetGroupSchema");
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
            Debug.Log($"[TaskBuildAddressablesContent] 已将 Schema 路径修正为 Remote: {schema.Group.Name}");
    }
}

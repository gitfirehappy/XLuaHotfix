using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA 构建 Task — 注入最终输出路径并执行 BuildPlayerContent。
/// </summary>
public class TaskBuildAAContent : IBuildTask
{
    private const string CatalogBuildPathVariable = "FYAsset.CatalogBuildPath";

    public string TaskName => "TaskBuildAAContent";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return BuildTaskResult.Fail(BuildErrorCodes.SettingNull,
                "AddressableAssetSettings 为空。", true);

        try
        {
            ConfigureBasicSettings(settings, request);
            AssetDatabase.Refresh();

            Debug.Log("[TaskBuildAAContent] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, result.Error, true);

            return BuildTaskResult.Ok(new System.Collections.Generic.List<string>
            {
                $"[AA BUILD] Catalog: {request.OutputDir}",
                $"[AA BUILD] Remote bundles: {request.BundlesDir}"
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
    private static void ConfigureBasicSettings(AddressableAssetSettings settings, BuildPackageRequest request)
    {
        settings.BuildRemoteCatalog = true;
        settings.OverridePlayerVersion = "addressables_content_state";
        ProjectConfigData.GenerateBuildLayout = true;

        string catalogBuildPath = FYAssetPathUtility.NormalizePath(request.OutputDir);
        string remoteBuildPath = FYAssetPathUtility.NormalizePath(request.BundlesDir);
        settings.profileSettings.CreateValue(CatalogBuildPathVariable, catalogBuildPath);
        settings.profileSettings.SetValue(settings.activeProfileId, CatalogBuildPathVariable, catalogBuildPath);
        settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath, remoteBuildPath);
        if (!settings.RemoteCatalogBuildPath.SetVariableByName(settings, CatalogBuildPathVariable))
            throw new InvalidOperationException($"Unable to bind RemoteCatalogBuildPath to '{CatalogBuildPathVariable}'.");

        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;

            if (group.Name == "Built In Data" || group.HasSchema<PlayerDataGroupSchema>())
            {
                if (group.HasSchema<BundledAssetGroupSchema>())
                {
                    Debug.LogWarning($"[TaskBuildAAContent] 修复冲突：移除 {group.Name} 中错误的 BundledAssetGroupSchema");
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

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TaskBuildAAContent] Addressables BuildPath 已注入并保存: Catalog={catalogBuildPath}, Remote={remoteBuildPath}");
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
            Debug.Log($"[TaskBuildAAContent] 已将 Schema 路径修正为 Remote: {schema.Group.Name}");
    }
}

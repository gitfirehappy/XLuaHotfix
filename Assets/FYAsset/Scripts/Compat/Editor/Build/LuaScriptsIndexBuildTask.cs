using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 项目胶水层的 LuaScriptsIndex 构建 Task。
/// 骨架管线保持 lua 无关；本 Task 由 BuildPipelineConfig 按名注入。
/// AA：重建索引并注册到 Addressables。
/// AB：按已采集容器重建索引，并把索引资产收编进 CollectedAssets。
/// Preview 模式不改写资产。
/// </summary>
public sealed class LuaScriptsIndexBuildTask : IBuildTask
{
    public string TaskName => "LuaScriptsIndexBuildTask";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        if (ctx.Get<bool>(BuildContextKeys.RepositoryPreviewMode))
            return BuildTaskResult.Ok(new List<string> { "[LUA INDEX] Preview skipped (no asset rewrite)" });

        var request = ctx.Get<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        if (request == null)
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "BuildPackageRequest is null.", true);

        try
        {
            return request.BackendKey == "AA"
                ? ExecuteAA()
                : ExecuteAB(ctx);
        }
        catch (LuaScriptsIndexBuildException ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.LuaIndexInvalid, ex.Message, true);
        }
    }

    private static BuildTaskResult ExecuteAA()
    {
        Debug.Log("[LuaScriptsIndexBuildTask] AA 开始导出 LuaScriptsIndex...");
        AddressableAssetSettings settings = RequireAASettings();
        AddressableAssetGroup group = GetOrCreateGroup(settings, "LuaScripts");
        int containerCount = RebuildFromAddressables(settings);
        EnsureAssetInGroup(
            settings,
            group,
            LuaScriptsIndex.EditorAssetPath,
            LuaScriptsIndex.AssetAddress,
            LuaScriptsIndex.AssetAddress);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        ValidateAAPublishedAssets(settings);
        Debug.Log($"[LuaScriptsIndexBuildTask] AA 导出完成。Containers={containerCount}");
        return BuildTaskResult.Ok(new List<string>
        {
            $"[LUA INDEX] AA containers={containerCount}"
        });
    }

    private static BuildTaskResult ExecuteAB(BuildContext ctx)
    {
        var assets = ctx.Get<List<CollectedAssetInfo>>(ABBuildContextKeys.CollectedAssets);
        if (assets == null || assets.Count == 0)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets,
                "TaskCollectAssets 未产出 Asset。无法构建 LuaScriptsIndex。", true);
        }

        var containerAddresses = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            if (!string.Equals(asset.PrimaryType, nameof(LuaScriptContainer), StringComparison.Ordinal))
                continue;
            containerAddresses[asset.AssetPath] = asset.Address;
        }

        int containerCount = LuaScriptsIndexBuilder.Rebuild(containerAddresses);
        if (!TryAddLuaScriptsIndex(assets, assets[0].PackageName ?? "Default", out bool added, out string error))
            return BuildTaskResult.Fail(BuildErrorCodes.LuaIndexInvalid, error, true);

        ctx.Set(ABBuildContextKeys.CollectedAssets, assets);
        ValidateABPublishedAssets(assets);

        var warnings = new List<string>
        {
            $"[LUA INDEX] AB containers={containerCount}"
        };
        if (added)
            warnings.Add("[BOOTSTRAP] LuaScriptsIndex collected.");
        return BuildTaskResult.Ok(warnings);
    }

    private static AddressableAssetSettings RequireAASettings()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new LuaScriptsIndexBuildException("AddressableAssetSettings 未找到。");
        return settings;
    }

    private static int RebuildFromAddressables(AddressableAssetSettings settings)
    {
        return LuaScriptsIndexBuilder.Rebuild(CollectContainerAddresses(settings));
    }

    private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group == null)
            group = settings.CreateGroup(groupName, false, false, true, null);
        return group;
    }

    private static void EnsureAssetInGroup(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string path,
        string address,
        string label = null)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid))
            throw new LuaScriptsIndexBuildException($"无法取得 LuaScriptsIndex GUID: {path}");

        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;
        if (!string.IsNullOrEmpty(label))
        {
            settings.AddLabel(label, false);
            entry.SetLabel(label, true, true);
        }

        EditorUtility.SetDirty(settings);
    }

    private static Dictionary<string, string> CollectContainerAddresses(AddressableAssetSettings settings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:LuaScriptContainer");
        for (int i = 0; i < guids.Length; i++)
        {
            string guid = guids[i];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var container = AssetDatabase.LoadAssetAtPath<LuaScriptContainer>(path);
            if (container == null)
                continue;

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                Debug.LogWarning($"[LuaScriptsIndexBuildTask] 跳过未注册到 Addressables 的容器: {path}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.address))
                throw new LuaScriptsIndexBuildException($"Addressables 中的 Lua 容器 Address 为空: {path}");
            result[path] = entry.address;
        }

        return result;
    }

    private static void ValidateAAPublishedAssets(AddressableAssetSettings settings)
    {
        AAAssetIndexData indexData = AAAssetIndexBuilder.Build(settings);
        var publishedAssets = new List<LuaScriptsIndexPublishedAsset>(indexData.AssetEntries.Count);
        for (int i = 0; i < indexData.AssetEntries.Count; i++)
        {
            PackageEntry entry = indexData.AssetEntries[i];
            publishedAssets.Add(new LuaScriptsIndexPublishedAsset(entry.key, entry.Type));
        }

        LuaScriptsIndexBuilder.ValidatePublishedAssets(publishedAssets);
    }

    private static bool TryAddLuaScriptsIndex(
        List<CollectedAssetInfo> assets,
        string packageName,
        out bool added,
        out string error)
    {
        added = false;
        error = null;

        var existingGuids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].AssetGUID))
                existingGuids.Add(assets[i].AssetGUID);
        }

        var index = AssetDatabase.LoadAssetAtPath<LuaScriptsIndex>(LuaScriptsIndex.EditorAssetPath);
        if (index == null)
        {
            error = $"LuaScriptsIndex 不存在: {LuaScriptsIndex.EditorAssetPath}";
            return false;
        }

        string guid = AssetDatabase.AssetPathToGUID(LuaScriptsIndex.EditorAssetPath);
        if (string.IsNullOrEmpty(guid))
        {
            error = $"无法取得 LuaScriptsIndex GUID: {LuaScriptsIndex.EditorAssetPath}";
            return false;
        }

        if (existingGuids.Contains(guid))
        {
            for (int i = 0; i < assets.Count; i++)
            {
                if (!string.Equals(assets[i].AssetGUID, guid, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(assets[i].Address, LuaScriptsIndex.AssetAddress, StringComparison.Ordinal))
                {
                    error = $"LuaScriptsIndex 已被采集但 Address 不正确: {assets[i].Address}";
                    return false;
                }

                return true;
            }
        }

        string primaryType = nameof(LuaScriptsIndex);
        assets.Add(new CollectedAssetInfo
        {
            AssetPath = LuaScriptsIndex.EditorAssetPath,
            AssetGUID = guid,
            Address = LuaScriptsIndex.AssetAddress,
            PrimaryType = primaryType,
            Labels = new List<string> { LuaScriptsIndex.AssetAddress },
            GroupLabels = new List<string>(),
            AssetLabels = new List<string> { LuaScriptsIndex.AssetAddress },
            GroupName = SystemIdentifiers.SharedGroupName,
            PackageName = packageName,
            BundleName = BundleNameBuilder.BuildShared(
                packageName,
                "lua-index",
                EPayloadKind.Serialized,
                primaryType),
            BundlePackingMode = BundlePackingMode.PackSeparately,
            Classification = new AssetClassification
            {
                Role = EAssetRole.Main,
                PayloadKind = EPayloadKind.Serialized
            },
            CollectorType = ECollectorType.Main,
            IsInSharedBundle = true,
            IsDuplicated = false
        });
        added = true;
        return true;
    }

    private static void ValidateABPublishedAssets(List<CollectedAssetInfo> assets)
    {
        var publishedAssets = new List<LuaScriptsIndexPublishedAsset>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            publishedAssets.Add(new LuaScriptsIndexPublishedAsset(
                asset.Address,
                asset.PrimaryType,
                asset.AssetPath));
        }

        LuaScriptsIndexBuilder.ValidatePublishedAssets(publishedAssets);
    }
}

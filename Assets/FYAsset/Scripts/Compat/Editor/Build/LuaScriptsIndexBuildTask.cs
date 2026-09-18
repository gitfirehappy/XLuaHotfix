using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 项目胶水层的 LuaScriptsIndex 构建 Task。
/// 骨架管线保持 lua 无关，本 Task 由 BuildPipelineConfig 按名注入。
/// </summary>
/// <remarks>
/// AA：重建索引并注册到 Addressables。
/// AB：在 Collect 后读取冻结快照中的容器 Address 重建索引；索引资产必须已由 Collector 显式采集。
/// 由 BuildPipelineConfig 按名注入到 AA 的 Input 槽与 AB 的 AnalyzeABDependencies 槽之前。
/// </remarks>
public sealed class LuaScriptsIndexBuildTask : IBuildTask
{
    public string TaskName => "LuaScriptsIndexBuildTask";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        var request = ctx.Get<BuildRequest>(BuildContextKeys.BuildRequest);
        if (request == null)
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "BuildRequest is null.", true);

        try
        {
            return request.BackendKey == "AA"
                ? ExecuteAA()
                : ExecuteAB(ctx);
        }
        catch (LuaScriptsIndexBuildException ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, ex.Message, true);
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

    private static BuildTaskResult ExecuteAB(BuildRunContext ctx)
    {
        ABCollectionSnapshot snapshot = ctx.Get<ABCollectionSnapshot>(ABBuildContextKeys.CollectionSnapshot);
        if (snapshot == null || snapshot.CollectedAssets.Count == 0)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets,
                "CollectABAssets 未产出快照。无法构建 LuaScriptsIndex。", true);
        }

        IReadOnlyList<CollectedAssetInfo> assets = snapshot.CollectedAssets;
        var containerAddresses = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectedAssetInfo indexAsset = null;
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            if (string.Equals(asset.AssetPath, LuaScriptsIndex.EditorAssetPath, StringComparison.Ordinal))
                indexAsset = asset;
            if (!string.Equals(asset.AssetType, AssetTypeKey.FromType(typeof(LuaScriptContainer)), StringComparison.Ordinal))
                continue;
            containerAddresses[asset.AssetPath] = asset.Address;
        }

        if (indexAsset == null)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.BuildFailed,
                $"LuaScriptsIndex 必须在 Collector 中显式采集: {LuaScriptsIndex.EditorAssetPath}",
                true);
        }
        if (!string.Equals(indexAsset.Address, LuaScriptsIndex.AssetAddress, StringComparison.Ordinal))
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.BuildFailed,
                $"LuaScriptsIndex Address 必须为 '{LuaScriptsIndex.AssetAddress}'，实际为 '{indexAsset.Address}'。",
                true);
        }

        int containerCount = LuaScriptsIndexBuilder.Rebuild(containerAddresses);
        ValidateABPublishedAssets(assets);
        return BuildTaskResult.Ok(new List<string>
        {
            $"[LUA INDEX] AB containers={containerCount}"
        });
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
        var publishedAssets = new List<LuaScriptsIndexPublishedAsset>();
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null)
                continue;

            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry == null || entry.IsFolder || string.IsNullOrEmpty(entry.address))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                publishedAssets.Add(new LuaScriptsIndexPublishedAsset(
                    entry.address,
                    mainType != null ? mainType.Name : string.Empty,
                    assetPath));
            }
        }

        LuaScriptsIndexBuilder.ValidatePublishedAssets(publishedAssets);
    }

    private static void ValidateABPublishedAssets(IReadOnlyList<CollectedAssetInfo> assets)
    {
        var publishedAssets = new List<LuaScriptsIndexPublishedAsset>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(asset.AssetPath);
            publishedAssets.Add(new LuaScriptsIndexPublishedAsset(
                asset.Address,
                mainType != null ? mainType.Name : string.Empty,
                asset.AssetPath));
        }

        LuaScriptsIndexBuilder.ValidatePublishedAssets(publishedAssets);
    }
}

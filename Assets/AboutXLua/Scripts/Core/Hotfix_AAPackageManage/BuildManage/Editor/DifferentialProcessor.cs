#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// 差异化构建处理器
/// 负责计算差异、临时切换Remote状态、快照轮转
/// </summary>
public static class DifferentialProcessor
{
    private static string SnapShotAssetPath = Constants.SNAPSHOT_ASSET_PATH;

    ///<summary> 缓存用于构建后还原组，GUID ->原Group </summary> 
    private static Dictionary<string, string> _groupCache = new();
    
    /// <summary>
    /// 分析快照差异并临时修改配置
    /// </summary>
    /// <param name="deleteList">要删除的资源列表</param>
    public static bool PrepareHotfix(VersionNumber currentVersion, List<string> deleteList)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var data = GetOrCreateSnapshotData();
        var head = data.GetHead();
        var currentAssets = ScanCurrentProjectAssets(settings);

        if (head == null)
        {
            Debug.LogError("[DiffProcessor] 没有找到基准版本(Head)，无法执行热更构建。请先执行 Build Full Package。");
            return false;
        }
        
        var modifiedAssets = FindModifiedAssets(currentAssets, head, deleteList);

        if (modifiedAssets.Count == 0 && deleteList.Count == 0)
        {
            Debug.Log("[DiffProcessor] 没有修改的资源，无需调整。");
            return false;
        }
        
        // 移动逻辑
        _groupCache.Clear();
        if (modifiedAssets.Count > 0)
        {
            var hotfixGroup = GetOrCreateHotfixGroup(settings);

            foreach (var asset in modifiedAssets)
            {
                _groupCache[asset.AssetGUID] = asset.GroupName;

                var entry = settings.FindAssetEntry(asset.AssetGUID);
                if (entry != null)
                {
                    settings.MoveEntry(entry, hotfixGroup);
                    asset.RemoteGroupName = hotfixGroup.Name;
                }
            }
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        
        // 生成暂存快照
        BuildSnapshot staged = new BuildSnapshot(currentVersion);
        staged.Assets = currentAssets;
        data.StageSnapshot = staged;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"[DiffProcessor] 差异准备完成。{modifiedAssets.Count} 个资源已移动至 {Constants.HOTFIX_GROUP_NAME}。Staged快照已保存。");
        return true;
    }

    /// <summary>
    /// 恢复修改的配置
    /// TODO：需要移除逻辑，并修改
    /// </summary>
    public static void RestoreAfterHotfix()
    {
        if (_groupCache.Count == 0) return;

        Debug.Log("[DiffProcessor] 正在恢复资源位置...");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var hotfixGroup = settings.FindGroup(Constants.HOTFIX_GROUP_NAME);

        List<string> guidsToRemove = new List<string>();

        foreach (var kvp in _groupCache)
        {
            string guid = kvp.Key;
            string originalGroupName = kvp.Value;
            
            var targetGroup = settings.FindGroup(originalGroupName);
            if (targetGroup == null)
            {
                // 如果原组没了，就建一个默认的或者放到Default
                targetGroup = settings.DefaultGroup;
                Debug.LogWarning($"[DiffProcessor] 原分组 {originalGroupName} 不存在，移动至默认组。");
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry != null)
            {
                settings.MoveEntry(entry, targetGroup);
            }
            guidsToRemove.Add(guid);
        }
        
        _groupCache.Clear();

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[DiffProcessor] 资源位置已恢复。");
    }

    /// <summary>
    /// 确认发布上线热更包，更新快照列表和 Head
    /// TODO: 此处添加按钮或在上层封装
    /// </summary>
    public static void ConfirmRelease()
    {
        var data = GetOrCreateSnapshotData();
        if (data.StageSnapshot == null)
        {
            EditorUtility.DisplayDialog("提示", "当前没有待发布的暂存快照 (Staged Snapshot)。请先构建热更包。", "OK");
            return;
        }

        // 将 Staged 转正，一定是hasUpdated=true
        foreach(var asset in data.StageSnapshot.Assets)
        {
            asset.hasUpdated = true;
        }
        data.Snapshots.Add(data.StageSnapshot);
        data.HeadIndex = data.Snapshots.Count - 1;
        
        // 打印日志
        string versionStr = data.StageSnapshot.Version.GetVersionString(); 
        Debug.Log($"[DiffProcessor] 版本 {versionStr} 已确认为 Head。");

        // 清空 Staged
        data.StageSnapshot = null;
        
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        
        EditorUtility.DisplayDialog("成功", $"版本 {versionStr} 已确认为基准！", "OK");
    }

    /// <summary>
    /// [整包构建] 生成全新的快照列表
    /// </summary>
    public static void ReBuildSnapShots(VersionNumber version)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var data = GetOrCreateSnapshotData();

        // 扫描当前所有资源
        var currentAssets = ScanCurrentProjectAssets(settings);
        
        data.Snapshots.Clear();
        data.StageSnapshot = null;
        data.Snapshots.Add(new BuildSnapshot(version)
        {
            Assets = currentAssets,
            DeleteList = null
        });
        data.HeadIndex = 0;

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"[DiffProcessor] 整包快照已建立。Head Index: {data.HeadIndex}, Assets: {currentAssets.Count}");
    }

    #region 内部辅助方法

    /// <summary>
    /// 获取快照数据
    /// </summary>
    private static BuildSnapshots GetOrCreateSnapshotData()
    {
        var data = AssetDatabase.LoadAssetAtPath<BuildSnapshots>(SnapShotAssetPath);
        if (data == null)
        {
            // 确保目录存在
            string dir = Path.GetDirectoryName(SnapShotAssetPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            data = ScriptableObject.CreateInstance<BuildSnapshots>();
            AssetDatabase.CreateAsset(data, SnapShotAssetPath);
            AssetDatabase.SaveAssets();
        }
        return data;
    }
    
    /// <summary>
    /// 扫描当前项目所有资源
    /// </summary>
    private static List<AssetSnapshot> ScanCurrentProjectAssets(AddressableAssetSettings settings)
    {
        List<AssetSnapshot> list = new List<AssetSnapshot>();
        
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            // 跳过内置数据、HelperData(由BuildManager处理)、以及我们的临时热更组
            if (group.Name == "Built In Data" || 
                group.Name == Constants.HELPER_BUILD_DATA_GROUP_NAME ||
                group.Name == Constants.HOTFIX_GROUP_NAME) 
                continue;

            foreach (var entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
                
                // 计算 Hash: 结合 GUID 和 文件修改时间/内容Hash
                string fullPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (!File.Exists(fullPath)) continue;
                
                string hash = HashGenerator.GenerateFileHash(fullPath); 
                long size = new FileInfo(fullPath).Length;

                list.Add(new AssetSnapshot
                {
                    Address = entry.address,
                    AssetPath = entry.AssetPath,
                    AssetGUID = entry.guid,
                    Labels = new List<string>(entry.labels),
                    GroupName = group.Name, // 记录其所在的本地组
                    FileHash = hash,
                    hasUpdated = false
                });
            }
        }
        return list;
    }

    /// <summary>
    /// 找出修改的资源
    /// </summary>
    private static List<AssetSnapshot> FindModifiedAssets(List<AssetSnapshot> currentAssets, BuildSnapshot head, List<string> deleteList)
    {
        List<AssetSnapshot> modified = new List<AssetSnapshot>();
        
        // 转字典加速查找
        var headDict = new Dictionary<string, AssetSnapshot>();
        foreach (var h in head.Assets)
        {
            if(!headDict.ContainsKey(h.AssetGUID)) headDict.Add(h.AssetGUID, h);
        }
        
        var currentGuids = new HashSet<string>();
        
        // 找出修改或新增的资源
        foreach (var curr in currentAssets)
        {
            currentGuids.Add(curr.AssetGUID);
            
            if (headDict.TryGetValue(curr.AssetGUID, out var oldAsset))
            {
                // 存在 -> 比较 Hash
                if (curr.FileHash != oldAsset.FileHash)
                {
                    Debug.Log($"[DiffProcessor] 资源修改: {curr.AssetPath}");
                    AppendDeletList(deleteList, oldAsset);
                    modified.Add(curr);
                }
            }
            else
            {
                // 不存在 -> 新增
                Debug.Log($"[DiffProcessor] 资源新增: {curr.AssetPath}");
                modified.Add(curr);
            }
        }

        // 删除不存在的资源
        foreach (var oldAsset in head.Assets)
        {
            if (!currentGuids.Contains(oldAsset.AssetGUID))
            {
                Debug.Log($"[DiffProcessor] 删除资源: {oldAsset.AssetPath}");
                AppendDeletList(deleteList, oldAsset);
            }
        }
        return modified;
    }
    
    /// <summary>
    /// 添加删除列表
    /// </summary>
    private static void AppendDeletList(List<string> deleteList, AssetSnapshot oldAssets)
    {
        string deletgroup = oldAssets.hasUpdated ? oldAssets.RemoteGroupName : oldAssets.GroupName;
        string deletlabels = oldAssets.Labels.Count == 0 ? "untyped" : string.Join("", oldAssets.Labels).ToLowerInvariant();
        string bundleIdentifier = $"{deletgroup}_assets_{deletlabels}";
        
        // 防止重复添加
        if (!deleteList.Contains(bundleIdentifier))
        {
            deleteList.Add(bundleIdentifier);
        }
    }
    
    /// <summary>
    /// 获取或创建 Hotfix 组
    /// </summary>
    private static AddressableAssetGroup GetOrCreateHotfixGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(Constants.HOTFIX_GROUP_NAME);
        if (group == null)
        {
            group = settings.CreateGroup(Constants.HOTFIX_GROUP_NAME, false, false, true, null);
            
            // 添加 BundledSchema 并强制设置为 Remote
            var schema = group.AddSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel; // 或 PackTogether
        }
        return group;
    }
    
    #endregion
}
#endif
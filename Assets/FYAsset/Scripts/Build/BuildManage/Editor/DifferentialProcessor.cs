#if UNITY_EDITOR
using System;
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
/// 负责计算差异、切换Remote状态、快照轮转
/// </summary>
public static class DifferentialProcessor
{
    private static string SnapShotAssetPath = FYAssetConstants.SNAPSHOT_ASSET_PATH;
    
    /// <summary>
    /// 分析快照差异，将修改的资源移入 Hotfix 组
    /// 并生成 Staged 快照
    /// </summary>
    public static bool PrepareHotfix(VersionNumber currentVersion)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var data = GetOrCreateSnapshotData();
        var head = data.GetHead();
        var currentAssets = ScanCurrentProjectAssets(settings,head);

        if (head == null)
        {
            Debug.LogError("[DiffProcessor] 没有找到基准版本(Head)，无法执行热更构建。请先执行 Build Full Package。");
            return false;
        }
        
        var modifiedAssets = FindModifiedAssets(currentAssets, head);
        
        if (modifiedAssets.Count == 0)
        {
            Debug.Log("[DiffProcessor] 没有修改的资源，无需调整。");
            return false;
        }
        
        if (modifiedAssets.Count > 0)
        {
            var hotfixGroup = GetOrCreateHotfixGroup(settings);

            foreach (var asset in modifiedAssets)
            {
                var entry = settings.FindAssetEntry(asset.AssetGUID);
                if (entry != null)
                {
                    settings.MoveEntry(entry, hotfixGroup);
                    asset.RemoteGroupName = hotfixGroup.Name;
                    Debug.Log($"[DiffProcessor] 移入热更组: {asset.AssetPath}");
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
        
        Debug.Log($"[DiffProcessor] 差异准备完成。{modifiedAssets.Count} 个资源已移动至 {FYAssetConstants.HOTFIX_GROUP_NAME}。Staged快照已保存。");
        return true;
    }

     /// <summary>
    /// 将远端Group重置回本地Group
    /// 根据 Head 快照中的记录，将 Hotfix Group 中的资源还原回 OriginalGroupName
    /// </summary>
    public static void RestoreOriginalGroups()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var hotfixGroup = settings.FindGroup(FYAssetConstants.HOTFIX_GROUP_NAME);
        
        if (hotfixGroup == null || hotfixGroup.entries.Count == 0)
        {
            Debug.Log("[DiffProcessor] 热更组为空或不存在，无需重置。");
            return;
        }

        var data = GetOrCreateSnapshotData();
        var head = data.GetHead();

        if (head == null)
        {
            Debug.LogWarning("[DiffProcessor] 找不到基准快照 (Head)，无法精确还原分组。请手动检查资源。");
            return;
        }

        // 建立 Head 索引以便快速查找
        Dictionary<string, AssetSnapshot> headIndex = new Dictionary<string, AssetSnapshot>();
        foreach (var asset in head.Assets)
        {
            if (!headIndex.ContainsKey(asset.AssetGUID))
            {
                headIndex.Add(asset.AssetGUID, asset);
            }
        }

        List<AddressableAssetEntry> entriesToMove = new List<AddressableAssetEntry>(hotfixGroup.entries);
        int moveCount = 0;

        foreach (var entry in entriesToMove)
        {
            if (headIndex.TryGetValue(entry.guid, out var originalInfo))
            {
                string targetGroupName = originalInfo.OriginalGroupName;
                
                // 如果原始组名无效或为空，尝试使用 CurrentGroupName (如果它不是 HotfixGroup)
                if (string.IsNullOrEmpty(targetGroupName) || targetGroupName == FYAssetConstants.HOTFIX_GROUP_NAME)
                {
                    // 尝试寻找是否有更早的记录，或者只能放到 Default Group
                    targetGroupName = "Default Local Group"; 
                }

                var targetGroup = settings.FindGroup(targetGroupName);
                if (targetGroup == null)
                {
                    // 如果原分组被删除了，创建一个新的或移入默认组
                    Debug.LogWarning($"[DiffProcessor] 原分组 {targetGroupName} 不存在，移入 Default Local Group: {entry.address}");
                    targetGroup = settings.FindGroup("Default Local Group");
                    if (targetGroup == null) targetGroup = settings.DefaultGroup;
                }

                if (targetGroup != null)
                {
                    settings.MoveEntry(entry, targetGroup);
                    moveCount++;
                }
            }
            else
            {
                // 快照里没找到（可能是热更期间新增的资源），根据策略处理
                // 这里选择移入 Default Group
                Debug.LogWarning($"[DiffProcessor] 快照中未找到资源记录 {entry.address}，移入默认组。");
                settings.MoveEntry(entry, settings.DefaultGroup);
                moveCount++;
            }
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"[DiffProcessor] 分组重置完成。已还原 {moveCount} 个资源。");
    }
    
    /// <summary>
    /// 确认发布上线热更包，更新快照列表和 Head
    /// </summary>
    public static void ConfirmRelease()
    {
        var data = GetOrCreateSnapshotData();
        if (data.StageSnapshot == null)
        {
            EditorUtility.DisplayDialog("提示", "当前没有待发布的暂存快照 (Staged Snapshot)。请先构建热更包。", "OK");
            return;
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
        var currentAssets = ScanCurrentProjectAssets(settings,null);
        
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
    /// <param name="headSnapshot">基准快照，用于查找 OriginalGroupName</param>
    private static List<AssetSnapshot> ScanCurrentProjectAssets(AddressableAssetSettings settings, BuildSnapshot headSnapshot)
    {
        List<AssetSnapshot> list = new List<AssetSnapshot>();
        
        // 建立 Head 索引 (如果存在)
        Dictionary<string, AssetSnapshot> headIndex = null;
        if (headSnapshot != null)
        {
            headIndex = new Dictionary<string, AssetSnapshot>();
            foreach(var a in headSnapshot.Assets)
            {
                if(!headIndex.ContainsKey(a.AssetGUID)) headIndex[a.AssetGUID] = a;
            }
        }
        
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            // 跳过内置数据、HelperData(由BuildManager处理)
            if (group.Name == "Built In Data" || 
                group.Name == FYAssetConstants.HELPER_BUILD_DATA_GROUP_NAME) 
                continue;

            foreach (var entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
                
                // 计算 Hash: 结合 GUID 和 文件修改时间/内容Hash
                string fullPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (!File.Exists(fullPath)) continue;
                
                string hash = "";
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(fullPath);

                // 判断是否为 ScriptableObject
                // 也可以根据需要把 typeof(GameObject) 加进去，让 Prefab 也支持深度依赖检测
                bool isScriptableObject = assetType != null && typeof(ScriptableObject).IsAssignableFrom(assetType)
                    || assetType == typeof(GameObject); 

                if (isScriptableObject)
                {
                    // 递归计算 SO 及其所有引用的 Hash (Lua, Texture, Material 等)
                    hash = HashGenerator.GenerateDeepHash(fullPath);
                }
                else
                {
                    // 普通资源 (Texture, Audio, TextAsset) 直接计算文件 Hash
                    hash = HashGenerator.GenerateFileHash(fullPath); 
                }

                // 确定 OriginalGroupName
                string originalGroup = group.Name;
                
                // 如果当前资源在 Hotfix Group，我们需要查阅历史记录来获取它原本在哪
                if (group.Name == FYAssetConstants.HOTFIX_GROUP_NAME && headIndex != null)
                {
                    if (headIndex.TryGetValue(entry.guid, out var oldAsset))
                    {
                        originalGroup = oldAsset.OriginalGroupName;
                        // 如果历史记录里 original 也是 hotfix (极少情况)，则尝试保持现状或 fallback
                        if (originalGroup == FYAssetConstants.HOTFIX_GROUP_NAME) 
                        {
                            originalGroup = "Default Local Group"; 
                        }
                    }
                }
                
                list.Add(new AssetSnapshot
                {
                    Address = entry.address,
                    AssetPath = entry.AssetPath,
                    AssetGUID = entry.guid,
                    Labels = new List<string>(entry.labels),
                    CurrentGroupName = group.Name, 
                    FileHash = hash,
                    OriginalGroupName = originalGroup
                });
            }
        }
        return list;
    }

    /// <summary>
    /// 找出修改的资源
    /// </summary>
    private static List<AssetSnapshot> FindModifiedAssets(List<AssetSnapshot> currentAssets, BuildSnapshot head)
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

        // 删除不存在的资源仅做日志，不输出删除列表
        foreach (var oldAsset in head.Assets)
        {
            if (!currentGuids.Contains(oldAsset.AssetGUID))
            {
                Debug.Log($"[DiffProcessor] 资源被删除 (仅日志): {oldAsset.AssetPath}");
            }
        }
        return modified;
    }
    
    /// <summary>
    /// 获取资源的 bundle 标识符
    /// </summary>
    private static string GetBundleIdentifier(AssetSnapshot asset)
    {
        string groupName = asset.CurrentGroupName;
        string labels = asset.Labels.Count == 0 ? "untyped" : string.Join("", asset.Labels).ToLowerInvariant();
        return $"{groupName}_assets_{labels}";
    }
    
    /// <summary>
    /// 获取或创建 Hotfix 组
    /// </summary>
    private static AddressableAssetGroup GetOrCreateHotfixGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(FYAssetConstants.HOTFIX_GROUP_NAME);
        if (group == null)
        {
            group = settings.CreateGroup(FYAssetConstants.HOTFIX_GROUP_NAME, false, false, true, null);
            
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
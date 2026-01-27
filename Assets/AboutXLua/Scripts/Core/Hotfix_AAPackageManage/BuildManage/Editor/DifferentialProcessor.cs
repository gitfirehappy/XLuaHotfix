#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
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
    private static string SnapShotAssetPath = Constants.SNAPSHOT_ASSET_PATH;
    
    /// <summary>
    /// 分析快照差异，将修改的资源移入 Hotfix 组
    /// 并生成 Staged 快照
    /// </summary>
    /// <param name="deleteList">要删除的资源列表</param>
    /// <param name="unchangedBundleIdentifiers">热更组中未修改的 bundle 标识符列表（用于跳过复制）</param>
    public static bool PrepareHotfix(VersionNumber currentVersion, List<string> deleteList, out HashSet<string> unchangedBundleIdentifiers)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var data = GetOrCreateSnapshotData();
        var head = data.GetHead();
        var currentAssets = ScanCurrentProjectAssets(settings);

        if (head == null)
        {
            Debug.LogError("[DiffProcessor] 没有找到基准版本(Head)，无法执行热更构建。请先执行 Build Full Package。");
            unchangedBundleIdentifiers = new HashSet<string>();
            return false;
        }
        
        var modifiedAssets = FindModifiedAssets(currentAssets, head, deleteList, out unchangedBundleIdentifiers);
        
        if (modifiedAssets.Count == 0 && deleteList.Count == 0)
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
        
        Debug.Log($"[DiffProcessor] 差异准备完成。{modifiedAssets.Count} 个资源已移动至 {Constants.HOTFIX_GROUP_NAME}。Staged快照已保存。");
        return true;
    }

    // TODO: 将远端Group重置回本地Group（手动调用）
    
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
            // 跳过内置数据、HelperData(由BuildManager处理)
            if (group.Name == "Built In Data" || 
                group.Name == Constants.HELPER_BUILD_DATA_GROUP_NAME) 
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
                    CurrentGroupName = group.Name, 
                    FileHash = hash,
                    // TODO: 处理 OriginalGroupName 逻辑
                });
            }
        }
        return list;
    }

    /// <summary>
    /// 找出修改的资源
    /// </summary>
    /// <param name="unchangedBundleIdentifiers">输出：热更组中未修改的 bundle 标识符（用户已有且此次无改动）</param>
    private static List<AssetSnapshot> FindModifiedAssets(List<AssetSnapshot> currentAssets, BuildSnapshot head, List<string> deleteList, out HashSet<string> unchangedBundleIdentifiers)
    {
        List<AssetSnapshot> modified = new List<AssetSnapshot>();
        unchangedBundleIdentifiers = new HashSet<string>();
        
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
                else
                {
                    // Hash 相同，表示该资源未修改，记录其 bundle 标识符
                    string bundleIdentifier = GetBundleIdentifier(oldAsset);
                    unchangedBundleIdentifiers.Add(bundleIdentifier);
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
        string bundleIdentifier = GetBundleIdentifier(oldAssets);
        
        // 防止重复添加
        if (!deleteList.Contains(bundleIdentifier))
        {
            deleteList.Add(bundleIdentifier);
        }
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
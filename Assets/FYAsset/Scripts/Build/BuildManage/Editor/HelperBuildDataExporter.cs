#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 辅助构建数据导出, 每次热更时变动
/// </summary>
public class HelperBuildDataExporter
{
    private const string _groupName = FYAssetSettings.HELPER_BUILD_DATA_GROUP_NAME;
    private const string _labelsConfigAssetPath = "Assets/Build/HelperBuildData/AddressableLabelsConfig.asset";
    private const string _luaScriptsIndexAssetPath = "Assets/Build/HelperBuildData/LuaScriptsIndex.asset";
    
    /// <summary>
    /// 总导出入口
    /// </summary>
    public static void ExportData()
    {
        Debug.Log("[HelperBuildData] 开始导出所有辅助构建数据...");
        
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[HelperBuildData] AddressableAssetSettings 未找到！");
        }

        var group = GetOrCreateGroup(settings, _groupName);
        
        ExportLuaScriptsIndex();
        EnsureAssetInGroup(settings, group, _luaScriptsIndexAssetPath, FYAssetSettings.LUA_SCRIPTS_INDEX, FYAssetSettings.LUA_SCRIPTS_INDEX);

        GetOrCreateAsset<AddressableLabelsConfig>(_labelsConfigAssetPath);
        EnsureAssetInGroup(settings, group, _labelsConfigAssetPath, FYAssetSettings.AA_LABELS_CONFIG, FYAssetSettings.AA_LABELS_CONFIG);
        
        ExportAddressableLabels();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[HelperBuildData] 导出完成。");
    }

    #region 辅助
    
    /// <summary>
    /// 辅助方法：获取或创建组
    /// </summary>
    private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, true, null);
        }
        return group;
    }
    
    /// <summary>
    /// 辅助方法：确保资源进入指定组，并设置地址和标签
    /// </summary>
    private static void EnsureAssetInGroup(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address,string label = null)
    {
        var guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;

        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;
        
        if (!string.IsNullOrEmpty(label))
        {
            settings.AddLabel(label,false);
            entry.SetLabel(label, true, true);
        }
        EditorUtility.SetDirty(settings);
    }
    
    /// <summary>
    /// 辅助方法：获取或创建指定路径的Asset
    /// </summary>
    private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<T>();
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }
    
    #endregion
    
    #region AddressableLabelsConfig
    
    /// <summary>
    /// 导出AddressableLabelsConfig
    /// </summary>
    private static void ExportAddressableLabels()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        var config = GetOrCreateAsset<AddressableLabelsConfig>(_labelsConfigAssetPath);
        
        config.allEntries.Clear();
        config.keysByType.Clear();
        config.keysByLabel.Clear();
        
        var tempTypeDict = new Dictionary<string, List<string>>();
        var tempLabelDict = new Dictionary<string, List<string>>();
        var groupLabelToAddressList = new Dictionary<(string Group,string CombineLabel), List<string>>();
        
        foreach (var group in settings.groups)
        {
            if (group == null) continue;

            foreach (var entry in group.entries)
            {
                if (entry.IsFolder || string.IsNullOrEmpty(entry.address)) continue;

                string entryType = entry.labels.Count > 0 ? entry.labels.First() : "Untyped";
                string key = entry.address;
                
                config.allEntries.Add(new PackageEntry
                {
                    key = entry.address,
                    Type = entryType,
                    Labels = entry.labels.ToList()
                });
                
                // Type Dict
                if (!tempTypeDict.ContainsKey(entryType)) tempTypeDict[entryType] = new List<string>();
                tempTypeDict[entryType].Add(key);
                
                // Label Dict
                if (entry.labels.Count == 0)
                {
                    AddToLabelDict(tempLabelDict, "Untyped", key);
                }
                else
                {
                    foreach (var label in entry.labels) AddToLabelDict(tempLabelDict, label, key);
                }
                
                // Combined Label (Hash)
                string combinedLabelKey = entry.labels.Count == 0 ? "untyped" : string.Join("", entry.labels).ToLowerInvariant();
                AddToGroupLabelDict(groupLabelToAddressList, group.Name.ToLowerInvariant(), combinedLabelKey, key);
            }
        }
        
        // 序列化字典到 List
        foreach (var kvp in tempTypeDict) config.keysByType.Add(new TypeToKeys { Type = kvp.Key, Keys = kvp.Value });
        foreach (var kvp in tempLabelDict) config.keysByLabel.Add(new LabelToKeys { Label = kvp.Key, Keys = kvp.Value });
        
        foreach (var kvp in groupLabelToAddressList)
        {
            var keys = kvp.Value;
            keys.Sort(StringComparer.Ordinal);
            StringBuilder sb = new StringBuilder();
            foreach (var k in keys) sb.Append(k);
            
            string logicalHash = HashGenerator.GenerateStringHash(sb.ToString());
        }
        
        EditorUtility.SetDirty(config);
        Debug.Log($"[LabelExporter] AddressableLabelsConfig 导出完成。");
    }
    
    #region 辅助方法 
    
    private static void AddToLabelDict(Dictionary<string, List<string>> dict, string label, string key)
    {
        if (!dict.ContainsKey(label)) dict[label] = new List<string>();
        dict[label].Add(key);
    }
    
    private static void AddToGroupLabelDict(Dictionary<(string, string), List<string>> dict, string group, string label, string key)
    {
        var tuple = (group, label);
        if (!dict.ContainsKey(tuple)) dict[tuple] = new List<string>();
        dict[tuple].Add(key);
    }
    
    #endregion
    
    #endregion

    #region LuaScriptsIndex

    /// <summary>
    /// 导出 Lua 脚本索引
    /// </summary>
    private static void ExportLuaScriptsIndex()
    { 
        var indexSO = GetOrCreateAsset<LuaScriptsIndex>(_luaScriptsIndexAssetPath);
        indexSO.data.Clear();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        
        string[] guids = AssetDatabase.FindAssets("t:LuaScriptContainer");
        
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var container = AssetDatabase.LoadAssetAtPath<LuaScriptContainer>(path);
            if (container == null) continue;

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                // 仅警告，不中断，可能该容器不需要进包
                Debug.LogWarning($"[LuaIndexExporter] Container不在Addressables中: {container.name}");
                continue; 
            }

            var entryData = new LuaScriptsIndex.ContainerEntry
            {
                containerAddress = entry.address,
                scriptNames = new List<string>()
            };

            foreach (var asset in container.luaAssets)
            {
                if (asset == null) continue;
                string scriptKey = XLuaLoader.NormalizeModuleKey(asset.name);
                entryData.scriptNames.Add(scriptKey);
            }

            indexSO.data.Add(entryData);
        }

        EditorUtility.SetDirty(indexSO);
        Debug.Log($"[LuaIndexExporter] LuaScriptsIndex 导出完成。包含 {indexSO.data.Count} 个容器。");
    }

    #endregion
    
}
#endif
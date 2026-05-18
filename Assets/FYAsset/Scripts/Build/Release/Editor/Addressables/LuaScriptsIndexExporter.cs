#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Lua 脚本索引导出。AA 资源索引已由 AAManifest 承载。
/// </summary>
public class LuaScriptsIndexExporter
{
    private const string _luaScriptsIndexAssetPath = "Assets/Build/LuaScriptsIndex.asset";
    
    /// <summary>
    /// 总导出入口
    /// </summary>
    public static void ExportData()
    {
        Debug.Log("[LuaScriptsIndexExporter] 开始导出 LuaScriptsIndex...");
        
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[LuaScriptsIndexExporter] AddressableAssetSettings 未找到！");
            return;
        }

        var group = GetOrCreateGroup(settings, "LuaScripts");
        
        ExportLuaScriptsIndex();
        EnsureAssetInGroup(settings, group, _luaScriptsIndexAssetPath, FYAssetSettings.LUA_SCRIPTS_INDEX, FYAssetSettings.LUA_SCRIPTS_INDEX);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[LuaScriptsIndexExporter] 导出完成。");
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

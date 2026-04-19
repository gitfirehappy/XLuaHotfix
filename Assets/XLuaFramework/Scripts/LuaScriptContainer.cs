using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif

[CreateAssetMenu(fileName = "LuaContainer", menuName = "XLua/Lua Script Container", order = 2)]
public class LuaScriptContainer : ScriptableObject
{
    [Tooltip("LuaScriptContainer 所在的组名")] public string groupName = "LuaScripts";

    [Tooltip("多个标签可用于细分加载逻辑")] public List<string> addressableLabels = new() { "LuaScriptContainer" };

    [Tooltip("LuaScriptContainer 所包含的Lua文件")]
    public List<TextAsset> luaAssets = new();

    [ContextMenu("清空列表")]
    public void ClearList()
    {
        luaAssets.Clear();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

#if UNITY_EDITOR
    
    /// <summary>
    /// 为LuaScriptContainer 自身添加Addressable标签
    /// </summary>
    public void ApplyAddressableLabels(AddressableAssetSettings settings)
    {
        // 获取或创建对应的Addressable组
        AddressableAssetGroup targetGroup = GetOrCreateAddressableGroup(groupName, settings);
        if (targetGroup == null)
        {
            Debug.LogError($"无法创建或找到组: {groupName}");
            return;
        }

        // 为LuaScriptContainer打标签
        string assetPath = AssetDatabase.GetAssetPath(this);
        AddressableAssetEntry entry = settings.FindAssetEntry(assetPath);
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), targetGroup);
        }
        else
        {
            settings.MoveEntry(entry, targetGroup);
        }

        if (entry != null)
        {
            // 清除现有标签
            List<string> currentLabels = entry.labels.ToList();
            foreach (string label in currentLabels)
            {
                entry.SetLabel(label, false);
            }

            // 应用新标签
            foreach (string label in addressableLabels)
            {
                if (!string.IsNullOrEmpty(label))
                {
                    if (!settings.GetLabels().Contains(label))
                    {
                        settings.AddLabel(label);
                    }

                    entry.SetLabel(label, true);
                }
            }
        }
    }

    /// <summary>
    /// 获取或创建Addressable组
    /// </summary>
    private AddressableAssetGroup GetOrCreateAddressableGroup(string groupName, AddressableAssetSettings settings)
    {
        if (string.IsNullOrEmpty(groupName)) groupName = "LuaScripts";

        // 查找现有组
        AddressableAssetGroup group = settings.groups.Find(g => g.Name == groupName);

        if (group == null)
        {
            // 创建新组
            group = settings.CreateGroup(groupName, false, false, true, null);

            // 设置新组的模式为Packed（可以根据需要调整，构建管理会统一处理）
            var schema = group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
            if (schema != null)
            {
                schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            }
        }

        return group;
    }
#endif
}
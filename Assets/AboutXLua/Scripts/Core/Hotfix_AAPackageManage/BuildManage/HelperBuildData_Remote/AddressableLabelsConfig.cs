using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 构建期导出的 AA 条目配置
/// </summary>
public class AddressableLabelsConfig : ScriptableObject
{
    public List<PackageEntry> allEntries = new();   // 作用：查询
    
    // Type -> Keys 索引
    public List<TypeToKeys> keysByType = new();
    
    // Label -> Keys 索引
    public List<LabelToKeys> keysByLabel = new();
    
    // Group + Label -> LogicalHash
    // TODO: 是否需要删除，version_state不依靠这个比对删除
    public List<GroupLabelToLogicalHash> labelLogicalHashes = new();    // 作用：生成version_state进行差异比对

    // 运行时快速查找字典 
    ///<summary> Key: "Type" -> Value: Keys </summary>
    private Dictionary<string, List<string>> _typeDict;
    
    ///<summary> Key: "Label" -> Value: Keys </summary>
    private Dictionary<string, List<string>> _labelDict;
    
    ///<summary> Key: "Group_Label" -> Value: Hash </summary> 
    private Dictionary<string, string> _labelLogicalHashDict;

    /// <summary>
    /// 获取某类型所有 Key
    /// </summary>
    public List<string> GetKeysByType(string type)
    {
        if (_typeDict == null) BuildRuntimeDicts();
        return _typeDict.TryGetValue(type, out var list) ? list : new List<string>();
    }

    /// <summary>
    /// 获取某标签所有 Key
    /// </summary>
    public List<string> GetKeysByLabel(string label)
    {
        if (_labelDict == null) BuildRuntimeDicts();
        return _labelDict.TryGetValue(label, out var list) ? list : new List<string>();
    }

    /// <summary>
    /// 获取所有标签
    /// </summary>
    public List<string> GetLabels()
    {
        if (_labelDict == null) BuildRuntimeDicts();
        return _labelDict?.Keys.ToList() ?? new List<string>();
    }
    
    /// <summary>
    /// 获取逻辑Hash,生成version_state 时调用
    /// </summary>
    /// <param name="groupName">解析文件名得到的Group部分 (通常是小写) </param>
    /// <param name="labels">解析文件名得到的Label部分 (通常是小写拼接)</param>
    public string GetLogicalHash(string groupName,string labels)
    {
        if (_labelLogicalHashDict == null) BuildRuntimeDicts();
        // 组合键策略需与构建时一致
        string key = $"{groupName.ToLowerInvariant()}_{labels.ToLowerInvariant()}"; 
        
        return _labelLogicalHashDict.TryGetValue(key, out var hash) ? hash : string.Empty;
    }

    /// <summary>
    /// 构建运行时快速查找字典
    /// </summary>
    private void BuildRuntimeDicts()
    {
        _typeDict = new Dictionary<string, List<string>>();
        _labelDict = new Dictionary<string, List<string>>();
        _labelLogicalHashDict = new Dictionary<string, string>();

        foreach (var item in keysByType) _typeDict[item.Type] = item.Keys;
        foreach (var item in keysByLabel) _labelDict[item.Label] = item.Keys;
        foreach (var item in labelLogicalHashes)
        {
            string key = $"{item.Group.ToLowerInvariant()}_{item.CombineLabel.ToLowerInvariant()}";
            
            // 防止重复Key报错
            if (!_labelLogicalHashDict.ContainsKey(key))
            {
                _labelLogicalHashDict.Add(key, item.Hash);
            }
        }
    }
}

[Serializable]
public class TypeToKeys
{
    public string Type;
    public List<string> Keys = new();
}

[Serializable]
public class LabelToKeys
{
    public string Label;
    public List<string> Keys = new();
}

[Serializable]
public class GroupLabelToLogicalHash
{
    public string Group;
    public string CombineLabel;    // 这个Labels是 一组 Label拼接的索引
    public string Hash;
}

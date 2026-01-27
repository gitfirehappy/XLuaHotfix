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

    // 运行时快速查找字典 
    ///<summary> Key: "Type" -> Value: Keys </summary>
    private Dictionary<string, List<string>> _typeDict;
    
    ///<summary> Key: "Label" -> Value: Keys </summary>
    private Dictionary<string, List<string>> _labelDict;

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
    /// 构建运行时快速查找字典
    /// </summary>
    private void BuildRuntimeDicts()
    {
        _typeDict = new Dictionary<string, List<string>>();
        _labelDict = new Dictionary<string, List<string>>();

        foreach (var item in keysByType) _typeDict[item.Type] = item.Keys;
        foreach (var item in keysByLabel) _labelDict[item.Label] = item.Keys;
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

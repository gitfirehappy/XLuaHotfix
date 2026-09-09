using System;
using System.Collections.Generic;

/// <summary>
/// AB 资源的运行时身份与查询元数据。
/// </summary>
/// <remarks>
/// EntryId 用作缓存与句柄身份，Address 可以重复；SourcePath 还用于 backend 从 Bundle 或 AssetDatabase 提取资源。
/// SetLabels 保留输入顺序、重复项和大小写，匹配使用大小写不敏感的集合。
/// </remarks>
[Serializable]
public class RuntimeAssetEntry
{

    /// <summary>
    /// Unity GUID，用作缓存和句柄身份。
    /// </summary>
    public string EntryId;

    /// <summary>
    /// 逻辑查询键；允许重复。
    /// </summary>
    public string Address;

    /// <summary>
    /// 查询匹配用的类型名；本字段不验证可赋值性。
    /// </summary>
    public string PrimaryType;

    /// <summary>
    /// Selects UnityEngine.Object or RawFile loading.
    /// </summary>
    public EPayloadKind PayloadKind = EPayloadKind.Serialized;

    /// <summary>
    /// SetLabels 原样复制的 Labels，不去重。
    /// </summary>
    private List<string> _labels = new();

    public IReadOnlyList<string> Labels => _labels;

    /// <summary>
    /// 来自 Manifest 的工程路径；不是公开查询键。
    /// </summary>
    public string SourcePath;

    /// <summary>
    /// 构建 Group 元数据；不是 Resolve/Load 过滤条件。
    /// </summary>
    public string Group;

    /// <summary>
    /// 是否由构建配置生成 Address。
    /// </summary>
    public bool AutoAddress = true;

    /// <summary>
    /// 大小写不敏感匹配缓存，SetLabels 时失效。
    /// </summary>
    private HashSet<string> _normalizedLabelsCache;

    /// <summary>
    /// 返回内部大小写不敏感集合；调用方不得修改。
    /// </summary>
    public HashSet<string> GetNormalizedLabels()
    {
        if (_normalizedLabelsCache != null) return _normalizedLabelsCache;

        _normalizedLabelsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Labels.Count; i++)
        {
            _normalizedLabelsCache.Add(Labels[i]);
        }

        return _normalizedLabelsCache;
    }

    /// <summary>
    /// 复制 Labels 并使匹配缓存失效。
    /// </summary>
    public void SetLabels(IEnumerable<string> labels)
    {
        _labels.Clear();
        if (labels != null)
            _labels.AddRange(labels);
        InvalidateLabelCache();
    }

    private void InvalidateLabelCache()
    {
        _normalizedLabelsCache = null;
    }

    /// <summary>
    /// 按大小写不敏感匹配非空 Label。
    /// </summary>
    public bool HasLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        for (int i = 0; i < Labels.Count; i++)
        {
            if (string.Equals(Labels[i], label, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 按大小写不敏感匹配全部 Labels；空过滤匹配所有条目。
    /// </summary>
    public bool HasAllLabels(IReadOnlyList<string> labels)
    {
        if (labels == null || labels.Count == 0) return true;
        var normalized = GetNormalizedLabels();
        for (int i = 0; i < labels.Count; i++)
        {
            if (!normalized.Contains(labels[i]))
                return false;
        }

        return true;
    }

    public override string ToString()
    {
        return string.Concat(
            "[", EntryId ?? "", "] ",
            Address ?? "", " (", PrimaryType ?? "", ") Labels=[",
            string.Join(",", Labels), "] Payload=",
            PayloadKind.ToString()
        );
    }
}

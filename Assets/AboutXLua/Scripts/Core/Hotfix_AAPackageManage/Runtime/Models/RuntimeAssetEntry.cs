using System;
using System.Collections.Generic;

/// <summary>
/// 运行时资源条目 — B5-1 定义的最小字段集。
/// 
/// 设计思路：
/// - EntryId（Unity GUID）承担内部唯一身份，用于缓存键、诊断、句柄归属
/// - Address 允许重复，不再承担全局唯一身份
/// - PrimaryType 默认自动推导，允许兼容性手改
/// - Labels 为无序唯一集合，匹配时大小写不敏感
/// - Group/SourcePath/AutoAddress 仅用于编辑器诊断，运行时可选
/// </summary>
[Serializable]
public class RuntimeAssetEntry
{
    #region 运行时必须字段

    /// <summary>
    /// 内部唯一身份（复用 Unity GUID）。
    /// 用途：缓存键、诊断标识、句柄归属。
    /// </summary>
    public string EntryId;

    /// <summary>
    /// 逻辑名（允许重复）。
    /// 默认由文件名去扩展自动生成；冲突时升级为 Filename_Type 格式。
    /// </summary>
    public string Address;

    /// <summary>
    /// V1 唯一公开 Type 字段。
    /// 默认自动推导资源类型；ScriptableObject 使用具体类名。
    /// 允许手动修改，但必须兼容实际类型。
    /// </summary>
    public string PrimaryType;

    /// <summary>
    /// 无序唯一标签集合。
    /// 存储时保留原始输入大小写；匹配时使用归一化（小写）比较。
    /// </summary>
    public List<string> Labels = new();

    #endregion

    #region 编辑器诊断字段（运行时可选）

    /// <summary>
    /// 资源在项目中的路径，仅用于编辑器定位与冲突报告。
    /// 不作为运行时查询入口。
    /// </summary>
    public string SourcePath;

    /// <summary>
    /// 构建分组名称，仅参与编辑器报表与构建语义。
    /// 不进入运行时 Resolve / Load 查询参数。
    /// </summary>
    public string Group;

    /// <summary>
    /// 标记 Address 是自动生成还是手动覆写。
    /// true = 自动生成（可重建）；false = 手动覆写（锁定，除非显式切回 Auto）。
    /// </summary>
    public bool AutoAddress = true;

    #endregion

    #region 归一化查询辅助（运行时性能优化：避免 LINQ，使用 for 循环）

    /// <summary>
    /// 缓存归一化后的 Labels 集合（全部小写），避免每次查询重复创建。
    /// 当 Labels 列表变化时需调用 InvalidateLabelCache() 重建。
    /// </summary>
    private HashSet<string> _normalizedLabelsCache;

    /// <summary>
    /// 获取归一化后的 Labels（全部小写），用于匹配比较。
    /// 结果被缓存，重复调用不分配内存。
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
    /// 当 Labels 列表发生变化时，调用此方法使缓存失效。
    /// </summary>
    public void InvalidateLabelCache()
    {
        _normalizedLabelsCache = null;
    }

    /// <summary>
    /// 检查是否包含指定标签（大小写不敏感）。
    /// 运行时高频调用，使用 for 循环避免 LINQ 开销。
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
    /// 检查是否包含所有指定标签（大小写不敏感）。
    /// 使用缓存的归一化集合进行高效查找。
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

    #endregion

    public override string ToString()
    {
        return string.Concat(
            "[", EntryId ?? "", "] ",
            Address ?? "", " (", PrimaryType ?? "", ") Labels=[",
            string.Join(",", Labels), "]"
        );
    }
}

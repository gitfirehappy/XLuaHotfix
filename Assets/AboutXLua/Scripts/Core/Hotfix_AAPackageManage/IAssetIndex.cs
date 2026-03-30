using System;
using System.Collections.Generic;

/// <summary>
/// 资源索引接口。
/// 
/// V1 保留原有字符串查询方法（向后兼容 AddressableLabelsConfig）；
/// B5-1 新增条目级查询方法，供 B5-2 Resolve/Load 使用。
/// 
/// 旧实现（AddressableLabelsConfig）通过默认方法抛 NotSupportedException，
/// 新实现（ABAssetIndex）将在 Phase 3 (B6) 中基于 RuntimeAssetEntry 完整实现。
/// </summary>
public interface IAssetIndex
{
    #region 原有方法（向后兼容）

    List<string> GetKeysByLabel(string label);
    List<string> GetKeysByType(string type);
    List<string> GetLabels();
    bool ContainsKey(string key);

    #endregion

    #region B5-1 新增：条目级查询

    /// <summary>
    /// 通过 EntryId 获取条目（精确匹配）。
    /// 返回 null 表示未找到。
    /// </summary>
    RuntimeAssetEntry GetEntryById(string entryId)
    {
        throw new NotSupportedException(
            $"{GetType().Name} 不支持条目级查询。请使用支持 RuntimeAssetEntry 的索引实现。");
    }

    /// <summary>
    /// 通过 Address 获取所有匹配条目（Address 允许重复）。
    /// </summary>
    IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddress(string address)
    {
        throw new NotSupportedException(
            $"{GetType().Name} 不支持条目级查询。请使用支持 RuntimeAssetEntry 的索引实现。");
    }

    /// <summary>
    /// 通过 Address + PrimaryType 获取匹配条目。
    /// 用于 ByAddress 查询的类型过滤。
    /// </summary>
    IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddressAndType(string address, string primaryType)
    {
        throw new NotSupportedException(
            $"{GetType().Name} 不支持条目级查询。请使用支持 RuntimeAssetEntry 的索引实现。");
    }

    /// <summary>
    /// 获取所有条目（用于校验、诊断）。
    /// </summary>
    IReadOnlyList<RuntimeAssetEntry> GetAllEntries()
    {
        throw new NotSupportedException(
            $"{GetType().Name} 不支持条目级查询。请使用支持 RuntimeAssetEntry 的索引实现。");
    }

    #endregion
}
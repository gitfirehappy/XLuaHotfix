using System;
using System.Collections.Generic;

/// <summary>
/// AB 资源索引 — 基于 ABManifest 的完整 IAssetIndex 实现。
///
/// 设计要点：
/// - 持有 ABManifest 引用，但不访问其 private 索引字典
/// - Initialize 时遍历 ABManifest.AssetEntries，调用 ToRuntimeEntry() 预转换为缓存数组
/// - 自建 4 个索引字典 + 2 个预建结果数组，查询真正零分配热路径
/// - 全部使用 for 循环，不使用 LINQ
///
/// 内存预估：RuntimeAssetEntry ~600 bytes/entry，1000 entries ≈ 600KB，可接受。
/// </summary>
public class ABAssetIndex : IAssetIndex
{
    #region 内部状态

    /// <summary>持有的清单引用（用于 Bundle 查询等后续扩展）</summary>
    private readonly ABManifest _manifest;

    /// <summary>预转换的全部条目缓存</summary>
    private RuntimeAssetEntry[] _entries;

    /// <summary>Address -> 条目索引列表（Address 允许重复）</summary>
    private Dictionary<string, List<int>> _addressIndex;

    /// <summary>EntryId -> 条目索引（唯一）</summary>
    private Dictionary<string, int> _entryIdIndex;

    /// <summary>PrimaryType -> 条目索引列表</summary>
    private Dictionary<string, List<int>> _typeIndex;

    /// <summary>Label -> 条目索引列表（大小写不敏感）</summary>
    private Dictionary<string, List<int>> _labelIndex;

    /// <summary>Address -> 预建结果数组（零分配热路径）</summary>
    private Dictionary<string, RuntimeAssetEntry[]> _addressResults;

    /// <summary>PrimaryType -> 预建结果数组（零分配热路径）</summary>
    private Dictionary<string, RuntimeAssetEntry[]> _typeResults;

    /// <summary>(Address, PrimaryType) -> 预建结果数组（零分配热路径）</summary>
    private Dictionary<(string, string), RuntimeAssetEntry[]> _addressTypeResults;

    #endregion

    #region 构造 & 初始化

    /// <summary>
    /// 构造 ABAssetIndex 并立即初始化索引。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest 实例（DeserializeFromJson 后自动调用 Initialize）</param>
    public ABAssetIndex(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        BuildIndex();
    }

    /// <summary>
    /// 遍历 ABManifest.AssetEntries，预转换为 RuntimeAssetEntry 并构建所有索引字典。
    /// </summary>
    private void BuildIndex()
    {
        var assetEntries = _manifest.AssetEntries;
        int count = assetEntries != null ? assetEntries.Count : 0;

        // 1. 预转换全部条目
        _entries = new RuntimeAssetEntry[count];
        for (int i = 0; i < count; i++)
        {
            _entries[i] = assetEntries[i].ToRuntimeEntry();
        }

        // 2. EntryId -> 索引（唯一）
        _entryIdIndex = new Dictionary<string, int>(count);
        for (int i = 0; i < count; i++)
        {
            string id = _entries[i].EntryId;
            if (!string.IsNullOrEmpty(id))
                _entryIdIndex[id] = i;
        }

        // 3. Address -> 索引列表（允许重复）
        _addressIndex = new Dictionary<string, List<int>>(count);
        for (int i = 0; i < count; i++)
        {
            string addr = _entries[i].Address;
            if (string.IsNullOrEmpty(addr)) continue;

            if (!_addressIndex.TryGetValue(addr, out var list))
            {
                list = new List<int>(1);
                _addressIndex[addr] = list;
            }
            list.Add(i);
        }

        // 4. PrimaryType -> 索引列表
        _typeIndex = new Dictionary<string, List<int>>();
        for (int i = 0; i < count; i++)
        {
            string type = _entries[i].PrimaryType;
            if (string.IsNullOrEmpty(type)) continue;

            if (!_typeIndex.TryGetValue(type, out var list))
            {
                list = new List<int>();
                _typeIndex[type] = list;
            }
            list.Add(i);
        }

        // 5. Label -> 索引列表（大小写不敏感，与 RuntimeAssetEntry.HasLabel 策略一致）
        _labelIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            var labels = _entries[i].Labels;
            if (labels == null) continue;
            for (int j = 0; j < labels.Count; j++)
            {
                string label = labels[j];
                if (string.IsNullOrEmpty(label)) continue;

                if (!_labelIndex.TryGetValue(label, out var list))
                {
                    list = new List<int>();
                    _labelIndex[label] = list;
                }
                list.Add(i);
            }
        }

        // 6. 预建 Address -> 结果数组（零分配热路径）
        _addressResults = new Dictionary<string, RuntimeAssetEntry[]>(_addressIndex.Count);
        foreach (var kv in _addressIndex)
        {
            var indices = kv.Value;
            var arr = new RuntimeAssetEntry[indices.Count];
            for (int i = 0; i < indices.Count; i++)
                arr[i] = _entries[indices[i]];
            _addressResults[kv.Key] = arr;
        }

        // 7. 预建 PrimaryType -> 结果数组（零分配热路径）
        _typeResults = new Dictionary<string, RuntimeAssetEntry[]>(_typeIndex.Count);
        foreach (var kv in _typeIndex)
        {
            var indices = kv.Value;
            var arr = new RuntimeAssetEntry[indices.Count];
            for (int i = 0; i < indices.Count; i++)
                arr[i] = _entries[indices[i]];
            _typeResults[kv.Key] = arr;
        }

        // 8. 预建 (Address, PrimaryType) -> 结果数组（零分配热路径）
        _addressTypeResults = new Dictionary<(string, string), RuntimeAssetEntry[]>();
        foreach (var kv in _addressIndex)
        {
            string address = kv.Key;
            var indices = kv.Value;
            var typeGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < indices.Count; i++)
            {
                int idx = indices[i];
                string type = _entries[idx].PrimaryType ?? "";
                if (!typeGroups.TryGetValue(type, out var typeList))
                {
                    typeList = new List<int>();
                    typeGroups[type] = typeList;
                }
                typeList.Add(idx);
            }
            foreach (var tg in typeGroups)
            {
                var arr = new RuntimeAssetEntry[tg.Value.Count];
                for (int i = 0; i < tg.Value.Count; i++)
                    arr[i] = _entries[tg.Value[i]];
                _addressTypeResults[(address, tg.Key)] = arr;
            }
        }
    }

    #endregion

    #region IAssetIndex

    /// <summary>
    /// 通过 EntryId 获取条目（精确匹配）。返回 null 表示未找到。
    /// 零分配热路径。
    /// </summary>
    public RuntimeAssetEntry GetEntryById(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return null;
        if (_entryIdIndex.TryGetValue(entryId, out int index))
            return _entries[index];
        return null;
    }

    /// <summary>
    /// 通过 Address 获取所有匹配条目（Address 允许重复）。
    /// 零分配热路径 — 返回预建缓存数组。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || !_addressResults.TryGetValue(address, out var result))
            return Array.Empty<RuntimeAssetEntry>();
        return result;
    }

    /// <summary>
    /// 通过 Address + PrimaryType 获取匹配条目。
    /// 零分配热路径 — 返回预建缓存数组。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddressAndType(string address, string primaryType)
    {
        if (string.IsNullOrEmpty(address))
            return Array.Empty<RuntimeAssetEntry>();
        if (_addressTypeResults.TryGetValue((address, primaryType ?? ""), out var result))
            return result;
        return Array.Empty<RuntimeAssetEntry>();
    }

    /// <summary>
    /// 获取所有条目。返回内部缓存数组的只读视图。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetAllEntries()
    {
        return _entries;
    }

    #endregion
}

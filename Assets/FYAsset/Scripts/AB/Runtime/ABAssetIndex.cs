using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// AB 资源索引 — 基于 ABManifest 的公共资源查询与 EntryId 定位。
/// </summary>
/// <remarks>
/// 索引构建时只把 IsPublic=true 的条目放进 Address / Type / Label 查询，
/// 隐式依赖条目只保留 EntryId 索引，供加载与依赖使用。
/// 公共 Address 大小写不敏感且唯一；运行期发现重复即记录结构化错误并拒绝本次索引。
/// 查询返回的数组为内部缓存，调用方不得修改。
/// </remarks>
public class ABAssetIndex
{

    /// <summary>持有的清单引用</summary>
    private readonly ABManifest _manifest;

    /// <summary>预转换的全部条目缓存（含隐式依赖条目）</summary>
    private RuntimeAssetEntry[] _entries;

    /// <summary>EntryId -> 条目（唯一，含隐式依赖条目）</summary>
    private Dictionary<string, RuntimeAssetEntry> _entryIdIndex;

    /// <summary>公共 Address -> 条目（大小写不敏感、唯一）</summary>
    private Dictionary<string, RuntimeAssetEntry> _addressIndex;

    /// <summary>PrimaryType -> 公共条目数组</summary>
    private Dictionary<string, RuntimeAssetEntry[]> _typeResults;

    /// <summary>PrimaryType -> 公共 Address 数组</summary>
    private Dictionary<string, string[]> _typeAddressResults;

    /// <summary>Label -> 公共条目数组（大小写不敏感）</summary>
    private Dictionary<string, RuntimeAssetEntry[]> _labelResults;

    /// <summary>Label -> 公共 Address 数组（大小写不敏感）</summary>
    private Dictionary<string, string[]> _labelAddressResults;

    /// <summary>
    /// 构造 ABAssetIndex 并立即构建索引。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest 实例</param>
    public ABAssetIndex(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        BuildIndex();
    }

    /// <summary>
    /// 索引构建失败的结构化原因；成功时为 null。
    /// </summary>
    /// <remarks>
    /// 唯一的失败条件是公共 Address 重复（大小写不敏感）。构建期已由构建结果校验阻断，
    /// 运行期仍然显式失败，避免用不确定的候选集继续加载。
    /// </remarks>
    public RuntimeMessage BuildError { get; private set; }

    /// <summary>索引是否可用。BuildError 非 null 时不可用。</summary>
    public bool IsValid => BuildError == null;

    /// <summary>
    /// 遍历 ABManifest.AssetEntries，预转换为 RuntimeAssetEntry 并构建查询索引。
    /// </summary>
    private void BuildIndex()
    {
        var assetEntries = _manifest.AssetEntries;
        int count = assetEntries != null ? assetEntries.Count : 0;

        _entries = new RuntimeAssetEntry[count];
        for (int i = 0; i < count; i++)
        {
            _entries[i] = assetEntries[i].ToRuntimeEntry();
        }

        _entryIdIndex = new Dictionary<string, RuntimeAssetEntry>(count);
        for (int i = 0; i < count; i++)
        {
            RuntimeAssetEntry entry = _entries[i];
            if (!string.IsNullOrEmpty(entry.EntryId))
                _entryIdIndex[entry.EntryId] = entry;
        }

        // 公共 Address 唯一性：重复即整体失败，不再保留任何可用的 address 索引
        var duplicatedAddresses = new List<string>();
        _addressIndex = new Dictionary<string, RuntimeAssetEntry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            RuntimeAssetEntry entry = _entries[i];
            if (!IsPublicAddressCandidate(entry)) continue;

            if (_addressIndex.TryGetValue(entry.Address, out RuntimeAssetEntry existing))
            {
                duplicatedAddresses.Add(string.Concat(
                    entry.Address,
                    " (EntryId=", existing.EntryId, " / ", entry.EntryId, ")"));
                continue;
            }

            _addressIndex[entry.Address] = entry;
        }

        if (duplicatedAddresses.Count > 0)
        {
            BuildError = RuntimeMessage.DuplicateAddress(
                string.Join(", ", duplicatedAddresses),
                duplicatedAddresses.Count);
            _addressIndex.Clear();
            _typeResults = new Dictionary<string, RuntimeAssetEntry[]>(0);
            _typeAddressResults = new Dictionary<string, string[]>(0);
            _labelResults = new Dictionary<string, RuntimeAssetEntry[]>(0);
            _labelAddressResults = new Dictionary<string, string[]>(0);
            return;
        }

        var typeGroups = new Dictionary<string, List<RuntimeAssetEntry>>();
        var labelGroups = new Dictionary<string, List<RuntimeAssetEntry>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            RuntimeAssetEntry entry = _entries[i];
            if (!IsPublicAddressCandidate(entry)) continue;

            if (!string.IsNullOrEmpty(entry.PrimaryType))
                AddGroup(typeGroups, entry.PrimaryType, entry);

            IReadOnlyList<string> labels = entry.Labels;
            for (int j = 0; j < labels.Count; j++)
            {
                if (string.IsNullOrEmpty(labels[j])) continue;
                AddGroup(labelGroups, labels[j], entry);
            }
        }

        _typeResults = new Dictionary<string, RuntimeAssetEntry[]>(typeGroups.Count, StringComparer.OrdinalIgnoreCase);
        _typeAddressResults = new Dictionary<string, string[]>(typeGroups.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var group in typeGroups)
        {
            _typeResults[group.Key] = group.Value.ToArray();
            _typeAddressResults[group.Key] = CollectAddresses(group.Value);
        }

        _labelResults = new Dictionary<string, RuntimeAssetEntry[]>(labelGroups.Count, StringComparer.OrdinalIgnoreCase);
        _labelAddressResults = new Dictionary<string, string[]>(labelGroups.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var group in labelGroups)
        {
            _labelResults[group.Key] = group.Value.ToArray();
            _labelAddressResults[group.Key] = CollectAddresses(group.Value);
        }
    }

    /// <summary>
    /// 公共条目必须同时满足 IsPublic 与非空 Address 才进入查询索引。
    /// </summary>
    private static bool IsPublicAddressCandidate(RuntimeAssetEntry entry)
    {
        return entry != null && entry.IsPublic && !string.IsNullOrEmpty(entry.Address);
    }

    /// <summary>
    /// 公共 Address 是否在索引内（大小写不敏感）。
    /// </summary>
    public bool ContainsAddress(string address)
    {
        if (BuildError != null || string.IsNullOrEmpty(address)) return false;
        return _addressIndex.ContainsKey(address);
    }

    /// <summary>
    /// 通过 EntryId 获取条目（含隐式依赖条目）。返回 null 表示未找到。
    /// </summary>
    public RuntimeAssetEntry GetEntryById(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return null;
        return _entryIdIndex.TryGetValue(entryId, out RuntimeAssetEntry entry) ? entry : null;
    }

    /// <summary>
    /// 通过公共 Address 获取唯一条目（大小写不敏感）。返回 null 表示未找到。
    /// </summary>
    public RuntimeAssetEntry GetEntryByAddress(string address)
    {
        if (BuildError != null || string.IsNullOrEmpty(address)) return null;
        return _addressIndex.TryGetValue(address, out RuntimeAssetEntry entry) ? entry : null;
    }

    /// <summary>
    /// 获取指定主类型的公共条目（大小写不敏感）。返回内部缓存数组，调用方不得修改。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetEntriesByType(string primaryType)
    {
        if (BuildError != null || string.IsNullOrEmpty(primaryType))
            return Array.Empty<RuntimeAssetEntry>();
        return _typeResults.TryGetValue(primaryType, out RuntimeAssetEntry[] entries)
            ? entries
            : Array.Empty<RuntimeAssetEntry>();
    }

    /// <summary>
    /// 获取指定 Label 的公共条目（大小写不敏感）。返回内部缓存数组，调用方不得修改。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetEntriesByLabel(string label)
    {
        if (BuildError != null || string.IsNullOrEmpty(label))
            return Array.Empty<RuntimeAssetEntry>();
        return _labelResults.TryGetValue(label, out RuntimeAssetEntry[] entries)
            ? entries
            : Array.Empty<RuntimeAssetEntry>();
    }

    /// <summary>
    /// 获取指定主类型的公共 Address（大小写不敏感）。返回内部缓存数组，调用方不得修改。
    /// </summary>
    public IReadOnlyList<string> GetAddressesByType(string primaryType)
    {
        if (BuildError != null || string.IsNullOrEmpty(primaryType))
            return Array.Empty<string>();
        return _typeAddressResults.TryGetValue(primaryType, out string[] addresses)
            ? addresses
            : Array.Empty<string>();
    }

    /// <summary>
    /// 获取指定 Label 的公共 Address（大小写不敏感）。返回内部缓存数组，调用方不得修改。
    /// </summary>
    public IReadOnlyList<string> GetAddressesByLabel(string label)
    {
        if (BuildError != null || string.IsNullOrEmpty(label))
            return Array.Empty<string>();
        return _labelAddressResults.TryGetValue(label, out string[] addresses)
            ? addresses
            : Array.Empty<string>();
    }

    /// <summary>
    /// 获取同时命中主类型与 Label 的公共地址；结果按查询即时计算，未预建组合索引。
    /// </summary>
    public IReadOnlyList<string> GetAddressesByTypeAndLabel(string primaryType, string label)
    {
        IReadOnlyList<RuntimeAssetEntry> entries = GetEntriesByType(primaryType);
        if (entries.Count == 0 || string.IsNullOrEmpty(label))
            return Array.Empty<string>();

        var addresses = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasLabel(label))
                addresses.Add(entries[i].Address);
        }

        return addresses;
    }

    /// <summary>
    /// 获取全部条目（含隐式依赖条目）。返回内部缓存数组的只读视图。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> GetAllEntries()
    {
        return _entries;
    }

    private static void AddGroup(
        Dictionary<string, List<RuntimeAssetEntry>> groups,
        string key,
        RuntimeAssetEntry entry)
    {
        if (!groups.TryGetValue(key, out List<RuntimeAssetEntry> list))
        {
            list = new List<RuntimeAssetEntry>();
            groups[key] = list;
        }

        list.Add(entry);
    }

    private static string[] CollectAddresses(List<RuntimeAssetEntry> entries)
    {
        var addresses = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            addresses[i] = entries[i].Address;
        return addresses;
    }
}

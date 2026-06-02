using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AB 资源清单 — 完整描述一次构建产出的所有资源与 Bundle 的映射关系。
/// 
/// 使用流程：
/// 1. DeserializeFromJson() 反序列化得到实例
/// 2. Initialize() 自动被调用，构建运行时索引
/// 3. 通过 TryGetAssets* / GetBundle* 方法查询资源和 Bundle 信息
/// </summary>
[Serializable]
[BinarySerializable(Magic = 0x41424D46, SchemaVersion = 1)]
public class ABManifest
{
    #region 序列化字段

    /// <summary>包裹标识（如 "MainPackage"）</summary>
    [BinaryField(0)]
    public string PackageName;

    /// <summary>包裹版本号</summary>
    [BinaryField(1)]
    public VersionNumber PackageVersion;

    /// <summary>构建时间戳（ISO 8601 格式，调试用）</summary>
    [BinaryField(2)]
    public string BuildTimestamp;

    /// <summary>所有资源条目</summary>
    [BinaryField(3)]
    public List<ManifestAssetEntry> AssetEntries = new();

    /// <summary>所有 Bundle 条目</summary>
    [BinaryField(4)]
    public List<ManifestBundleEntry> BundleEntries = new();

    #endregion

    #region 运行时索引（不序列化，由 Initialize 构建）
    /// <summary>Address -> AssetEntry 索引列表（支持重复 Address）</summary>
    [NonSerialized] private Dictionary<string, List<int>> _addressIndex;

    /// <summary>EntryId -> AssetEntry 索引（唯一）</summary>
    [NonSerialized] private Dictionary<string, int> _entryIdIndex;

    /// <summary>PrimaryType -> AssetEntry 索引列表</summary>
    [NonSerialized] private Dictionary<string, List<int>> _typeIndex;

    /// <summary>Label -> AssetEntry 索引列表（大小写不敏感）</summary>
    [NonSerialized] private Dictionary<string, List<int>> _labelIndex;

    /// <summary>BundleName -> BundleEntry 索引</summary>
    [NonSerialized] private Dictionary<string, int> _bundleNameIndex;

    /// <summary>标记是否已初始化</summary>
    [NonSerialized] private bool _initialized;

    #endregion

    #region 初始化

    /// <summary>
    /// 构建运行时索引。反序列化后必须调用。
    /// 全部使用 for 循环，不使用 LINQ，避免运行时 GC 压力。
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        int assetCount = AssetEntries != null ? AssetEntries.Count : 0;
        int bundleCount = BundleEntries != null ? BundleEntries.Count : 0;

        // 1. EntryId -> 索引（唯一映射）
        _entryIdIndex = new Dictionary<string, int>(assetCount);
        for (int i = 0; i < assetCount; i++)
        {
            var entry = AssetEntries[i];
            if (!string.IsNullOrEmpty(entry.EntryId))
                _entryIdIndex[entry.EntryId] = i;
        }

        // 2. Address -> 索引列表（允许重复）
        _addressIndex = new Dictionary<string, List<int>>(assetCount);
        for (int i = 0; i < assetCount; i++)
        {
            string addr = AssetEntries[i].Address;
            if (string.IsNullOrEmpty(addr)) continue;

            if (!_addressIndex.TryGetValue(addr, out var list))
            {
                list = new List<int>(1);
                _addressIndex[addr] = list;
            }
            list.Add(i);
        }

        // 3. PrimaryType -> 索引列表
        _typeIndex = new Dictionary<string, List<int>>();
        for (int i = 0; i < assetCount; i++)
        {
            string type = AssetEntries[i].PrimaryType;
            if (string.IsNullOrEmpty(type)) continue;

            if (!_typeIndex.TryGetValue(type, out var list))
            {
                list = new List<int>();
                _typeIndex[type] = list;
            }
            list.Add(i);
        }

        // 4. Label -> 索引列表（大小写不敏感，与 RuntimeAssetEntry.HasLabel 策略一致）
        _labelIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assetCount; i++)
        {
            var labels = AssetEntries[i].Labels;
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

        // 5. BundleName -> 索引
        _bundleNameIndex = new Dictionary<string, int>(bundleCount);
        for (int i = 0; i < bundleCount; i++)
        {
            var bundle = BundleEntries[i];
            if (string.IsNullOrEmpty(bundle.BundleName))
                continue;
            if (_bundleNameIndex.ContainsKey(bundle.BundleName))
                throw new InvalidOperationException($"Duplicate ManifestBundleEntry.BundleName: {bundle.BundleName}");
            _bundleNameIndex[bundle.BundleName] = i;
        }

        // 6. 填充 IncludeAssets 反向映射
        for (int i = 0; i < bundleCount; i++)
        {
            BundleEntries[i].IncludeAssets = new List<ManifestAssetEntry>();
            BundleEntries[i].ReferencedByBundleIndices = new List<int>();
        }

        for (int i = 0; i < assetCount; i++)
        {
            int bundleIdx = AssetEntries[i].BundleIndex;
            if (bundleIdx >= 0 && bundleIdx < bundleCount)
                BundleEntries[bundleIdx].IncludeAssets.Add(AssetEntries[i]);
        }

        // 7. 构建反向依赖索引：遍历每个 Bundle 的 DependBundleIndices，
        //    将当前 Bundle 索引添加到被依赖 Bundle 的 ReferencedByBundleIndices 中
        for (int i = 0; i < bundleCount; i++)
        {
            var deps = BundleEntries[i].DependBundleIndices;
            if (deps == null) continue;
            for (int j = 0; j < deps.Length; j++)
            {
                int depIdx = deps[j];
                if (depIdx >= 0 && depIdx < bundleCount)
                    BundleEntries[depIdx].ReferencedByBundleIndices.Add(i);
            }
        }

        _initialized = true;
    }

    #endregion

    #region 资源查询

    /// <summary>
    /// 按 Address 查找资源条目（Address 允许重复，返回所有匹配项）。
    /// </summary>
    public bool TryGetAssetsByAddress(string address, out List<ManifestAssetEntry> results)
    {
        results = null;
        if (_addressIndex == null || string.IsNullOrEmpty(address))
            return false;
        if (!_addressIndex.TryGetValue(address, out var indices))
            return false;

        results = new List<ManifestAssetEntry>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
            results.Add(AssetEntries[indices[i]]);
        return true;
    }

    /// <summary>
    /// 按 EntryId 查找资源条目（唯一映射）。
    /// </summary>
    public bool TryGetAssetByEntryId(string entryId, out ManifestAssetEntry result)
    {
        result = null;
        if (_entryIdIndex == null || string.IsNullOrEmpty(entryId))
            return false;
        if (!_entryIdIndex.TryGetValue(entryId, out int index))
            return false;
        result = AssetEntries[index];
        return true;
    }

    /// <summary>
    /// 按 PrimaryType 查找所有资源条目。
    /// </summary>
    public bool TryGetAssetsByType(string primaryType, out List<ManifestAssetEntry> results)
    {
        results = null;
        if (_typeIndex == null || string.IsNullOrEmpty(primaryType))
            return false;
        if (!_typeIndex.TryGetValue(primaryType, out var indices))
            return false;

        results = new List<ManifestAssetEntry>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
            results.Add(AssetEntries[indices[i]]);
        return true;
    }

    /// <summary>
    /// 按 Label 查找所有资源条目（大小写不敏感）。
    /// </summary>
    public bool TryGetAssetsByLabel(string label, out List<ManifestAssetEntry> results)
    {
        results = null;
        if (_labelIndex == null || string.IsNullOrEmpty(label))
            return false;
        if (!_labelIndex.TryGetValue(label, out var indices))
            return false;

        results = new List<ManifestAssetEntry>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
            results.Add(AssetEntries[indices[i]]);
        return true;
    }

    /// <summary>
    /// 获取所有资源条目数量。
    /// </summary>
    public int AssetCount => AssetEntries != null ? AssetEntries.Count : 0;

    /// <summary>
    /// 获取所有 Bundle 条目数量。
    /// </summary>
    public int BundleCount => BundleEntries != null ? BundleEntries.Count : 0;

    #endregion

    #region Bundle 查询

    /// <summary>
    /// 获取资源条目所属的 Bundle 条目。
    /// </summary>
    public ManifestBundleEntry GetBundleForAsset(ManifestAssetEntry assetEntry)
    {
        if (assetEntry == null || BundleEntries == null) return null;
        int idx = assetEntry.BundleIndex;
        if (idx >= 0 && idx < BundleEntries.Count)
            return BundleEntries[idx];
        return null;
    }

    /// <summary>
    /// 获取 Bundle 的直接依赖列表。
    /// 注意：递归展开由 ABBundleLoader 负责，此方法只返回直接依赖。
    /// </summary>
    public List<ManifestBundleEntry> GetDirectDependencies(ManifestBundleEntry bundleEntry)
    {
        if (bundleEntry == null || bundleEntry.DependBundleIndices == null)
            return new List<ManifestBundleEntry>(0);

        var deps = bundleEntry.DependBundleIndices;
        var result = new List<ManifestBundleEntry>(deps.Length);
        for (int i = 0; i < deps.Length; i++)
        {
            int depIdx = deps[i];
            if (depIdx >= 0 && depIdx < BundleEntries.Count)
                result.Add(BundleEntries[depIdx]);
        }
        return result;
    }

    /// <summary>
    /// 按 BundleName 查找 Bundle 条目。
    /// </summary>
    public bool TryGetBundleByName(string bundleName, out ManifestBundleEntry result)
    {
        result = null;
        if (_bundleNameIndex == null || string.IsNullOrEmpty(bundleName))
            return false;
        if (!_bundleNameIndex.TryGetValue(bundleName, out int index))
            return false;
        result = BundleEntries[index];
        return true;
    }

    #endregion

    #region 序列化

    /// <summary>
    /// 从 JSON 反序列化并自动初始化运行时索引。
    /// </summary>
    public static ABManifest DeserializeFromJson(string json)
    {
        var manifest = SerializationUtility.DeserializeJson<ABManifest>(json);
        manifest.Initialize();
        return manifest;
    }

    /// <summary>
    /// 序列化为 JSON 字符串。
    /// </summary>
    public string SerializeToJson(bool prettyPrint = false)
    {
        return SerializationUtility.SerializeToJson(this, prettyPrint);
    }

    /// <summary>
    /// 从文件路径反序列化并自动初始化运行时索引。
    /// 自动探测格式（.bin 二进制 或 .json JSON）。
    /// </summary>
    public static ABManifest DeserializeFromFile(string path)
    {
        var manifest = SerializationUtility.ReadFromFile<ABManifest>(path);
        manifest.Initialize();
        return manifest;
    }

    #endregion
}

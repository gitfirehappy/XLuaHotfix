using System;
using System.Collections.Generic;

/// <summary>
/// AB 资源清单 —— 完整描述一次构建产出的资源与内容（物理文件）的映射关系。
///
/// 使用流程：
/// 1. DeserializeFromJson() / DeserializeFromFile() 反序列化得到实例，并自动 Initialize()
/// 2. 通过 TryGetAssetsByAddress / TryGetAssetByEntryId / TryGetContentByFileName 查询
///
/// 地址语义：Address 只对公共条目（IsPublic=true，即显式采集的资源）有意义，
/// 隐式依赖条目不写 Address，也不进入地址索引；地址索引只服务公共资源的加载与查询。
/// </summary>
[Serializable]
[BinarySerializable(Magic = 0x41424D46, SchemaVersion = 6)]
public class ABManifest
{

    /// <summary>包裹版本号</summary>
    [BinaryField(0)]
    public VersionNumber PackageVersion;

    /// <summary>所有资源条目；显式采集与隐式依赖都在这里，用 IsPublic 区分</summary>
    [BinaryField(1)]
    public List<ManifestAssetEntry> AssetEntries = new();

    /// <summary>所有内容条目（AssetBundle 与 RawFile 统一承载）</summary>
    [BinaryField(2)]
    public List<ManifestContentEntry> ContentEntries = new();

    /// <summary>公共 Address -> AssetEntry 索引列表；只含 IsPublic 条目</summary>
    [NonSerialized] private Dictionary<string, List<int>> _addressIndex;

    /// <summary>EntryId -> AssetEntry 索引（唯一）</summary>
    [NonSerialized] private Dictionary<string, int> _entryIdIndex;

    /// <summary>FileName -> ContentEntry 索引</summary>
    [NonSerialized] private Dictionary<string, int> _contentFileNameIndex;

    /// <summary>ContentIndex -> 该内容包含的资产条目下标；按需构建，不序列化</summary>
    [NonSerialized] private List<List<int>> _assetIndicesByContent;

    /// <summary>标记是否已初始化</summary>
    [NonSerialized] private bool _initialized;

    /// <summary>
    /// 构建运行时索引。反序列化后必须调用。
    /// </summary>
    /// <remarks>
    /// 索引只做查询加速，不做数据校验；重复或非法的条目由构建期 VerifyABContent 阻断，
    /// 运行时不因为坏数据抛异常而拒绝加载整份清单。
    /// </remarks>
    public void Initialize()
    {
        if (_initialized) return;

        int assetCount = AssetEntries != null ? AssetEntries.Count : 0;
        int contentCount = ContentEntries != null ? ContentEntries.Count : 0;

        _entryIdIndex = new Dictionary<string, int>(assetCount);
        for (int i = 0; i < assetCount; i++)
        {
            var entry = AssetEntries[i];
            if (entry != null && !string.IsNullOrEmpty(entry.EntryId))
                _entryIdIndex[entry.EntryId] = i;
        }

        // 只有公共条目的 Address 有意义；隐式依赖条目的 Address 为空，天然不进索引
        _addressIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assetCount; i++)
        {
            var entry = AssetEntries[i];
            if (entry == null || !entry.IsPublic) continue;

            string addr = entry.Address;
            if (string.IsNullOrEmpty(addr)) continue;

            if (!_addressIndex.TryGetValue(addr, out var list))
            {
                list = new List<int>(1);
                _addressIndex[addr] = list;
            }
            list.Add(i);
        }

        _contentFileNameIndex = new Dictionary<string, int>(contentCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < contentCount; i++)
        {
            var content = ContentEntries[i];
            if (content == null || string.IsNullOrEmpty(content.FileName))
                continue;

            // 同名内容只保留首个；重复文件名由构建期校验阻断
            if (!_contentFileNameIndex.ContainsKey(content.FileName))
                _contentFileNameIndex[content.FileName] = i;
        }

        _initialized = true;
    }

    /// <summary>
    /// 按公共 Address 查找资源条目（大小写不敏感）。
    /// 契约上公共 Address 在单包内唯一，返回列表是为了让调用方在坏数据下也能显式消歧。
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
    /// 按输出文件名查找内容条目（大小写不敏感）。
    /// </summary>
    public bool TryGetContentByFileName(string fileName, out ManifestContentEntry result)
    {
        result = null;
        if (_contentFileNameIndex == null || string.IsNullOrEmpty(fileName))
            return false;
        if (!_contentFileNameIndex.TryGetValue(fileName, out int index))
            return false;
        result = ContentEntries[index];
        return true;
    }

    /// <summary>
    /// 获取资源条目数量。
    /// </summary>
    public int AssetCount => AssetEntries != null ? AssetEntries.Count : 0;

    /// <summary>
    /// 获取内容条目数量。
    /// </summary>
    public int ContentCount => ContentEntries != null ? ContentEntries.Count : 0;

    /// <summary>
    /// 获取资源条目所属的内容条目；ContentIndex 越界或不在清单内时返回 null。
    /// </summary>
    public ManifestContentEntry GetContentForAsset(ManifestAssetEntry assetEntry)
    {
        if (assetEntry == null || ContentEntries == null) return null;
        int idx = assetEntry.ContentIndex;
        if (idx >= 0 && idx < ContentEntries.Count)
            return ContentEntries[idx];
        return null;
    }

    /// <summary>
    /// 获取内容的直接依赖列表。
    /// 注意：递归展开由 ABBundleLoader 负责，此方法只返回直接依赖。
    /// </summary>
    public List<ManifestContentEntry> GetDirectDependencies(ManifestContentEntry contentEntry)
    {
        if (contentEntry == null || contentEntry.DependencyIndices == null)
            return new List<ManifestContentEntry>(0);

        var deps = contentEntry.DependencyIndices;
        var result = new List<ManifestContentEntry>(deps.Length);
        for (int i = 0; i < deps.Length; i++)
        {
            int depIdx = deps[i];
            if (depIdx >= 0 && ContentEntries != null && depIdx < ContentEntries.Count)
                result.Add(ContentEntries[depIdx]);
        }
        return result;
    }

    /// <summary>
    /// 获取指定内容包含的资产条目。
    /// 该索引按需构建，只存在于运行时，不是内容条目的字段。
    /// </summary>
    public IReadOnlyList<ManifestAssetEntry> GetAssetsInContent(int contentIndex)
    {
        if (ContentEntries == null || contentIndex < 0 || contentIndex >= ContentEntries.Count)
            return Array.Empty<ManifestAssetEntry>();

        if (_assetIndicesByContent == null)
            BuildAssetIndicesByContent();

        return _assetIndicesByContent[contentIndex].Count == 0
            ? Array.Empty<ManifestAssetEntry>()
            : CollectAssets(_assetIndicesByContent[contentIndex]);
    }

    private void BuildAssetIndicesByContent()
    {
        _assetIndicesByContent = new List<List<int>>(ContentEntries.Count);
        for (int i = 0; i < ContentEntries.Count; i++)
            _assetIndicesByContent.Add(new List<int>());

        int assetCount = AssetEntries != null ? AssetEntries.Count : 0;
        for (int i = 0; i < assetCount; i++)
        {
            var entry = AssetEntries[i];
            if (entry == null) continue;
            int contentIndex = entry.ContentIndex;
            if (contentIndex < 0 || contentIndex >= _assetIndicesByContent.Count)
                continue;
            _assetIndicesByContent[contentIndex].Add(i);
        }
    }

    private List<ManifestAssetEntry> CollectAssets(List<int> indices)
    {
        var result = new List<ManifestAssetEntry>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
            result.Add(AssetEntries[indices[i]]);
        return result;
    }

    /// <summary>
    /// 从 JSON 反序列化并自动初始化运行时索引。
    /// </summary>
    public static ABManifest DeserializeFromJson(string json)
    {
        if (!VersionNumber.JsonHasObjectField(json, nameof(PackageVersion)))
            throw new System.IO.InvalidDataException("ABManifest JSON 缺少 PackageVersion 对象。");

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
        byte[] data = FileHelper.ReadAllBytes(path);
        if (!BinaryHeader.HasValidMagic(data) && !VersionNumber.JsonHasObjectField(data, nameof(PackageVersion)))
            throw new System.IO.InvalidDataException("ABManifest JSON 缺少 PackageVersion 对象。");

        var manifest = SerializationUtility.Deserialize<ABManifest>(data);
        manifest.Initialize();
        return manifest;
    }
}

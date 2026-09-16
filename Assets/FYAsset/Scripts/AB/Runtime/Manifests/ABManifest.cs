using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AB 资源清单：公共 Address 到物理 Content 的映射，以及所有 Content 的文件事实。
/// 初始化同时完成运行时索引和 Manifest-local 引用校验；发现坏数据立即失败。
/// </summary>
[Serializable]
[BinarySerializable(Magic = 0x41424D46, SchemaVersion = 7)]
public sealed class ABManifest
{
    [BinaryField(0)]
    public VersionNumber PackageVersion;

    /// <summary>仅包含公共运行时资源；隐式依赖不生成 AssetEntry。</summary>
    [BinaryField(1)]
    public List<ManifestAssetEntry> AssetEntries = new();

    /// <summary>所有物理 Content 文件，包括没有公共映射的隐式依赖 Content。</summary>
    [BinaryField(2)]
    public List<ManifestContentEntry> ContentEntries = new();

    [NonSerialized] private Dictionary<string, int> _addressIndex;
    [NonSerialized] private Dictionary<string, int> _contentFileNameIndex;
    [NonSerialized] private List<List<int>> _assetIndicesByContent;
    [NonSerialized] private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;

        int contentCount = ContentEntries?.Count ?? 0;
        _contentFileNameIndex = new Dictionary<string, int>(contentCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < contentCount; i++)
        {
            ManifestContentEntry content = ContentEntries[i];
            if (content == null || string.IsNullOrWhiteSpace(content.FileName))
                throw new InvalidDataException($"ContentEntries[{i}] 缺少 FileName。");
            if (!_contentFileNameIndex.TryAdd(content.FileName, i))
                throw new InvalidDataException($"Content 文件名重复（大小写不敏感）: {content.FileName}");

            int[] dependencies = content.DependencyIndices ?? Array.Empty<int>();
            var seenDependencies = new HashSet<int>();
            for (int d = 0; d < dependencies.Length; d++)
            {
                int dependency = dependencies[d];
                if (dependency < 0 || dependency >= contentCount)
                    throw new InvalidDataException($"Content '{content.FileName}' 的 DependencyIndices[{d}] 越界: {dependency}");
                if (dependency == i)
                    throw new InvalidDataException($"Content '{content.FileName}' 包含自依赖。");
                if (!seenDependencies.Add(dependency))
                    throw new InvalidDataException($"Content '{content.FileName}' 的 DependencyIndices 重复: {dependency}");
            }
        }

        int assetCount = AssetEntries?.Count ?? 0;
        _addressIndex = new Dictionary<string, int>(assetCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assetCount; i++)
        {
            ManifestAssetEntry asset = AssetEntries[i];
            if (asset == null || string.IsNullOrWhiteSpace(asset.Address))
                throw new InvalidDataException($"AssetEntries[{i}] 缺少公共 Address。");
            if (asset.ContentIndex < 0 || asset.ContentIndex >= contentCount)
                throw new InvalidDataException($"Asset '{asset.Address}' 的 ContentIndex 越界: {asset.ContentIndex}");
            if (!_addressIndex.TryAdd(asset.Address, i))
                throw new InvalidDataException($"公共 Address 重复（大小写不敏感）: {asset.Address}");
        }

        _initialized = true;
    }

    public bool TryGetAssetByAddress(string address, out ManifestAssetEntry result)
    {
        result = null;
        if (_addressIndex == null || string.IsNullOrWhiteSpace(address)
            || !_addressIndex.TryGetValue(address, out int index))
            return false;
        result = AssetEntries[index];
        return true;
    }

    public bool TryGetContentByFileName(string fileName, out ManifestContentEntry result)
    {
        result = null;
        if (_contentFileNameIndex == null || string.IsNullOrWhiteSpace(fileName)
            || !_contentFileNameIndex.TryGetValue(fileName, out int index))
            return false;
        result = ContentEntries[index];
        return true;
    }

    public int AssetCount => AssetEntries?.Count ?? 0;
    public int ContentCount => ContentEntries?.Count ?? 0;

    public ManifestContentEntry GetContentForAsset(ManifestAssetEntry assetEntry)
    {
        if (assetEntry == null || ContentEntries == null)
            return null;
        int index = assetEntry.ContentIndex;
        return index >= 0 && index < ContentEntries.Count ? ContentEntries[index] : null;
    }

    public List<ManifestContentEntry> GetDirectDependencies(ManifestContentEntry contentEntry)
    {
        var result = new List<ManifestContentEntry>();
        if (contentEntry?.DependencyIndices == null || ContentEntries == null)
            return result;
        for (int i = 0; i < contentEntry.DependencyIndices.Length; i++)
        {
            int index = contentEntry.DependencyIndices[i];
            if (index >= 0 && index < ContentEntries.Count)
                result.Add(ContentEntries[index]);
        }
        return result;
    }

    public IReadOnlyList<ManifestAssetEntry> GetAssetsInContent(int contentIndex)
    {
        if (ContentEntries == null || contentIndex < 0 || contentIndex >= ContentEntries.Count)
            return Array.Empty<ManifestAssetEntry>();
        if (_assetIndicesByContent == null)
        {
            _assetIndicesByContent = new List<List<int>>(ContentEntries.Count);
            for (int i = 0; i < ContentEntries.Count; i++)
                _assetIndicesByContent.Add(new List<int>());
            for (int i = 0; i < AssetEntries.Count; i++)
                _assetIndicesByContent[AssetEntries[i].ContentIndex].Add(i);
        }

        List<int> indices = _assetIndicesByContent[contentIndex];
        var result = new List<ManifestAssetEntry>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
            result.Add(AssetEntries[indices[i]]);
        return result;
    }

    public static ABManifest DeserializeFromJson(string json)
    {
        if (!VersionNumber.JsonHasObjectField(json, nameof(PackageVersion)))
            throw new InvalidDataException("ABManifest JSON 缺少 PackageVersion 对象。");
        ABManifest manifest = SerializationUtility.DeserializeJson<ABManifest>(json);
        manifest.Initialize();
        return manifest;
    }

    public string SerializeToJson(bool prettyPrint = false)
        => SerializationUtility.SerializeToJson(this, prettyPrint);

    public static ABManifest DeserializeFromFile(string path)
    {
        byte[] data = FileHelper.ReadAllBytes(path);
        ABManifest manifest = BinaryHeader.HasValidMagic(data)
            ? SerializationUtility.Deserialize<ABManifest>(data)
            : SerializationUtility.DeserializeJson<ABManifest>(System.Text.Encoding.UTF8.GetString(data));
        manifest.Initialize();
        return manifest;
    }
}

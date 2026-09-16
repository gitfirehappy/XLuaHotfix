using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class ManifestContentEntry
{
    public string FileName;
    public AssetContentType ContentType = AssetContentType.SerializedObject;
    internal string[] DependencyNames = Array.Empty<string>();
}

public sealed class ABManifest
{
    private readonly Dictionary<string, ManifestContentEntry> _contents =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ManifestContentEntry> _contentList = new();

    /// <summary>资源条目；公共与隐式依赖条目都在这里，用 IsPublic 区分。</summary>
    public List<ManifestAssetEntry> AssetEntries = new();

    public ABManifest Add(string fileName, params string[] dependencies)
    {
        var entry = new ManifestContentEntry
        {
            FileName = fileName,
            DependencyNames = dependencies ?? Array.Empty<string>()
        };
        _contents[fileName] = entry;
        _contentList.Add(entry);
        return this;
    }

    public ABManifest AddContent(string fileName, AssetContentType contentType)
    {
        var entry = new ManifestContentEntry
        {
            FileName = fileName,
            ContentType = contentType
        };
        _contents[fileName] = entry;
        _contentList.Add(entry);
        return this;
    }

    public ABManifest AddAsset(ManifestAssetEntry entry)
    {
        AssetEntries.Add(entry);
        return this;
    }

    /// <summary>按 Address 定位资源条目。</summary>
    public bool TryGetAssetByAddress(string address, out ManifestAssetEntry result)
    {
        for (int i = 0; i < AssetEntries.Count; i++)
        {
            if (string.Equals(AssetEntries[i].Address, address, StringComparison.OrdinalIgnoreCase))
            {
                result = AssetEntries[i];
                return true;
            }
        }

        result = null;
        return false;
    }

    /// <summary>按资源条目的 ContentIndex 返回内容条目；下标越界时回退到第一个内容条目。</summary>
    public ManifestContentEntry GetContentForAsset(ManifestAssetEntry assetEntry)
    {
        if (assetEntry == null || _contentList.Count == 0)
            return null;

        int index = assetEntry.ContentIndex;
        if (index < 0 || index >= _contentList.Count)
            index = 0;
        return _contentList[index];
    }

    public bool TryGetContentByFileName(string fileName, out ManifestContentEntry result)
    {
        return _contents.TryGetValue(fileName, out result);
    }

    public List<ManifestContentEntry> GetDirectDependencies(ManifestContentEntry entry)
    {
        var result = new List<ManifestContentEntry>();
        if (entry?.DependencyNames == null) return result;
        for (int i = 0; i < entry.DependencyNames.Length; i++)
        {
            if (_contents.TryGetValue(entry.DependencyNames[i], out ManifestContentEntry dependency))
                result.Add(dependency);
        }
        return result;
    }
}

public static class RuntimePathManager
{
    /// <summary>本地热更包目录身份（诊断与切换用）。</summary>
    public static string CurrentGUIDRoot = "hotfix";

    /// <summary>当前激活包根 —— 加载器唯一的读取根。</summary>
    public static string ActivePackageRoot = "hotfix";
}

public class FYAssetSettings
{
    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= new FYAssetSettings();

    public const string BUNDLES_DIRECTORY_NAME = "bundles";
    public const string STANDALONE_DIRECTORY_NAME = "Standalone";

    public bool StandaloneBuild;
}

public static class FYAssetPathUtility
{
    public static string JoinFilePath(params string[] parts)
    {
        return string.Join("/", parts.Where(part => !string.IsNullOrEmpty(part))
            .Select(part => part.Replace('\\', '/').Trim('/')));
    }
}

public static class FileHelper
{
    public static bool Exists(string path) => FakeAssetBundleIO.Exists(path);

    /// <summary>RawFile 读取入口；场景中未准备真实文件，保持结构化失败。</summary>
    public static Task<byte[]> ReadAllBytesAsync(string path) =>
        Task.FromException<byte[]>(new System.IO.FileNotFoundException(path));
}

public sealed class PackageEntry
{
    public string key;
}

public sealed class TypeToKeys
{
    public string Type;
    public List<string> Keys = new();
}

public sealed class LabelToKeys
{
    public string Label;
    public List<string> Keys = new();
}

public sealed class AAManifest
{
    public List<PackageEntry> AssetEntries = new();
    public List<TypeToKeys> KeysByType = new();
    public List<LabelToKeys> KeysByLabel = new();
}

public static class AAManifestLoader
{
    public static AAManifest Manifest = new AAManifest
    {
        AssetEntries = new List<PackageEntry> { new PackageEntry { key = "dummy" } },
        KeysByType = new List<TypeToKeys>(),
        KeysByLabel = new List<LabelToKeys>()
    };

    public static Task<AAManifest> LoadAsync() => Task.FromResult(Manifest);
}

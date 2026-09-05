using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class ManifestBundleEntry
{
    public string BundleName;
    internal string[] DependencyNames = Array.Empty<string>();
}

public sealed class ABManifest
{
    private readonly Dictionary<string, ManifestBundleEntry> _bundles =
        new(StringComparer.OrdinalIgnoreCase);

    public ABManifest Add(string bundleName, params string[] dependencies)
    {
        _bundles[bundleName] = new ManifestBundleEntry
        {
            BundleName = bundleName,
            DependencyNames = dependencies ?? Array.Empty<string>()
        };
        return this;
    }

    public bool TryGetBundleByName(string bundleName, out ManifestBundleEntry result)
    {
        return _bundles.TryGetValue(bundleName, out result);
    }

    public List<ManifestBundleEntry> GetDirectDependencies(ManifestBundleEntry entry)
    {
        var result = new List<ManifestBundleEntry>();
        if (entry?.DependencyNames == null) return result;
        for (int i = 0; i < entry.DependencyNames.Length; i++)
        {
            if (_bundles.TryGetValue(entry.DependencyNames[i], out ManifestBundleEntry dependency))
                result.Add(dependency);
        }
        return result;
    }
}

public static class RuntimePathManager
{
    public static string CurrentGUIDRoot = "hotfix";
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

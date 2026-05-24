#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AB 输出侧 scanner。可从 ABManifest 直接复用 hash/CRC/size，也可独立扫描输出目录。
/// </summary>
public class AbBundleOutputArtifactScanner : IArtifactScanner
{
    private readonly IList<ManifestBundleEntry> _bundleEntries;
    private readonly string _outputDirectory;

    public AbBundleOutputArtifactScanner(IList<ManifestBundleEntry> bundleEntries)
    {
        _bundleEntries = bundleEntries;
    }

    public AbBundleOutputArtifactScanner(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public List<ArtifactDigest> Scan()
    {
        return _bundleEntries != null ? ScanManifestEntries() : ScanOutputDirectory();
    }

    private List<ArtifactDigest> ScanManifestEntries()
    {
        var result = new List<ArtifactDigest>(_bundleEntries.Count);
        for (int i = 0; i < _bundleEntries.Count; i++)
        {
            var entry = _bundleEntries[i];
            if (entry == null || string.IsNullOrEmpty(entry.BundleName))
                continue;

            result.Add(new ArtifactDigest
            {
                Name = entry.BundleName,
                Hash = entry.FileHash,
                CRC = entry.FileCRC,
                Size = entry.FileSize
            });
        }
        return result;
    }

    private List<ArtifactDigest> ScanOutputDirectory()
    {
        // 不限定 *.bundle；RawFile 和部分 Unity 输出文件可能没有 .bundle 扩展名。
        var files = FileHelper.GetFiles(_outputDirectory, "*", SearchOption.TopDirectoryOnly);
        var result = new List<ArtifactDigest>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            if (string.IsNullOrEmpty(path))
                continue;

            var info = new FileInfo(path);
            if (!info.Exists)
                continue;

            result.Add(new ArtifactDigest
            {
                Name = info.Name,
                Hash = HashGenerator.GenerateFileHash(path),
                CRC = HashGenerator.GenerateFileCRC(path),
                Size = info.Length
            });
        }
        return result;
    }
}
#endif

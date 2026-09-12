#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AB 包清单读取器：把 ABManifest 的内容条目映射成发布流程使用的文件摘要集合。
/// </summary>
/// <remarks>
/// Shared 不引用 ABManifest，因此清单解析留在 AB 侧，由调用方注入 IPackageManifestReader。
/// 摘要名称统一为包根相对路径（bundles/xxx），与发布流程的目录扫描口径一致。
/// </remarks>
public sealed class ABPackageManifestReader : IPackageManifestReader
{
    /// <summary>无状态实现，可直接共享。</summary>
    public static readonly ABPackageManifestReader Instance = new ABPackageManifestReader();

    /// <summary>按 ManifestOutputFormat 解析包根必须存在的清单文件名。</summary>
    public static IReadOnlyList<string> ResolveRequiredFileNames()
    {
        return FYAssetABSettings.Instance.ManifestOutputFormat switch
        {
            ManifestOutputFormat.JsonOnly => new[] { FYAssetSettings.MANIFEST_FILE_NAME },
            ManifestOutputFormat.BinaryOnly => new[] { FYAssetSettings.MANIFEST_FILE_NAME_BIN },
            _ => new[]
            {
                FYAssetSettings.MANIFEST_FILE_NAME,
                FYAssetSettings.MANIFEST_FILE_NAME_BIN
            }
        };
    }

    public IReadOnlyList<string> RequiredPackageFileNames => ResolveRequiredFileNames();

    public string ContentDirectoryName => FYAssetSettings.BUNDLES_DIRECTORY_NAME;

    public bool TryReadContentDigests(string packageDir, out IReadOnlyList<FileDigest> contents, out string error)
    {
        contents = new List<FileDigest>();
        error = string.Empty;

        if (string.IsNullOrEmpty(packageDir) || !FileHelper.DirectoryExists(packageDir))
        {
            error = $"包目录不存在: {packageDir}";
            return false;
        }

        if (!TryResolveManifestPath(packageDir, out string manifestPath))
        {
            error = $"包目录内没有 ABManifest: {packageDir}";
            return false;
        }

        ABManifest manifest;
        try
        {
            manifest = SerializationUtility.ReadFromFile<ABManifest>(manifestPath);
        }
        catch (Exception ex)
        {
            error = $"ABManifest 解析失败: {manifestPath} — {ex.Message}";
            return false;
        }

        if (manifest == null)
        {
            error = $"ABManifest 反序列化结果为空: {manifestPath}";
            return false;
        }

        var result = new List<FileDigest>();
        List<ManifestContentEntry> entries = manifest.ContentEntries;
        if (entries == null)
        {
            error = $"ABManifest 缺少内容条目: {manifestPath}";
            return false;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ManifestContentEntry entry = entries[i];
            if (entry == null)
                continue;

            if (string.IsNullOrEmpty(entry.FileName))
            {
                error = $"ABManifest 存在缺少文件名的内容条目: {manifestPath}";
                return false;
            }

            result.Add(new FileDigest(
                string.Concat(ContentDirectoryName, "/", entry.FileName),
                entry.FileHash,
                entry.FileCRC,
                entry.FileSize));
        }

        contents = result;
        return true;
    }

    /// <summary>按设置格式优先选择清单文件；配置的格式缺失时回退到实际存在的另一种。</summary>
    private static bool TryResolveManifestPath(string packageDir, out string manifestPath)
    {
        string json = FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.MANIFEST_FILE_NAME);
        string binary = FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN);

        bool preferBinary = FYAssetABSettings.Instance.ManifestOutputFormat == ManifestOutputFormat.BinaryOnly;
        manifestPath = preferBinary ? binary : json;
        if (FileHelper.Exists(manifestPath))
            return true;

        manifestPath = preferBinary ? json : binary;
        return FileHelper.Exists(manifestPath);
    }
}
#endif

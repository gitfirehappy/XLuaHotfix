#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AA 包清单读取器：把 AAManifest 的 Bundle 条目映射成发布流程使用的文件摘要集合。
/// </summary>
/// <remarks>
/// Shared 不引用 AAManifest，因此清单解析留在 AA 侧，由调用方注入 IPackageManifestReader。
/// 摘要名称统一为包根相对路径（bundles/xxx），与发布流程的目录扫描口径一致。
/// </remarks>
public sealed class AAPackageManifestReader : IPackageManifestReader
{
    /// <summary>无状态实现，可直接共享。</summary>
    public static readonly AAPackageManifestReader Instance = new AAPackageManifestReader();

    /// <summary>按 ManifestOutputFormat 解析包根必须存在的清单文件名（含 Addressables catalog）。</summary>
    public static IReadOnlyList<string> ResolveRequiredFileNames()
    {
        return FYAssetAASettings.Instance.ManifestOutputFormat switch
        {
            ManifestOutputFormat.JsonOnly => new[]
            {
                FYAssetSettings.AA_MANIFEST_FILE_NAME,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME
            },
            ManifestOutputFormat.BinaryOnly => new[]
            {
                FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME
            },
            _ => new[]
            {
                FYAssetSettings.AA_MANIFEST_FILE_NAME,
                FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME
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
            error = $"包目录内没有 AAManifest: {packageDir}";
            return false;
        }

        AAManifest manifest;
        try
        {
            manifest = SerializationUtility.ReadFromFile<AAManifest>(manifestPath);
        }
        catch (Exception ex)
        {
            error = $"AAManifest 解析失败: {manifestPath} — {ex.Message}";
            return false;
        }

        if (manifest == null)
        {
            error = $"AAManifest 反序列化结果为空: {manifestPath}";
            return false;
        }

        var result = new List<FileDigest>();
        List<BundleInfo> bundles = manifest.Bundles;
        if (bundles == null)
        {
            error = $"AAManifest 缺少 Bundle 列表: {manifestPath}";
            return false;
        }

        for (int i = 0; i < bundles.Count; i++)
        {
            BundleInfo bundle = bundles[i];
            if (bundle == null)
                continue;

            if (string.IsNullOrEmpty(bundle.BundleName))
            {
                error = $"AAManifest 存在缺少文件名的 Bundle 条目: {manifestPath}";
                return false;
            }

            result.Add(new FileDigest(
                string.Concat(ContentDirectoryName, "/", bundle.BundleName),
                bundle.FileHash,
                bundle.FileCRC,
                bundle.FileSize));
        }

        contents = result;
        return true;
    }

    /// <summary>按设置格式优先选择清单文件；配置的格式缺失时回退到实际存在的另一种。</summary>
    private static bool TryResolveManifestPath(string packageDir, out string manifestPath)
    {
        string json = FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binary = FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);

        bool preferBinary = FYAssetAASettings.Instance.ManifestOutputFormat == ManifestOutputFormat.BinaryOnly;
        manifestPath = preferBinary ? binary : json;
        if (FileHelper.Exists(manifestPath))
            return true;

        manifestPath = preferBinary ? json : binary;
        return FileHelper.Exists(manifestPath);
    }
}
#endif

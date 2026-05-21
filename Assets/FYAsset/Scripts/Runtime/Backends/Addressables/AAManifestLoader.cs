using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AA Manifest 加载器 — 负责从包目录读取 AAManifest.bin/.json 并反序列化。
///
/// 职责边界：
/// - 只加载 AA manifest 和其中的索引数据。
/// - 不初始化 Addressables catalog。
/// - 不加载资源对象；资源对象加载由 AddressablesBackend 负责。
///
/// 文件搜索顺序（每个目录）：
/// 1. AAManifest.bin（二进制格式，优先）
/// 2. AAManifest.json（JSON 格式，fallback）
/// </summary>
public static class AAManifestLoader
{
    private const string ManifestFileNameBin = FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
    private const string ManifestFileNameJson = FYAssetSettings.AA_MANIFEST_FILE_NAME;

    /// <summary>
    /// 从 RuntimePathManager.CurrentGUIDRoot 异步加载 AAManifest。
    /// </summary>
    public static Task<AAManifest> LoadAsync()
    {
        return LoadFromDirectoryAsync(RuntimePathManager.CurrentGUIDRoot);
    }

    /// <summary>
    /// 从指定包目录异步加载 AAManifest。
    /// </summary>
    public static async Task<AAManifest> LoadFromDirectoryAsync(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
        {
            Debug.LogWarning("[AAManifestLoader] packageRoot 为空，无法读取 AAManifest。");
            return null;
        }

        string binPath = Path.Combine(packageRoot, ManifestFileNameBin);
        string jsonPath = Path.Combine(packageRoot, ManifestFileNameJson);

        var manifest = await TryLoadFromFileAsync(binPath);
        if (manifest != null)
        {
            Debug.Log($"[AAManifestLoader] 从包目录加载二进制清单成功: {binPath}");
            return manifest;
        }

        manifest = await TryLoadFromFileAsync(jsonPath);
        if (manifest != null)
        {
            Debug.Log($"[AAManifestLoader] 从包目录加载 JSON 清单成功: {jsonPath}");
            return manifest;
        }

        Debug.LogWarning(
            $"[AAManifestLoader] AAManifest 加载失败。\n" +
            $"  Binary: {binPath}\n" +
            $"  JSON: {jsonPath}");
        return null;
    }

    /// <summary>
    /// 从指定包目录同步加载 AAManifest。
    /// </summary>
    public static AAManifest LoadFromDirectory(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
        {
            Debug.LogWarning("[AAManifestLoader] packageRoot 为空，无法读取 AAManifest。");
            return null;
        }

        string binPath = Path.Combine(packageRoot, ManifestFileNameBin);
        if (FileHelper.Exists(binPath))
            return TryLoadFromFile(binPath);

        string jsonPath = Path.Combine(packageRoot, ManifestFileNameJson);
        if (FileHelper.Exists(jsonPath))
            return TryLoadFromFile(jsonPath);

        Debug.LogWarning($"[AAManifestLoader] 未找到 AAManifest: {packageRoot}");
        return null;
    }

    private static async Task<AAManifest> TryLoadFromFileAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            byte[] data = await FileHelper.ReadAllBytesAsync(path);
            if (data == null || data.Length == 0)
            {
                Debug.LogWarning($"[AAManifestLoader] 文件内容为空: {path}");
                return null;
            }

            return SerializationUtility.Deserialize<AAManifest>(data);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }

    private static AAManifest TryLoadFromFile(string path)
    {
        try
        {
            return SerializationUtility.ReadFromFile<AAManifest>(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }
}

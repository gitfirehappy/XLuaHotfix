using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AAManifest 加载器 — 从当前激活包根读取 AAManifest.bin/.json 并反序列化。
/// </summary>
/// <remarks>
/// 只读取 RuntimePathManager.ActivePackageRoot 一个包根，不做跨目录回退。
/// 同一目录内优先 .bin，其次 .json；二进制读取失败后仍尝试 .json。
/// 不初始化 Addressables catalog，也不加载资源对象。
/// </remarks>
public static class AAManifestLoader
{
    private const string ManifestFileNameBin = FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
    private const string ManifestFileNameJson = FYAssetSettings.AA_MANIFEST_FILE_NAME;

    /// <summary>
    /// 加载当前激活包根下的 AAManifest；包根未激活或文件不可用时返回 null。
    /// </summary>
    public static async Task<AAManifest> LoadAsync()
    {
        string root = RuntimePathManager.ActivePackageRoot;
        if (string.IsNullOrEmpty(root))
        {
            Debug.LogError("[AAManifestLoader] 当前没有激活包根，无法加载 AAManifest。请先激活内置包或本地热更包。");
            return null;
        }

        AAManifest manifest = await LoadFromDirectoryAsync(root);
        if (manifest != null)
            return manifest;

        Debug.LogWarning($"[AAManifestLoader] AAManifest 在激活包根下加载失败: {root}");
        return null;
    }

    /// <summary>
    /// 在指定目录先读二进制再读 JSON；失败返回 null。
    /// </summary>
    public static async Task<AAManifest> LoadFromDirectoryAsync(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
        {
            Debug.LogWarning("[AAManifestLoader] packageRoot 为空，无法读取 AAManifest。");
            return null;
        }

        string binPath = FYAssetPathUtility.JoinFilePath(packageRoot, ManifestFileNameBin);
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageRoot, ManifestFileNameJson);

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
    /// 二进制文件存在则读二进制，否则读 JSON；失败返回 null。
    /// </summary>
    public static AAManifest LoadFromDirectory(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
        {
            Debug.LogWarning("[AAManifestLoader] packageRoot 为空，无法读取 AAManifest。");
            return null;
        }

        string binPath = FYAssetPathUtility.JoinFilePath(packageRoot, ManifestFileNameBin);
        if (FileHelper.Exists(binPath))
            return TryLoadFromFile(binPath);

        string jsonPath = FYAssetPathUtility.JoinFilePath(packageRoot, ManifestFileNameJson);
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

            AAManifest manifest = SerializationUtility.Deserialize<AAManifest>(data);
            if (manifest == null)
                return null;
            if (!BinaryHeader.HasValidMagic(data) && !VersionNumber.JsonHasObjectField(data, nameof(AAManifest.Version)))
            {
                Debug.LogWarning($"[AAManifestLoader] JSON 缺少 Version 对象: {path}");
                return null;
            }

            return manifest;
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
            byte[] data = FileHelper.ReadAllBytes(path);
            AAManifest manifest = SerializationUtility.Deserialize<AAManifest>(data);
            if (manifest == null)
                return null;
            if (!BinaryHeader.HasValidMagic(data) && !VersionNumber.JsonHasObjectField(data, nameof(AAManifest.Version)))
            {
                Debug.LogWarning($"[AAManifestLoader] JSON 缺少 Version 对象: {path}");
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 从包目录或 StreamingAssets 基线读取 AAManifest。
/// </summary>
/// <remarks>
/// LoadAsync 先读 CurrentGUIDRoot，失败后再读 StreamingAssets；按目录读取时不跨目录回退。
/// 异步在二进制读取失败后尝试 JSON；同步仅在二进制文件不存在时改读 JSON。JSON 必须含 Version 对象。
/// 不初始化 Addressables catalog，也不加载资源对象。
/// </remarks>
public static class AAManifestLoader
{
    private const string ManifestFileNameBin = FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
    private const string ManifestFileNameJson = FYAssetSettings.AA_MANIFEST_FILE_NAME;

    /// <summary>
    /// 加载当前 Manifest；失败后回退 StreamingAssets。
    /// </summary>
    public static async Task<AAManifest> LoadAsync()
    {
        var manifest = await LoadFromDirectoryAsync(RuntimePathManager.CurrentGUIDRoot);
        if (manifest != null)
            return manifest;

        manifest = await LoadFromDirectoryAsync(Application.streamingAssetsPath);
        if (manifest != null)
            return manifest;

        Debug.LogWarning("[AAManifestLoader] AAManifest 在热更目录与 StreamingAssets 中均加载失败。");
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

using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB Manifest 加载器 — 从当前激活包根读取 ABManifest.bin/.json 并反序列化。
/// </summary>
/// <remarks>
/// 只读取 RuntimePathManager.ActivePackageRoot 一个包根，不做跨目录回退。
/// 同一目录内优先 .bin，其次 .json；两者都不可用时返回 null 并输出错误。
/// </remarks>
public static class ABManifestLoader
{
    private const string ManifestFileNameBin = FYAssetSettings.MANIFEST_FILE_NAME_BIN;
    private const string ManifestFileNameJson = FYAssetSettings.MANIFEST_FILE_NAME;

    /// <summary>
    /// 异步加载 ABManifest；全部候选文件都不可用时返回 null。
    /// </summary>
    public static async Task<ABManifest> LoadAsync()
    {
        string root = RuntimePathManager.ActivePackageRoot;
        if (string.IsNullOrEmpty(root))
        {
            Debug.LogError("[ABManifestLoader] 当前没有激活包根，无法加载 ABManifest。请先激活内置包或本地热更包。");
            return null;
        }

        string binPath = FYAssetPathUtility.JoinFilePath(root, ManifestFileNameBin);
        string jsonPath = FYAssetPathUtility.JoinFilePath(root, ManifestFileNameJson);

        var manifest = await TryLoadFromFile(binPath);
        if (manifest != null)
        {
            Debug.Log($"[ABManifestLoader] 从激活包根加载二进制清单成功: {binPath}");
            return manifest;
        }

        manifest = await TryLoadFromFile(jsonPath);
        if (manifest != null)
        {
            Debug.Log($"[ABManifestLoader] 从激活包根加载 JSON 清单成功: {jsonPath}");
            return manifest;
        }

        Debug.LogError(
            $"[ABManifestLoader] ABManifest 加载失败。\n" +
            $"  激活包根: {root}\n" +
            $"  候选 (.bin): {binPath}\n" +
            $"  候选 (.json): {jsonPath}");
        return null;
    }

    /// <summary>
    /// 尝试从指定路径读取并反序列化 ABManifest。
    /// 文件不存在或反序列化失败返回 null。
    /// </summary>
    private static async Task<ABManifest> TryLoadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            byte[] data = await FileHelper.ReadAllBytesAsync(path);

            if (data == null || data.Length == 0)
            {
                Debug.LogWarning($"[ABManifestLoader] 文件内容为空: {path}");
                return null;
            }

            var manifest = SerializationUtility.Deserialize<ABManifest>(data);
            if (manifest == null)
                return null;
            if (!BinaryHeader.HasValidMagic(data) && !VersionNumber.JsonHasObjectField(data, nameof(ABManifest.PackageVersion)))
            {
                Debug.LogWarning($"[ABManifestLoader] JSON 缺少 PackageVersion 对象: {path}");
                return null;
            }

            manifest.Initialize();
            return manifest;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ABManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }
}

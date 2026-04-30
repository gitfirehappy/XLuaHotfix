using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 最小化清单加载器 — 负责从磁盘读取 ABManifest.bin/.json 并反序列化。
///
/// 路径策略：
/// 1. Primary: PathManager.CurrentGUIDRoot/（热更目录）
/// 2. Fallback: Application.streamingAssetsPath/（包内初始资源）
///
/// 文件搜索顺序（每个目录）：
/// 1. ABManifest.bin（二进制格式，优先）
/// 2. ABManifest.json（JSON 格式，fallback）
///
/// 当前实现使用 File.ReadAllBytes（同步 I/O + Task.Run 包装），并通过 SerializationUtility 自动探测格式。
/// Android StreamingAssets 路径需要 UnityWebRequest，已记录为后续多平台统一处理项。
/// </summary>
public static class ManifestLoader
{
    private const string ManifestFileNameBin = FYAssetConstants.MANIFEST_FILE_NAME_BIN;
    private const string ManifestFileNameJson = FYAssetConstants.MANIFEST_FILE_NAME;

    /// <summary>
    /// 异步加载 ABManifest。
    /// 优先从热更目录加载，失败后回退到 StreamingAssets。
    /// 每个目录内优先加载 .bin，失败后回退到 .json。
    /// 全部失败返回 null 并输出错误日志。
    /// </summary>
    public static async Task<ABManifest> LoadAsync()
    {
        string primaryDir = PathManager.CurrentGUIDRoot;
        string fallbackDir = Application.streamingAssetsPath;

        string primaryBinPath = Path.Combine(primaryDir, ManifestFileNameBin);
        string primaryJsonPath = Path.Combine(primaryDir, ManifestFileNameJson);
        string fallbackBinPath = Path.Combine(fallbackDir, ManifestFileNameBin);
        string fallbackJsonPath = Path.Combine(fallbackDir, ManifestFileNameJson);

        var manifest = await TryLoadFromFile(primaryBinPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从热更目录加载二进制清单成功: {primaryBinPath}");
            return manifest;
        }

        manifest = await TryLoadFromFile(primaryJsonPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从热更目录加载 JSON 清单成功: {primaryJsonPath}");
            return manifest;
        }

        manifest = await TryLoadFromFile(fallbackBinPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从 StreamingAssets 加载二进制清单成功: {fallbackBinPath}");
            return manifest;
        }

        manifest = await TryLoadFromFile(fallbackJsonPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从 StreamingAssets 加载 JSON 清单成功: {fallbackJsonPath}");
            return manifest;
        }

        Debug.LogError(
            $"[ManifestLoader] ABManifest 加载失败。\n" +
            $"  Primary (.bin): {primaryBinPath}\n" +
            $"  Primary (.json): {primaryJsonPath}\n" +
            $"  Fallback (.bin): {fallbackBinPath}\n" +
            $"  Fallback (.json): {fallbackJsonPath}");
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

        if (!File.Exists(path))
            return null;

        try
        {
            byte[] data = await FileHelper.ReadAllBytesAsync(path);

            if (data == null || data.Length == 0)
            {
                Debug.LogWarning($"[ManifestLoader] 文件内容为空: {path}");
                return null;
            }

            var manifest = SerializationUtility.Deserialize<ABManifest>(data);
            manifest.Initialize();
            return manifest;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }
}

using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 最小化清单加载器 — 负责从磁盘读取 ABManifest.json 并反序列化。
///
/// 路径策略（B6 审视确认）：
/// 1. Primary: PathManager.CurrentGUIDRoot/ABManifest.json（热更目录）
/// 2. Fallback: Application.streamingAssetsPath/ABManifest.json（包内初始资源）
///
/// 当前实现使用 File.ReadAllText（同步 I/O + Task.Run 包装）。
/// Android StreamingAssets 路径需要 UnityWebRequest，已记录为后续多平台统一处理项。
/// </summary>
public static class ManifestLoader
{
    /// <summary>清单文件固定名称</summary>
    private const string ManifestFileName = "ABManifest.json";

    /// <summary>
    /// 异步加载 ABManifest。
    /// 优先从热更目录加载，失败后回退到 StreamingAssets。
    /// 全部失败返回 null 并输出错误日志。
    /// </summary>
    public static async Task<ABManifest> LoadAsync()
    {
        // Primary: 热更目录
        string primaryPath = Path.Combine(PathManager.CurrentGUIDRoot, ManifestFileName);
        var manifest = await TryLoadFromFile(primaryPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从热更目录加载成功: {primaryPath}");
            return manifest;
        }

        // Fallback: StreamingAssets
        string fallbackPath = Path.Combine(Application.streamingAssetsPath, ManifestFileName);
        manifest = await TryLoadFromFile(fallbackPath);
        if (manifest != null)
        {
            Debug.Log($"[ManifestLoader] 从 StreamingAssets 加载成功: {fallbackPath}");
            return manifest;
        }

        Debug.LogError(
            $"[ManifestLoader] ABManifest 加载失败。\n" +
            $"  Primary: {primaryPath}\n" +
            $"  Fallback: {fallbackPath}");
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
            // 文件 I/O 放入线程池避免阻塞主线程
            string json = await Task.Run(() => File.ReadAllText(path));

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[ManifestLoader] 文件内容为空: {path}");
                return null;
            }

            var manifest = ABManifest.DeserializeFromJson(json);
            return manifest;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ManifestLoader] 读取失败: {path}\n{ex.Message}");
            return null;
        }
    }
}

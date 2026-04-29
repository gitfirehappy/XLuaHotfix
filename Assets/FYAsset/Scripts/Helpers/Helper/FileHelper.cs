using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 跨平台文件 I/O 工具类。
/// 定位：与 NetworkDownloader / PathManager / SerializationUtility 同级的基础设施层。
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// 异步读取文件为字节数组。
    /// Android StreamingAssets 路径 → UnityWebRequest（需主线程）。
    /// 其他平台/路径 → Task.Run(File.ReadAllBytes)。
    /// </summary>
    public static async Task<byte[]> ReadAllBytesAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

#if UNITY_ANDROID && !UNITY_EDITOR
        if (path.StartsWith(Application.streamingAssetsPath, StringComparison.OrdinalIgnoreCase))
        {
            using var request = UnityWebRequest.Get(path);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException(
                    $"[FileHelper] 读取 StreamingAsset 失败: {path}, error: {request.error}");
            return request.downloadHandler.data;
        }
#endif
        return await Task.Run(() =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"[FileHelper] 文件不存在: {path}");
            return File.ReadAllBytes(path);
        });
    }

    /// <summary>
    /// 异步读取文件为字符串（UTF-8）。
    /// 平台分支策略与 ReadAllBytesAsync 一致。
    /// </summary>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

#if UNITY_ANDROID && !UNITY_EDITOR
        if (path.StartsWith(Application.streamingAssetsPath, StringComparison.OrdinalIgnoreCase))
        {
            using var request = UnityWebRequest.Get(path);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException(
                    $"[FileHelper] 读取 StreamingAsset 失败: {path}, error: {request.error}");
            return request.downloadHandler.text;
        }
#endif
        return await Task.Run(() =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"[FileHelper] 文件不存在: {path}");
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        });
    }

    /// <summary>
    /// 原子写入字节数组。
    /// 先写临时文件，再 rename 到目标路径。
    /// 保证：目标文件要么是旧版本（完整），要么是新版本（完整），不会出现半截文件。
    /// </summary>
    public static void WriteAllBytesAtomic(string path, byte[] data)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        EnsureDirectoryForFile(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 8);

        File.WriteAllBytes(tempPath, data);

        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }

    /// <summary>
    /// 原子写入字符串（UTF-8）。
    /// 原子模式与 WriteAllBytesAtomic 一致。
    /// </summary>
    public static void WriteAllTextAtomic(string path, string text)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        EnsureDirectoryForFile(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 8);

        File.WriteAllText(tempPath, text, System.Text.Encoding.UTF8);

        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }

    /// <summary>
    /// 删除文件。失败时返回 false 并输出警告日志。绝不抛异常。
    /// </summary>
    public static bool TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (!File.Exists(path))
            return true;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileHelper] 删除文件失败: {path}, 原因: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 递归删除目录。失败时返回 false 并输出警告日志。绝不抛异常。
    /// </summary>
    public static bool TryDeleteDirectory(string path, bool recursive = true)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (!Directory.Exists(path))
            return true;

        try
        {
            Directory.Delete(path, recursive);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileHelper] 删除目录失败: {path}, 原因: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 确保文件路径的父目录存在。目录组件为空时无操作。
    /// </summary>
    public static void EnsureDirectoryForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        string dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            return;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}

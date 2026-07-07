using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 跨平台文件 I/O 工具类。
/// 定位：与 NetworkDownloader / RuntimePathManager / SerializationUtility 同级的基础设施层。
/// </summary>
public static class FileHelper
{
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

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

        File.WriteAllText(tempPath, text, Utf8NoBom);

        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }

    /// <summary>
    /// 原子写入字符串。encoding 参数仅用于兼容旧调用点签名，内部始终使用 UTF-8。
    /// </summary>
    public static void WriteAllTextAtomic(string path, string text, System.Text.Encoding _)
    {
        WriteAllTextAtomic(path, text);
    }

    /// <summary>
    /// 跨平台文件存在性检查。
    /// Android StreamingAssets 路径（jar: URI）无法用 File.Exists 检测，直接返回 false。
    /// </summary>
    public static bool Exists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (path.StartsWith(Application.streamingAssetsPath, StringComparison.OrdinalIgnoreCase))
            return false;
#endif
        return File.Exists(path);
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

    /// <summary>
    /// 确保目录存在。目录路径为空时无操作。
    /// 与 EnsureDirectoryForFile 不同，本方法直接作用于目录路径而非文件路径。
    /// </summary>
    public static void EnsureDirectory(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath))
            return;
        if (!Directory.Exists(dirPath))
            Directory.CreateDirectory(dirPath);
    }

    /// <summary>
    /// 跨平台目录存在性检查。
    /// </summary>
    public static bool DirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return Directory.Exists(path);
    }

    /// <summary>
    /// 拷贝文件。默认覆盖已存在目标。
    /// </summary>
    public static void CopyFile(string src, string dest, bool overwrite = true)
    {
        if (string.IsNullOrEmpty(src))
            throw new ArgumentNullException(nameof(src));
        if (string.IsNullOrEmpty(dest))
            throw new ArgumentNullException(nameof(dest));

        EnsureDirectoryForFile(dest);
        File.Copy(src, dest, overwrite);
    }

    /// <summary>
    /// 替换文件。目标存在时先删除，再移动源文件到目标路径。
    /// </summary>
    public static void ReplaceFile(string sourcePath, string targetPath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrEmpty(targetPath))
            throw new ArgumentNullException(nameof(targetPath));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"[FileHelper] 替换源文件不存在: {sourcePath}", sourcePath);

        EnsureDirectoryForFile(targetPath);
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(sourcePath, targetPath);
    }

    /// <summary>
    /// 拷贝文件，失败时返回 false 并输出警告日志。绝不抛异常。
    /// </summary>
    public static bool TryCopyFile(string src, string dest, bool overwrite = true)
    {
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dest))
            return false;
        if (!File.Exists(src))
        {
            Debug.LogWarning($"[FileHelper] 拷贝源文件不存在: {src}");
            return false;
        }

        try
        {
            EnsureDirectoryForFile(dest);
            File.Copy(src, dest, overwrite);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileHelper] 拷贝文件失败: {src} → {dest}, 原因: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 枚举目录下的子目录（返回绝对路径）。跨平台安全。
    /// </summary>
    public static string[] GetDirectories(string path, string searchPattern = "*")
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return new string[0];
        return Directory.GetDirectories(path, searchPattern);
    }

    /// <summary>
    /// 同步读取文件全部文本（UTF-8）。用于无法改为异步的同步调用点。
    /// </summary>
    public static string ReadAllText(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"[FileHelper] 文件不存在: {path}");
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 同步读取文件全部字节。仅支持真实文件系统路径。
    /// </summary>
    public static byte[] ReadAllBytes(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"[FileHelper] 文件不存在: {path}");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// 枚举目录下的文件。目录不存在时返回空数组。
    /// </summary>
    public static string[] GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return new string[0];
        return Directory.GetFiles(path, searchPattern, searchOption);
    }
}

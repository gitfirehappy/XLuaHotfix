using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Cross-platform file I/O utility.
/// Positioning: same tier as NetworkDownloader / PathManager / SerializationUtility.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Read entire file as byte array.
    /// Android StreamingAssets → UnityWebRequest (main thread required).
    /// Other paths / platforms → Task.Run(File.ReadAllBytes).
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
                    $"[FileHelper] Failed to read StreamingAsset: {path}, error: {request.error}");
            return request.downloadHandler.data;
        }
#endif
        return await Task.Run(() =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"[FileHelper] File not found: {path}");
            return File.ReadAllBytes(path);
        });
    }

    /// <summary>
    /// Read entire file as string (UTF-8).
    /// Same platform branching as ReadAllBytesAsync.
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
                    $"[FileHelper] Failed to read StreamingAsset: {path}, error: {request.error}");
            return request.downloadHandler.text;
        }
#endif
        return await Task.Run(() =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"[FileHelper] File not found: {path}");
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        });
    }

    /// <summary>
    /// Write byte array to file atomically.
    /// Writes to temp file first, then renames to target.
    /// Guarantees: target file is either old (complete) or new (complete), never partial.
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
    /// Write string to file atomically (UTF-8).
    /// Same atomic pattern as WriteAllBytesAtomic.
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
    /// Delete file. Returns false (logs warning) on failure. Never throws.
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
            Debug.LogWarning($"[FileHelper] Failed to delete file: {path}, reason: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete directory recursively. Returns false (logs warning) on failure. Never throws.
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
            Debug.LogWarning($"[FileHelper] Failed to delete directory: {path}, reason: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Create parent directory for a file path if it does not exist.
    /// Null or empty directory component → no-op.
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

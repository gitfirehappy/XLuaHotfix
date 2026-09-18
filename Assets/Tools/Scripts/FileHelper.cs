using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 跨平台文件 I/O、目录扫描、摘要比较和原子写入工具。
/// </summary>
public static class FileHelper
{
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

    #region Read

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

    #endregion

    #region Atomic Write

    /// <summary>
    /// 原子写入字节数组。
    /// 先写同目录临时文件，再原子替换到目标路径。
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
        try
        {
            File.WriteAllBytes(tempPath, data);
            ReplaceFile(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
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
        try
        {
            File.WriteAllText(tempPath, text, Utf8NoBom);
            ReplaceFile(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    #endregion

    #region File And Directory Operations

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
    /// 原子替换同一文件系统中的文件；目标不存在时直接移动。
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
            File.Replace(sourcePath, targetPath, null);
        else
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

    /// <summary>
    /// 递归统计目录内文件总大小。无法读取的文件会记录警告并跳过。
    /// </summary>
    public static long GetDirectorySize(string path)
    {
        if (!DirectoryExists(path))
            return 0L;

        long total = 0L;
        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileHelper] 读取文件大小失败：{file}，原因：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileHelper] 统计目录大小失败：{path}，已返回部分结果。原因：{ex.Message}");
        }

        return total;
    }

    /// <summary>字节数格式化为可读文本。</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>判断大小是否未超过阈值；非正阈值表示不限制。</summary>
    public static bool IsWithinSizeLimit(long sizeBytes, long maxSizeBytes)
    {
        return maxSizeBytes <= 0 || sizeBytes < maxSizeBytes;
    }

    #endregion

    #region File Digests And Diff

    /// <summary>扫描目录并为每个文件创建摘要。nameSelector 为空时使用文件名。</summary>
    public static bool TryScanFiles(
        string rootDir,
        Func<string, string> nameSelector,
        out List<FileDigest> files,
        out string error)
    {
        files = new List<FileDigest>();
        error = string.Empty;
        if (string.IsNullOrEmpty(rootDir) || !DirectoryExists(rootDir))
        {
            error = $"目录不存在: {rootDir}";
            return false;
        }

        string[] paths = GetFiles(rootDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < paths.Length; i++)
        {
            string name = nameSelector != null ? nameSelector(paths[i]) : Path.GetFileName(paths[i]);
            if (!TryCreateDigest(paths[i], name, out FileDigest digest))
            {
                error = $"文件摘要计算失败（缺失、被占用或不可读）: {paths[i]}";
                return false;
            }
            files.Add(digest);
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return true;
    }

    /// <summary>计算单个文件摘要。</summary>
    public static bool TryCreateDigest(string filePath, string name, out FileDigest digest)
    {
        digest = default;
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return false;

            HashGenerator.ComputeFileHashAndCRC(filePath, out string hash, out uint crc);
            digest = new FileDigest(string.IsNullOrEmpty(name) ? info.Name : name, hash, crc, info.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>按名称建立索引，重复名称保留首个条目。</summary>
    public static Dictionary<string, FileDigest> IndexByName(IReadOnlyList<FileDigest> digests)
    {
        var index = new Dictionary<string, FileDigest>(StringComparer.Ordinal);
        if (digests == null)
            return index;

        for (int i = 0; i < digests.Count; i++)
        {
            FileDigest item = digests[i];
            if (!string.IsNullOrEmpty(item.Name) && !index.ContainsKey(item.Name))
                index.Add(item.Name, item);
        }
        return index;
    }

    /// <summary>按 Hash 和大小建立内容索引，重复内容保留首个条目。</summary>
    public static Dictionary<string, FileDigest> IndexByHash(IReadOnlyList<FileDigest> digests)
    {
        var index = new Dictionary<string, FileDigest>(StringComparer.Ordinal);
        if (digests == null)
            return index;

        for (int i = 0; i < digests.Count; i++)
        {
            FileDigest item = digests[i];
            if (string.IsNullOrEmpty(item.Hash))
                continue;

            string key = HashKey(item.Hash, item.Size);
            if (!index.ContainsKey(key))
                index.Add(key, item);
        }
        return index;
    }

    /// <summary>比较两组摘要并返回四类结果。</summary>
    public static void ComputeDiff(
        IReadOnlyList<FileDigest> previous,
        IReadOnlyList<FileDigest> current,
        out List<FileDigest> added,
        out List<FileDigest> modified,
        out List<FileDigest> unchanged,
        out List<string> removed)
    {
        added = new List<FileDigest>();
        modified = new List<FileDigest>();
        unchanged = new List<FileDigest>();
        removed = new List<string>();
        Dictionary<string, FileDigest> previousByName = IndexByName(previous);

        if (current != null)
        {
            for (int i = 0; i < current.Count; i++)
            {
                FileDigest item = current[i];
                if (string.IsNullOrEmpty(item.Name))
                    continue;

                if (!previousByName.TryGetValue(item.Name, out FileDigest baseline))
                    added.Add(item);
                else if (baseline.Matches(item))
                    unchanged.Add(item);
                else
                    modified.Add(item);
            }
        }

        if (previous != null)
        {
            for (int i = 0; i < previous.Count; i++)
            {
                string name = previous[i].Name;
                if (!string.IsNullOrEmpty(name) && !ContainsName(current, name))
                    removed.Add(name);
            }
        }
    }

    /// <summary>收集新增和修改文件的名称。</summary>
    public static HashSet<string> CollectChangedNames(
        IReadOnlyList<FileDigest> added,
        IReadOnlyList<FileDigest> modified)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        AddNames(added, names);
        AddNames(modified, names);
        return names;
    }

    /// <summary>生成内容索引键。</summary>
    public static string HashKey(string hash, long size)
    {
        return hash + ":" + size;
    }

    private static void AddNames(IReadOnlyList<FileDigest> files, HashSet<string> names)
    {
        if (files == null)
            return;
        for (int i = 0; i < files.Count; i++)
            names.Add(files[i].Name);
    }

    private static bool ContainsName(IReadOnlyList<FileDigest> digests, string name)
    {
        if (digests == null)
            return false;
        for (int i = 0; i < digests.Count; i++)
        {
            if (string.Equals(digests[i].Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    #endregion

    #region Data Types

    public readonly struct FileDigest
    {
        public readonly string Name;
        public readonly string Hash;
        public readonly uint CRC;
        public readonly long Size;

        public FileDigest(string name, string hash, uint crc, long size)
        {
            Name = name;
            Hash = hash;
            CRC = crc;
            Size = size;
        }

        public bool IsComplete =>
            !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Hash) && Size >= 0;

        public bool Matches(in FileDigest other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(Hash, other.Hash, StringComparison.Ordinal)
                && CRC == other.CRC
                && Size == other.Size;
        }
    }

    #endregion
}

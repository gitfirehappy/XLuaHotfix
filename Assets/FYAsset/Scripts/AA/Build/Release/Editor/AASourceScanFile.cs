#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// AA 源快照文件：记录上一次成功构建时 Addressables 源（GUID → Asset+meta 摘要）的状态。
/// </summary>
/// <remarks>
/// 事实来源：该文件写在包根，Full 与 Hotfix 都随包交付；
/// 下一次 Hotfix 通过读取<b>作用域最近成功 Full 包</b>内这份文件得到“上次成功构建事实”，
/// 据此决定哪些资源需要临时移入 HotfixGroup，因此不再依赖独立 baseline 指针文件。
/// FileDigest 是 readonly struct，Unity 序列化不覆盖，落盘使用可序列化 DTO。
/// </remarks>
public static class AASourceScanFile
{
    public const string FileName = "AASourceScan.json";

    /// <summary>源快照文件在包目录内的路径。</summary>
    public static string ResolvePath(string packageDir) =>
        FYAssetPathUtility.JoinFilePath(packageDir, FileName);

    /// <summary>读取源快照；文件缺失或损坏时返回 false。</summary>
    public static bool TryRead(string packageDir, out List<FileDigest> files, out string error)
    {
        files = new List<FileDigest>();
        error = string.Empty;

        string path = ResolvePath(packageDir);
        if (!FileHelper.Exists(path))
        {
            error = $"源快照文件不存在: {path}";
            return false;
        }

        SourceScanDocument document;
        try
        {
            document = SerializationUtility.ReadFromFile<SourceScanDocument>(path);
        }
        catch (Exception ex)
        {
            error = $"源快照解析失败: {path} — {ex.Message}";
            return false;
        }

        if (document?.Files == null)
        {
            error = $"源快照缺少文件列表: {path}";
            return false;
        }

        for (int i = 0; i < document.Files.Count; i++)
        {
            SourceEntry entry = document.Files[i];
            if (entry == null || string.IsNullOrEmpty(entry.Name))
                continue;

            files.Add(new FileDigest(entry.Name, entry.Hash, entry.Crc, entry.Size));
        }

        return true;
    }

    /// <summary>写入源快照；供 ExportAAOutputTask 在包根落盘。</summary>
    public static void Write(string packageDir, IReadOnlyList<FileDigest> files)
    {
        var document = new SourceScanDocument { Files = new List<SourceEntry>() };
        if (files != null)
        {
            for (int i = 0; i < files.Count; i++)
            {
                FileDigest file = files[i];
                if (!file.IsComplete)
                    continue;

                document.Files.Add(new SourceEntry
                {
                    Name = file.Name,
                    Hash = file.Hash,
                    Crc = file.CRC,
                    Size = file.Size
                });
            }
        }

        FileHelper.EnsureDirectory(packageDir);
        FileHelper.WriteAllTextAtomic(ResolvePath(packageDir), SerializationUtility.SerializeToJson(document, true));
    }

    /// <summary>可序列化文档。</summary>
    [Serializable]
    public sealed class SourceScanDocument
    {
        public List<SourceEntry> Files = new();
    }

    /// <summary>可序列化条目。</summary>
    [Serializable]
    public sealed class SourceEntry
    {
        public string Name;
        public string Hash;
        public uint Crc;
        public long Size;
    }
}
#endif

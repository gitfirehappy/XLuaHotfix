#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 构建事实存储：正式摘要位于 BuildData/Summaries/{AA|AB}/{BuildPackageName}.json，
/// 索引位于 BuildData/Summaries/index.json。
/// </summary>
/// <remarks>
/// 只服务本地构建端，不进入包目录、StreamingAssets 或运行时读取链。
/// 摘要不可变：同一构建标识已存在且内容不同时拒绝覆盖；索引可重建。
/// 根目录由调用方注入，测试与工具可以指向临时目录。
/// </remarks>
public sealed class BuildSummaryStore
{
    public const string RootFolderName = "BuildData/Summaries";
    public const string IndexFileName = "index.json";

    private static readonly string[] Backends = { "AA", "AB" };

    public BuildSummaryStore(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("项目根不能为空。", nameof(projectRoot));

        RootDir = FYAssetPathUtility.JoinFilePath(
            FYAssetPathUtility.NormalizePath(projectRoot),
            RootFolderName.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>用当前项目根建立存储实例。</summary>
    public static BuildSummaryStore CreateDefault() => new(BuildPathManager.ProjectRoot);

    public string RootDir { get; }

    public string IndexPath => FYAssetPathUtility.JoinFilePath(RootDir, IndexFileName);

    public string GetBackendDir(string backend) => FYAssetPathUtility.JoinFilePath(RootDir, backend ?? string.Empty);

    /// <summary>正式摘要路径；构建标识不是安全单段名时返回 null。</summary>
    public string GetSummaryPath(string backend, string summaryId)
    {
        if (!IsSafeSegment(summaryId))
            return null;
        return FYAssetPathUtility.JoinFilePath(GetBackendDir(backend), summaryId + ".json");
    }

    /// <summary>写入正式摘要（原子写）；同一构建标识已存在且内容不同时拒绝。</summary>
    public bool TryWriteSummary(CompleteBuildSummary summary, out string error)
    {
        error = string.Empty;
        if (summary == null || string.IsNullOrEmpty(summary.BuildId))
        {
            error = "摘要或构建标识为空。";
            return false;
        }

        string path = GetSummaryPath(summary.BackendId, summary.BuildId);
        if (string.IsNullOrEmpty(path))
        {
            error = $"构建标识不是安全单段名: '{summary.BuildId}'";
            return false;
        }

        string json = SerializationUtility.SerializeToJson(summary.ToDocument(), true);
        try
        {
            if (FileHelper.Exists(path))
            {
                string existing = FileHelper.ReadAllText(path);
                if (!string.Equals(existing, json, StringComparison.Ordinal))
                {
                    error = $"正式摘要不可变，拒绝覆盖: {path}";
                    return false;
                }

                return true;
            }

            FileHelper.EnsureDirectory(Path.GetDirectoryName(path));
            FileHelper.WriteAllTextAtomic(path, json);
            return true;
        }
        catch (Exception ex)
        {
            error = $"写入正式摘要失败: {path} — {ex.Message}";
            return false;
        }
    }

    /// <summary>读取单个正式摘要文档；缺失或无法解析时返回 false。</summary>
    public bool TryReadSummaryDocument(
        string backend,
        string summaryId,
        out CompleteBuildSummary.SummaryDocument document,
        out string error)
    {
        document = null;
        error = string.Empty;
        string path = GetSummaryPath(backend, summaryId);
        if (string.IsNullOrEmpty(path) || !FileHelper.Exists(path))
        {
            error = $"正式摘要不存在: {path}";
            return false;
        }

        try
        {
            document = SerializationUtility.ReadFromFile<CompleteBuildSummary.SummaryDocument>(path);
            return document != null;
        }
        catch (Exception ex)
        {
            error = $"正式摘要解析失败: {path} — {ex.Message}";
            return false;
        }
    }

    /// <summary>枚举全部后端目录内的正式摘要；无法解析的文件跳过，不阻断重建。</summary>
    public List<CompleteBuildSummary.SummaryDocument> ReadAllSummaries()
    {
        var documents = new List<CompleteBuildSummary.SummaryDocument>();
        for (int i = 0; i < Backends.Length; i++)
            documents.AddRange(ReadSummaries(Backends[i]));
        return documents;
    }

    /// <summary>枚举一个后端目录内的正式摘要。</summary>
    public List<CompleteBuildSummary.SummaryDocument> ReadSummaries(string backend)
    {
        var documents = new List<CompleteBuildSummary.SummaryDocument>();
        string dir = GetBackendDir(backend);
        if (!Directory.Exists(dir))
            return documents;

        foreach (string path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                CompleteBuildSummary.SummaryDocument document =
                    SerializationUtility.ReadFromFile<CompleteBuildSummary.SummaryDocument>(path);
                if (document != null && !string.IsNullOrEmpty(document.BuildId))
                    documents.Add(document);
            }
            catch (Exception)
            {
                // 单个损坏文件不阻断重建：它无法作为定位或复用事实。
            }
        }

        return documents;
    }

    /// <summary>读取索引；缺失或损坏时返回 false，由调用方执行重建。</summary>
    public bool TryReadIndex(out BuildSummaryIndex index, out string error)
    {
        index = null;
        error = string.Empty;
        if (!FileHelper.Exists(IndexPath))
        {
            error = $"Summary Index 不存在: {IndexPath}";
            return false;
        }

        try
        {
            index = SerializationUtility.ReadFromFile<BuildSummaryIndex>(IndexPath);
            if (index == null || index.SummarySchemaVersion != BuildSummaryIndex.CurrentSchemaVersion)
            {
                error = $"Summary Index 版本不兼容: {IndexPath}";
                index = null;
                return false;
            }

            index.Scopes ??= new List<BuildSummaryScope>();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Summary Index 解析失败: {IndexPath} — {ex.Message}";
            return false;
        }
    }

    /// <summary>原子写入索引；写入前确保目录存在。</summary>
    public bool TryWriteIndex(BuildSummaryIndex index, out string error)
    {
        error = string.Empty;
        if (index == null)
        {
            error = "Summary Index 为空。";
            return false;
        }

        try
        {
            FileHelper.EnsureDirectory(RootDir);
            FileHelper.WriteAllTextAtomic(IndexPath, SerializationUtility.SerializeToJson(index, true));
            return true;
        }
        catch (Exception ex)
        {
            error = $"写入 Summary Index 失败: {IndexPath} — {ex.Message}";
            return false;
        }
    }

    /// <summary>枚举所有正式摘要并重建索引；不写盘，由调用方决定提交。</summary>
    public BuildSummaryIndex RebuildIndex() => BuildSummaryIndex.Rebuild(ReadAllSummaries());

    /// <summary>
    /// 读取项目当前全局成功版本；索引缺失或损坏时从正式摘要重建。
    /// 只作为读取便利入口，重建结果不自动写盘。
    /// </summary>
    public string ReadCurrentVersionText()
    {
        if (TryReadIndex(out BuildSummaryIndex index, out _))
            return index.ProjectVersion?.CurrentSuccessfulVersion ?? string.Empty;

        return RebuildIndex().ProjectVersion?.CurrentSuccessfulVersion ?? string.Empty;
    }

    /// <summary>读取当前索引文件字节用于事务备份；不存在时返回 null。</summary>
    public byte[] ReadIndexBytesOrNull() =>
        FileHelper.Exists(IndexPath) ? FileHelper.ReadAllBytes(IndexPath) : null;

    /// <summary>把索引恢复为备份字节；null 表示事务开始前不存在该文件。</summary>
    public bool TryRestoreIndex(byte[] bytes, out string error)
    {
        error = string.Empty;
        try
        {
            if (bytes == null)
            {
                if (FileHelper.Exists(IndexPath))
                    FileHelper.TryDelete(IndexPath);
                return true;
            }

            FileHelper.EnsureDirectory(RootDir);
            FileHelper.WriteAllBytesAtomic(IndexPath, bytes);
            return true;
        }
        catch (Exception ex)
        {
            error = $"恢复 Summary Index 失败: {IndexPath} — {ex.Message}";
            return false;
        }
    }

    /// <summary>删除单个正式摘要（测试重置与显式清理入口用）。</summary>
    public bool TryDeleteSummary(string backend, string summaryId, out string error)
    {
        error = string.Empty;
        string path = GetSummaryPath(backend, summaryId);
        if (string.IsNullOrEmpty(path) || !FileHelper.Exists(path))
        {
            error = $"正式摘要不存在: {path}";
            return false;
        }

        try
        {
            FileHelper.TryDelete(path);
            return true;
        }
        catch (Exception ex)
        {
            error = $"删除正式摘要失败: {path} — {ex.Message}";
            return false;
        }
    }

    /// <summary>清空全部正式摘要与索引（测试重置的唯一入口，调用方必须显式确认）。</summary>
    public bool TryResetAll(out string error)
    {
        error = string.Empty;
        try
        {
            if (FileHelper.DirectoryExists(RootDir))
                FileHelper.TryDeleteDirectory(RootDir, true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"清空构建事实失败: {RootDir} — {ex.Message}";
            return false;
        }
    }

    private static bool IsSafeSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (value == "." || value == "..")
            return false;
        if (value.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return false;
        return value.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0
               && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
#endif

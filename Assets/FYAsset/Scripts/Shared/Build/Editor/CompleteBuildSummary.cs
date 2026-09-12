using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 构建规模统计：摘要里除文件清单之外的计数事实。
/// </summary>
[Serializable]
public sealed class BuildStatistics
{
    /// <summary>本次构建产出的资产条目数</summary>
    public int AssetCount;

    /// <summary>本次构建产出的内容文件条目数</summary>
    public int ContentCount;

    /// <summary>摘要记录的文件数（与 Files 数量一致）</summary>
    public int FileCount;

    /// <summary>摘要记录的文件总字节数</summary>
    public long TotalBytes;
}

/// <summary>
/// 单个内容的复用事实：内容身份、输入指纹、制品物理摘要与内容级依赖输出文件名。
/// </summary>
/// <remarks>
/// 该记录是 Summary 作为复用索引的最小事实单元：历史包目录只是字节来源，
/// 复用方按 ContentIdentity + InputFingerprint 定位条目，再用 FileName/FileHash/FileCRC/FileSize 校验制品。
/// 字段全部落在 Unity 序列化范围内。
/// </remarks>
[Serializable]
public sealed class SummaryContentFact
{
    /// <summary>内容逻辑名（与 CollectedAssetInfo.ContentName 同一套规则）</summary>
    public string ContentIdentity;

    /// <summary>该内容的构建输入指纹；为空表示该内容不参与复用</summary>
    public string InputFingerprint;

    /// <summary>制品物理文件名（包内 bundles/ 下的名称）</summary>
    public string FileName;

    /// <summary>制品内容 MD5</summary>
    public string FileHash;

    /// <summary>制品内容 CRC32</summary>
    public uint FileCRC;

    /// <summary>制品字节数</summary>
    public long FileSize;

    /// <summary>该内容在 Unity 侧的内容级直接依赖输出文件名；复用时必须原样回放。null 表示该记录缺少依赖事实，不得复用。</summary>
    public List<string> DependencyFileNames = new();
}

/// <summary>
/// 一次完整构建的事实摘要：构建身份、模式、版本、平台、耗时、成功状态、文件摘要、消息与统计。
/// 它是构建结果面板的唯一数据源；详细 Task 日志仍由 Runner 的逐个 Task 结果独立承载。
/// </summary>
/// <remarks>
/// 摘要只描述一次构建的结果，不拥有生命周期，也不被缓存或发布流程引用；
/// 构建缓存、发布缓存和摘要三者复用 FileHelper.FileDigest，但文件与生命周期严格分离。
/// 落盘时通过 <see cref="ToDocument"/> 转成 Unity 可序列化的 DTO：
/// DateTime/TimeSpan/readonly struct 不在 Unity 序列化范围内，直接写字段会静默丢数据。
/// </remarks>
public sealed class CompleteBuildSummary
{
    /// <summary>正式摘要文档格式版本；索引与面板据此判断兼容性。</summary>
    public const int CurrentSchemaVersion = 1;

    public int SummarySchemaVersion = CurrentSchemaVersion;

    /// <summary>构建标识（当前为包名，与 BuildPackageRequest.PackageName 一致）</summary>
    public string BuildId;

    /// <summary>制品相对路径（项目根下）；只描述位置，不复制内容。删除包目录后该路径失效，但摘要仍保留。</summary>
    public string ArtifactRelativePath;

    /// <summary>Hotfix 的基准 Full 摘要标识；Full/Standalone 为空。</summary>
    public string BaseFullSummaryId;

    /// <summary>构建配方指纹；用于判断历史制品是否可复用。</summary>
    public string BuildRecipeFingerprint;

    /// <summary>采集事实指纹；用于判断历史制品是否可复用。</summary>
    public string CollectionFingerprint;

    /// <summary>后端标识（AA/AB）</summary>
    public string BackendId;

    /// <summary>构建类型</summary>
    public BuildType BuildType;

    /// <summary>构建侧运行模式。枚举定义在 Shared/Runtime/RuntimeMode.cs，与 BuildIndex 共用同一取值。</summary>
    public RuntimeMode RuntimeMode;

    /// <summary>版本号</summary>
    public VersionNumber Version;

    /// <summary>目标平台</summary>
    public string Platform;

    /// <summary>构建开始时间（UTC）</summary>
    public DateTime StartedAt;

    /// <summary>构建结束时间（UTC）；交付成功前不写入正式摘要。</summary>
    public DateTime FinishedAtUtc;

    /// <summary>构建耗时</summary>
    public TimeSpan Duration;

    /// <summary>整体是否成功</summary>
    public bool Success;

    /// <summary>本次构建产出的文件摘要</summary>
    public List<FileHelper.FileDigest> Files = new();

    /// <summary>本次构建产出的内容复用事实；按制品逐条记录，供后续构建按内容身份与输入指纹复用历史包</summary>
    public List<SummaryContentFact> Contents = new();

    /// <summary>构建消息（错误与警告）</summary>
    public List<BuildMessage> Messages = new();

    /// <summary>构建规模统计</summary>
    public BuildStatistics Statistics = new();

    /// <summary>按构建类型推导运行模式：Standalone 为离线包，其余为联网包。构建导出与摘要共用这一映射。</summary>
    public static RuntimeMode ResolveRuntimeMode(BuildType buildType)
    {
        return buildType == BuildType.Standalone ? RuntimeMode.Standalone : RuntimeMode.Online;
    }

    /// <summary>追加一条文件摘要并同步统计计数与总字节数。</summary>
    public void AddFile(in FileHelper.FileDigest digest)
    {
        if (!digest.IsComplete)
            return;

        Files.Add(digest);
        Statistics.FileCount = Files.Count;
        Statistics.TotalBytes += digest.Size;
    }

    /// <summary>追加一条构建消息。</summary>
    public void AddMessage(BuildMessage message)
    {
        if (message != null)
            Messages.Add(message);
    }

    /// <summary>转换成 Unity 可序列化文档，用于把摘要写入包目录。</summary>
    public SummaryDocument ToDocument()
    {
        var document = new SummaryDocument
        {
            SummarySchemaVersion = SummarySchemaVersion,
            BuildId = BuildId ?? string.Empty,
            ArtifactRelativePath = ArtifactRelativePath ?? string.Empty,
            BaseFullSummaryId = BaseFullSummaryId ?? string.Empty,
            BuildRecipeFingerprint = BuildRecipeFingerprint ?? string.Empty,
            CollectionFingerprint = CollectionFingerprint ?? string.Empty,
            BackendId = BackendId ?? string.Empty,
            BuildType = BuildType.ToString(),
            RuntimeMode = RuntimeMode.ToString(),
            Version = Version.GetReleaseVersionString(),
            Platform = Platform ?? string.Empty,
            StartedAtUtc = StartedAt.ToString("o"),
            FinishedAtUtc = FinishedAtUtc.ToString("o"),
            DurationSeconds = Duration.TotalSeconds,
            Success = Success,
            Statistics = Statistics,
            Files = new List<SummaryFile>(Files.Count),
            Contents = new List<SummaryContentFact>(Contents.Count),
            Messages = new List<SummaryMessage>(Messages.Count)
        };

        for (int i = 0; i < Contents.Count; i++)
        {
            SummaryContentFact content = Contents[i];
            if (content == null)
                continue;

            document.Contents.Add(new SummaryContentFact
            {
                ContentIdentity = content.ContentIdentity ?? string.Empty,
                InputFingerprint = content.InputFingerprint ?? string.Empty,
                FileName = content.FileName ?? string.Empty,
                FileHash = content.FileHash ?? string.Empty,
                FileCRC = content.FileCRC,
                FileSize = content.FileSize,
                // null 与空集合语义不同：前者表示该记录缺少依赖事实（不得复用），后者表示叶子内容。
                DependencyFileNames = content.DependencyFileNames != null
                    ? new List<string>(content.DependencyFileNames)
                    : null
            });
        }

        for (int i = 0; i < Files.Count; i++)
        {
            FileHelper.FileDigest file = Files[i];
            document.Files.Add(new SummaryFile
            {
                Name = file.Name ?? string.Empty,
                Hash = file.Hash ?? string.Empty,
                CRC = file.CRC,
                Size = file.Size
            });
        }

        for (int i = 0; i < Messages.Count; i++)
        {
            BuildMessage message = Messages[i];
            if (message == null)
                continue;

            document.Messages.Add(new SummaryMessage
            {
                Severity = message.Severity.ToString(),
                Code = message.Code ?? string.Empty,
                Message = message.Message ?? string.Empty,
                Source = message.Source ?? string.Empty
            });
        }

        return document;
    }

    /// <summary>摘要的可读文本形式，用于构建日志与人工排查。</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"BuildId: {BuildId}");
        builder.AppendLine($"Backend: {BackendId}");
        builder.AppendLine($"BuildType: {BuildType} ({RuntimeMode})");
        builder.AppendLine($"Version: {Version.GetReleaseVersionString()}");
        builder.AppendLine($"Platform: {Platform}");
        builder.AppendLine($"StartedAtUtc: {StartedAt:o}");
        builder.AppendLine($"Duration: {Duration.TotalSeconds:F2}s");
        builder.AppendLine($"Success: {Success}");
        builder.AppendLine($"Assets: {Statistics.AssetCount}");
        builder.AppendLine($"Contents: {Statistics.ContentCount}");
        builder.AppendLine($"Files: {Statistics.FileCount}");
        builder.AppendLine($"TotalBytes: {Statistics.TotalBytes}");
        for (int i = 0; i < Messages.Count; i++)
        {
            BuildMessage message = Messages[i];
            if (message != null)
                builder.AppendLine($"[{message.Severity}] {message.Code} {message.Message} ({message.Source})");
        }

        return builder.ToString();
    }

    /// <summary>落盘用文档：字段类型全部落在 Unity 序列化范围内。</summary>
    [Serializable]
    public sealed class SummaryDocument
    {
        public int SummarySchemaVersion;
        public string BuildId;
        public string ArtifactRelativePath;
        public string BaseFullSummaryId;
        public string BuildRecipeFingerprint;
        public string CollectionFingerprint;
        public string BackendId;
        public string BuildType;
        public string RuntimeMode;
        public string Version;
        public string Platform;
        public string StartedAtUtc;
        public string FinishedAtUtc;
        public double DurationSeconds;
        public bool Success;
        public BuildStatistics Statistics;
        public List<SummaryFile> Files;

        /// <summary>内容复用事实；缺失或为空只损失复用优化，不影响构建</summary>
        public List<SummaryContentFact> Contents;

        public List<SummaryMessage> Messages;
    }

    [Serializable]
    public sealed class SummaryFile
    {
        public string Name;
        public string Hash;
        public uint CRC;
        public long Size;
    }

    [Serializable]
    public sealed class SummaryMessage
    {
        public string Severity;
        public string Code;
        public string Message;
        public string Source;
    }
}

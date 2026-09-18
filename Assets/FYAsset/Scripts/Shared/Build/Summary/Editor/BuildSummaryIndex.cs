#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Summary Index 的作用域记录：按后端、平台与目标通道隔离定位成功构建事实。
/// </summary>
[Serializable]
public sealed class BuildSummaryScope
{
    public string Backend;
    public string Platform;
    public string Channel;
    public string LatestSuccessfulSummaryId;
    public string LatestFullSummaryId;
}

/// <summary>项目全局版本事实：所有后端与平台共用同一个成功版本序列。</summary>
[Serializable]
public sealed class BuildSummaryProjectVersion
{
    public string CurrentSuccessfulVersion;
}

/// <summary>
/// 构建事实索引（BuildData/Summaries/index.json）：只记录定位引用与项目版本，不复制 Manifest 或文件内容。
/// </summary>
/// <remarks>
/// 索引是可重建的定位加速：缺失或损坏时由 BuildSummaryStore 枚举正式 Summary 重建。
/// 删除历史包或制品缺失只影响制品状态，不让项目版本倒退。
/// </remarks>
[Serializable]
public sealed class BuildSummaryIndex
{
    public const int CurrentSchemaVersion = 1;

    private const char ScopeKeySeparator = '\u001F';

    public int SummarySchemaVersion = CurrentSchemaVersion;
    public BuildSummaryProjectVersion ProjectVersion = new();
    public List<BuildSummaryScope> Scopes = new();

    /// <summary>按后端、平台与通道查找作用域；不存在时返回 null。</summary>
    public BuildSummaryScope FindScope(string backend, string platform, string channel)
    {
        if (Scopes == null)
            return null;

        for (int i = 0; i < Scopes.Count; i++)
        {
            BuildSummaryScope scope = Scopes[i];
            if (scope == null)
                continue;
            if (!string.Equals(scope.Backend, backend, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(scope.Platform, platform, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(scope.Channel ?? string.Empty, channel ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                continue;
            return scope;
        }

        return null;
    }

    /// <summary>取得或创建作用域记录；已存在时返回原记录。</summary>
    public BuildSummaryScope GetOrAddScope(string backend, string platform, string channel)
    {
        BuildSummaryScope existing = FindScope(backend, platform, channel);
        if (existing != null)
            return existing;

        var scope = new BuildSummaryScope
        {
            Backend = backend ?? string.Empty,
            Platform = platform ?? string.Empty,
            Channel = channel ?? string.Empty
        };
        Scopes ??= new List<BuildSummaryScope>();
        Scopes.Add(scope);
        return scope;
    }

    /// <summary>
    /// 从正式 Summary 文档集合重建索引：全局版本取最高成功版本，
    /// 作用域取开始时间最新的成功事实，LatestFullSummaryId 只指向 BuildType.Full。
    /// </summary>
    public static BuildSummaryIndex Rebuild(IEnumerable<CompleteBuildSummary.SummaryDocument> documents)
    {
        var index = new BuildSummaryIndex();
        if (documents == null)
            return index;

        var latestByScope = new Dictionary<string, (CompleteBuildSummary.SummaryDocument Document, DateTime Time)>(
            StringComparer.OrdinalIgnoreCase);
        var latestFullByScope = new Dictionary<string, (CompleteBuildSummary.SummaryDocument Document, DateTime Time)>(
            StringComparer.OrdinalIgnoreCase);
        CompleteBuildSummary.SummaryDocument highestDocument = null;
        VersionNumber highestVersion = default;
        bool hasHighest = false;

        foreach (CompleteBuildSummary.SummaryDocument document in documents)
        {
            if (document == null || !document.Success || string.IsNullOrEmpty(document.BuildId))
                continue;
            if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
                continue;

            string key = ScopeKey(document.BackendId, document.Platform, version.Channel);
            DateTime time = ParseUtc(document.StartedAtUtc);

            if (!latestByScope.TryGetValue(key, out var current) || time >= current.Time)
                latestByScope[key] = (document, time);

            if (string.Equals(document.BuildType, BuildType.Full.ToString(), StringComparison.OrdinalIgnoreCase)
                && (!latestFullByScope.TryGetValue(key, out var currentFull) || time >= currentFull.Time))
                latestFullByScope[key] = (document, time);

            if (!hasHighest || version > highestVersion)
            {
                highestVersion = version;
                highestDocument = document;
                hasHighest = true;
            }
        }

        foreach (KeyValuePair<string, (CompleteBuildSummary.SummaryDocument Document, DateTime Time)> pair in latestByScope)
        {
            SplitScopeKey(pair.Key, out string backend, out string platform, out string channel);
            BuildSummaryScope scope = index.GetOrAddScope(backend, platform, channel);
            scope.LatestSuccessfulSummaryId = pair.Value.Document.BuildId;
            scope.LatestFullSummaryId = latestFullByScope.TryGetValue(pair.Key, out var full)
                ? full.Document.BuildId
                : string.Empty;
        }

        if (highestDocument != null)
        {
            index.ProjectVersion.CurrentSuccessfulVersion = highestDocument.Version;
        }

        return index;
    }

    private static string ScopeKey(string backend, string platform, string channel) =>
        string.Concat(backend ?? string.Empty, ScopeKeySeparator, platform ?? string.Empty, ScopeKeySeparator,
            channel ?? string.Empty);

    private static void SplitScopeKey(string key, out string backend, out string platform, out string channel)
    {
        string[] parts = (key ?? string.Empty).Split(ScopeKeySeparator);
        backend = parts.Length > 0 ? parts[0] : string.Empty;
        platform = parts.Length > 1 ? parts[1] : string.Empty;
        channel = parts.Length > 2 ? parts[2] : string.Empty;
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out DateTime parsed)
            ? parsed
            : DateTime.MinValue;

    private static string ParseDatePart(string value)
    {
        DateTime parsed = ParseUtc(value);
        return parsed == DateTime.MinValue ? string.Empty : parsed.ToString("yyyy-MM-dd");
    }
}
#endif

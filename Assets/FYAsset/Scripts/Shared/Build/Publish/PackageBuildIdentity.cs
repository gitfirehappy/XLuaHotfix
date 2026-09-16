using System;
using System.Globalization;

/// <summary>
/// 待发布包的身份事实：包名、版本与后端标识，用于生成服务器 PackageIndex。
/// </summary>
/// <remarks>
/// 事实来源是 BuildData/Summaries 内的正式构建摘要：包目录只含发布内容，
/// 因此身份由发布 UI 选择 Summary 后注入，不能从包目录推断。
/// </remarks>
public sealed class PackageBuildIdentity
{
    /// <summary>包名（服务器目录名与 PackageIndex.LatestPackage）</summary>
    public string PackageName;

    /// <summary>包版本（PackageIndex.LatestVersion）</summary>
    public VersionNumber Version;

    /// <summary>后端标识（PackageIndex.BackendMode）</summary>
    public string BackendId;

    /// <summary>构建类型文本（Full / Hotfix / Standalone），仅用于日志与诊断</summary>
    public string BuildType;

    /// <summary>后端标识是否与本次发布的后端一致；不一致说明拿错了包目录。</summary>
    public bool MatchesBackend(string backendKey)
    {
        return !string.IsNullOrEmpty(backendKey)
               && string.Equals(BackendId, backendKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>生产包名前缀与时间戳格式；与 BuildRequest 的命名契约一致。</summary>
    public const string PackageNamePrefix = "Build_";
    public const string PackageTimestampFormat = "yyyyMMddHHmmss";

    /// <summary>包名是否是合法的目录名单段：拒绝点段、Windows 设备名与保留字符。</summary>
    public bool IsSafePackageName() => IsSafeSegment(PackageName);

    /// <summary>单段目录名校验；包名与包集合名共用同一规则。</summary>
    public static bool IsSafeSegment(string value) => PublishPathGuard.IsSafeSegment(value);

    /// <summary>
    /// 严格解析生产包名 Build_{yyyyMMddHHmmss}_{VersionNumber}；任一段不可解析即拒绝。
    /// </summary>
    public static bool TryParsePackageName(
        string packageName,
        out DateTime timestampUtc,
        out VersionNumber version,
        out string error)
    {
        timestampUtc = default;
        version = default;
        error = string.Empty;

        const int timestampLength = 14;
        if (!IsSafeSegment(packageName))
        {
            error = $"包名不是合法单段目录名: '{packageName}'";
            return false;
        }

        if (!packageName.StartsWith(PackageNamePrefix, StringComparison.Ordinal)
            || packageName.Length <= PackageNamePrefix.Length + timestampLength + 1
            || packageName[PackageNamePrefix.Length + timestampLength] != '_')
        {
            error = $"包名必须满足 Build_{{yyyyMMddHHmmss}}_{{version}}: '{packageName}'";
            return false;
        }

        string timestamp = packageName.Substring(PackageNamePrefix.Length, timestampLength);
        if (!DateTime.TryParseExact(timestamp, PackageTimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestampUtc))
        {
            error = $"包名时间戳无法严格解析: '{timestamp}'";
            return false;
        }

        string versionText = packageName.Substring(PackageNamePrefix.Length + timestampLength + 1);
        if (!VersionNumber.TryParse(versionText, out version)
            || !string.Equals(version.GetReleaseVersionString(), versionText, StringComparison.Ordinal))
        {
            error = $"包名版本无法严格解析: '{versionText}'";
            return false;
        }

        return true;
    }
}

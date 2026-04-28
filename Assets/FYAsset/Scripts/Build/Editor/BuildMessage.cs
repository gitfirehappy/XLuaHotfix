/// <summary>
/// 构建时消息严重级别。
/// </summary>
public enum BuildSeverity
{
    /// <summary>警告 —— 不影响构建继续，但需要关注</summary>
    Warning = 0,

    /// <summary>错误 —— 阻止构建继续</summary>
    Error = 1
}

/// <summary>
/// 构建时错误码常量 —— 集中管理所有构建管线消息代码。
/// </summary>
public static class BuildErrorCodes
{
    public const string SettingNull = "SETTING_NULL";
    public const string NoPackages = "NO_PACKAGES";
    public const string EmptyPackage = "EMPTY_PACKAGE";
    public const string EmptyPackageName = "EMPTY_PACKAGE_NAME";
    public const string DuplicatePackageName = "DUPLICATE_PACKAGE_NAME";
    public const string EmptyGroupName = "EMPTY_GROUP_NAME";
    public const string DuplicateGroupName = "DUPLICATE_GROUP_NAME";
    public const string EmptyCollectPath = "EMPTY_COLLECT_PATH";
    public const string PathNotFound = "PATH_NOT_FOUND";
    public const string CrossPackageOverlap = "CROSS_PACKAGE_OVERLAP";
    public const string SamePathConflict = "SAME_PATH_CONFLICT";
    public const string RuleNotFound = "RULE_NOT_FOUND";
    public const string EmptyCollector = "EMPTY_COLLECTOR";
    public const string DuplicateGuid = "DUPLICATE_GUID";
}

/// <summary>
/// 构建时诊断消息 —— 替代旧的 ScanMessage。
/// 通过静态工厂方法构造，禁止裸 new。
/// </summary>
public class BuildMessage
{
    /// <summary>消息严重级别</summary>
    public readonly BuildSeverity Severity;

    /// <summary>消息代码，见 BuildErrorCodes</summary>
    public readonly string Code;

    /// <summary>人类可读的描述信息</summary>
    public readonly string Message;

    /// <summary>触发源路径，如 "Package[0].Group[1].Collector[2]" 或 Collector 路径</summary>
    public readonly string Source;

    private BuildMessage(BuildSeverity severity, string code, string message, string source)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Source = source ?? string.Empty;
    }

    #region Factory Methods

    public static BuildMessage Error(string code, string message, string source)
    {
        return new BuildMessage(BuildSeverity.Error, code, message, source);
    }

    public static BuildMessage Warning(string code, string message, string source)
    {
        return new BuildMessage(BuildSeverity.Warning, code, message, source);
    }

    #endregion
}

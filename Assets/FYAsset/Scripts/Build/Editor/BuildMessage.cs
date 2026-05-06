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
    /// <summary>CollectorSetting 为 null</summary>
    public const string SettingNull = "SETTING_NULL";

    /// <summary>CollectorSetting 未配置任何 Package</summary>
    public const string NoPackages = "NO_PACKAGES";

    /// <summary>Package 内未配置任何 Group</summary>
    public const string EmptyPackage = "EMPTY_PACKAGE";

    /// <summary>PackageName 为空字符串（保存时校验）</summary>
    public const string EmptyPackageName = "EMPTY_PACKAGE_NAME";

    /// <summary>PackageName 在同一个 Setting 中重复（保存时校验）</summary>
    public const string DuplicatePackageName = "DUPLICATE_PACKAGE_NAME";

    /// <summary>GroupName 为空字符串（保存时校验）</summary>
    public const string EmptyGroupName = "EMPTY_GROUP_NAME";

    /// <summary>GroupName 在同一 Package 内重复（保存时校验）</summary>
    public const string DuplicateGroupName = "DUPLICATE_GROUP_NAME";

    /// <summary>CollectPath 为空字符串（扫描/保存时校验）</summary>
    public const string EmptyCollectPath = "EMPTY_COLLECT_PATH";

    /// <summary>CollectPath 所指向的目录在磁盘上不存在（Warning，不阻止继续扫描）</summary>
    public const string PathNotFound = "PATH_NOT_FOUND";

    /// <summary>不同 Package 的 Collector 的 CollectPath 存在包含/重叠关系</summary>
    public const string CrossPackageOverlap = "CROSS_PACKAGE_OVERLAP";

    /// <summary>同一 Package 内两个 Collector 的 CollectPath 相同且深度相同</summary>
    public const string SamePathConflict = "SAME_PATH_CONFLICT";

    /// <summary>Rule 类名无法通过反射解析为实例</summary>
    public const string RuleNotFound = "RULE_NOT_FOUND";

    /// <summary>Collector 扫描后采集到零个资源（Warning，可能是配置错误）</summary>
    public const string EmptyCollector = "EMPTY_COLLECTOR";

    /// <summary>Package 内出现重复的 Asset GUID（内部逻辑错误）</summary>
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

    /// <summary>创建 Error 级别的构建消息</summary>
    /// <param name="code">错误码，使用 BuildErrorCodes 常量</param>
    /// <param name="message">人类可读的描述信息</param>
    /// <param name="source">触发源路径，如 "Package[0].Group[1].Collector[2]"</param>
    public static BuildMessage Error(string code, string message, string source)
    {
        return new BuildMessage(BuildSeverity.Error, code, message, source);
    }

    /// <summary>创建 Warning 级别的构建消息</summary>
    /// <param name="code">错误码，使用 BuildErrorCodes 常量</param>
    /// <param name="message">人类可读的描述信息</param>
    /// <param name="source">触发源路径，如 "Package[0].Group[1].Collector[2]"</param>
    public static BuildMessage Warning(string code, string message, string source)
    {
        return new BuildMessage(BuildSeverity.Warning, code, message, source);
    }

    #endregion

    #region Semantic Factory Methods

    public static BuildMessage SettingNull(string source)
        => Error(BuildErrorCodes.SettingNull, "CollectorSetting is null.", source);

    public static BuildMessage NoPackages(string source)
        => Error(BuildErrorCodes.NoPackages, "CollectorSetting has no Packages configured.", source);

    public static BuildMessage EmptyPackage(string packageName, string source)
        => Warning(BuildErrorCodes.EmptyPackage,
            string.Concat("Package '", packageName, "' has no Groups."), source);

    public static BuildMessage EmptyPackageName(string source)
        => Error(BuildErrorCodes.EmptyPackageName, "Package name is empty.", source);

    public static BuildMessage DuplicatePackageName(string packageName, string source)
        => Error(BuildErrorCodes.DuplicatePackageName,
            string.Concat("Duplicate PackageName: ", packageName), source);

    public static BuildMessage EmptyGroupName(string source)
        => Error(BuildErrorCodes.EmptyGroupName, "Group name is empty.", source);

    public static BuildMessage DuplicateGroupName(string groupName, string packageName, string source)
        => Warning(BuildErrorCodes.DuplicateGroupName,
            string.Concat("Duplicate GroupName '", groupName, "' in Package '", packageName, "'."), source);

    public static BuildMessage EmptyCollectPath(string source)
        => Error(BuildErrorCodes.EmptyCollectPath, "CollectPath is empty.", source);

    public static BuildMessage PathNotFound(string path, string source)
        => Warning(BuildErrorCodes.PathNotFound,
            string.Concat("CollectPath not found: ", path), source);

    public static BuildMessage CrossPackageOverlap(string path, string pkg1, string pkg2, string source)
        => Error(BuildErrorCodes.CrossPackageOverlap,
            string.Concat("Path '", path, "' is used in both Package '", pkg1, "' and '", pkg2, "'."), source);

    public static BuildMessage CrossPackageContainment(string parentPath, string parentPkg, string childPath, string childPkg, string source)
        => Error(BuildErrorCodes.CrossPackageOverlap,
            string.Concat("Path '", parentPath, "' (", parentPkg, ") contains '", childPath, "' (", childPkg, ")."), source);

    public static BuildMessage SamePathConflict(string path, string source)
        => Error(BuildErrorCodes.SamePathConflict,
            string.Concat("Same CollectPath used in two Collectors: ", path), source);

    public static BuildMessage RuleNotFound(string ruleClassName, string source)
        => Error(BuildErrorCodes.RuleNotFound,
            string.Concat("Rule class '", ruleClassName, "' cannot be resolved."), source);

    public static BuildMessage EmptyCollector(string collectorPath, string source)
        => Warning(BuildErrorCodes.EmptyCollector,
            string.Concat("Collector collected zero assets: ", collectorPath), source);

    public static BuildMessage DuplicateGuid(string guid, string source)
        => Error(BuildErrorCodes.DuplicateGuid,
            string.Concat("Duplicate Asset GUID: ", guid), source);

    #endregion
}

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
    /// <summary>AssetCollectionSetting 为 null</summary>
    public const string SettingNull = "SETTING_NULL";

    /// <summary>AssetCollectionSetting 未配置任何 Package</summary>
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

    /// <summary>Rule 类名为空</summary>
    public const string EmptyRuleName = "EMPTY_RULE_NAME";

    /// <summary>Rule 执行过程中抛出异常</summary>
    public const string RuleExecutionFailed = "RULE_EXECUTION_FAILED";

    /// <summary>CollectorType 不能作为用户配置使用</summary>
    public const string InvalidCollectorType = "INVALID_COLLECTOR_TYPE";

    /// <summary>Bundle 命名片段包含非法字符</summary>
    public const string InvalidBundleNameSegment = "INVALID_BUNDLE_NAME_SEGMENT";

    /// <summary>资源不能作为 AssetBundle 入口资产</summary>
    public const string InvalidBundleEntryAsset = "INVALID_BUNDLE_ENTRY_ASSET";

    /// <summary>Label 包含非法字符</summary>
    public const string InvalidLabel = "INVALID_LABEL";

    /// <summary>Collector 扫描后采集到零个资源（Warning，可能是配置错误）</summary>
    public const string EmptyCollector = "EMPTY_COLLECTOR";

    /// <summary>Package 内出现重复的 Asset GUID（内部逻辑错误）</summary>
    public const string DuplicateGuid = "DUPLICATE_GUID";

    /// <summary>构建后端类型无效</summary>
    public const string InvalidBackend = "INVALID_BACKEND";

    /// <summary>Collector 采集阶段失败</summary>
    public const string CollectAssetsFailed = "COLLECT_ASSETS_FAILED";

    /// <summary>目标平台不支持或未识别</summary>
    public const string InvalidPlatform = "INVALID_PLATFORM";

    /// <summary>AssetBundle 构建过程失败</summary>
    public const string BuildFailed = "BUILD_FAILED";

    /// <summary>RawFile 与 Serialized/Scene 输出路线落入同一个逻辑 Bundle</summary>
    public const string RawfilePayloadConflict = "RAWFILE_PAYLOAD_CONFLICT";

    /// <summary>同一个 Bundle 构建组混入多种 PayloadKind</summary>
    public const string MixedPayloadBundle = "MIXED_PAYLOAD_BUNDLE";

    /// <summary>同一个 Bundle 构建组混入多种 PrimaryType</summary>
    public const string MixedPrimaryTypeBundle = "MIXED_PRIMARY_TYPE_BUNDLE";

    /// <summary>RawFile 模式下单个文件产生多个 Asset（内部逻辑错误）</summary>
    public const string RawfileMultiAsset = "RAWFILE_MULTI_ASSET";

    /// <summary>RawFile 拷贝到输出目录失败</summary>
    public const string RawfileCopyFailed = "RAWFILE_COPY_FAILED";

    /// <summary>Bundle 文件在构建输出中未找到</summary>
    public const string BundleFileNotFound = "BUNDLE_FILE_NOT_FOUND";

    /// <summary>构建侧 Manifest 中未找到对应 Bundle 条目</summary>
    public const string BundleNotFoundBuild = "BUNDLE_NOT_FOUND_BUILD";

    /// <summary>构建结果中同一个资产与逻辑 Bundle 的实际归属重复</summary>
    public const string DuplicateManifestMembership = "DUPLICATE_MANIFEST_MEMBERSHIP";

    /// <summary>CollectedAssetInfo 无法在实际 BundleBuildInfo.AssetPaths 中找到归属</summary>
    public const string ManifestMembershipMissing = "MANIFEST_MEMBERSHIP_MISSING";

    /// <summary>CollectedAssetInfo 与实际 BundleBuildInfo 的 PayloadKind 不一致</summary>
    public const string ManifestPayloadMismatch = "MANIFEST_PAYLOAD_MISMATCH";

    /// <summary>ABManifest 初始化失败（数据为空或格式错误）</summary>
    public const string ManifestInitFailed = "MANIFEST_INIT_FAILED";

    /// <summary>Bundle 构建结果中存在重复或冲突的 Bundle 身份</summary>
    public const string DuplicateBundleName = "DUPLICATE_BUNDLE_NAME";

    /// <summary>构建结果校验失败</summary>
    public const string VerificationFailed = "VERIFICATION_FAILED";

    /// <summary>依赖图中发现循环依赖</summary>
    public const string CycleDependency = "CYCLE_DEPENDENCY";

    /// <summary>循环依赖数量摘要</summary>
    public const string CycleCount = "CYCLE_COUNT";

    /// <summary>循环依赖日志被截断</summary>
    public const string CycleTruncated = "CYCLE_TRUNCATED";

    /// <summary>SharePolicy 规则存在冲突</summary>
    public const string SharePolicyConflict = "SHAREPOLICY_CONFLICT";

    /// <summary>SharePolicy 需要文件大小但无法读取</summary>
    public const string SharePolicySizeUnknown = "SHAREPOLICY_SIZE_UNKNOWN";

    /// <summary>管线配置中没有 Task</summary>
    public const string NoPipelineTasks = "NO_PIPELINE_TASKS";

    /// <summary>管线配置缺少必需主干 Task</summary>
    public const string MissingBackboneTask = "MISSING_BACKBONE_TASK";

    /// <summary>配置引用的 TaskName 未找到实现</summary>
    public const string TaskNotFound = "TASK_NOT_FOUND";

    /// <summary>Task 反射发现或实例化失败</summary>
    public const string TaskResolutionFailed = "TASK_RESOLUTION_FAILED";

    /// <summary>Task 依赖缺失或未启用</summary>
    public const string MissingDependency = "MISSING_DEPENDENCY";

    /// <summary>Task 读取的 BuildContext key 没有上游产出</summary>
    public const string UnsatisfiedReadKey = "UNSATISFIED_READ_KEY";

    /// <summary>Task 执行返回 null</summary>
    public const string NullTaskResult = "NULL_RESULT";

    /// <summary>Task 执行异常</summary>
    public const string TaskExecutionError = "TASK_EXECUTION_ERROR";

    /// <summary>采集结果为空，后续 Task 无法继续</summary>
    public const string NoCollectedAssets = "NO_COLLECTED_ASSETS";

    /// <summary>依赖分析阶段失败</summary>
    public const string DependencyAnalysisFailed = "DEPENDENCY_ANALYSIS_FAILED";
}

/// <summary>
/// 构建结果校验项代码。它们描述 verification issue，不等同于 Task 失败码。
/// </summary>
public static class BuildVerificationIssueCodes
{
    public const string FileExistence = "FILE_EXISTENCE";
    public const string FileIntegrity = "FILE_INTEGRITY";
    public const string HashReVerify = "HASH_RE_VERIFY";
    public const string SizeAnomaly = "SIZE_ANOMALY";
    public const string OrphanCheck = "ORPHAN_CHECK";
    public const string CountCrossCheck = "COUNT_CROSS_CHECK";
}

/// <summary>
/// 构建时诊断消息
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

    #region 语义化工厂方法

    public static BuildMessage SettingNull(string source)
        => Error(BuildErrorCodes.SettingNull, "AssetCollectionSetting is null.", source);

    public static BuildMessage NoPackages(string source)
        => Error(BuildErrorCodes.NoPackages, "AssetCollectionSetting has no Packages configured.", source);

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

    public static BuildMessage CrossPackageContainment(string parentPath, string parentPkg, string childPath,
        string childPkg, string source)
        => Error(BuildErrorCodes.CrossPackageOverlap,
            string.Concat("Path '", parentPath, "' (", parentPkg, ") contains '", childPath, "' (", childPkg, ")."),
            source);

    public static BuildMessage SamePathConflict(string path, string source)
        => Error(BuildErrorCodes.SamePathConflict,
            string.Concat("Same CollectPath used in two Collectors: ", path), source);

    public static BuildMessage RuleNotFound(string ruleClassName, string source)
        => Error(BuildErrorCodes.RuleNotFound,
            string.Concat("Rule class '", ruleClassName, "' cannot be resolved."), source);

    public static BuildMessage EmptyRuleName(string ruleType, string source)
        => Error(BuildErrorCodes.EmptyRuleName,
            string.Concat(ruleType, " class name is empty."), source);

    public static BuildMessage InvalidCollectorType(ECollectorType collectorType, string source)
        => Error(BuildErrorCodes.InvalidCollectorType,
            string.Concat("CollectorType '", collectorType.ToString(), "' is not valid for manual Collector configuration."), source);

    public static BuildMessage RuleExecutionFailed(string ruleType, string ruleClassName, string assetPath, string message, string source)
        => Error(BuildErrorCodes.RuleExecutionFailed,
            string.Concat(ruleType, " '", ruleClassName, "' failed for asset '", assetPath, "': ", message), source);

    public static BuildMessage InvalidBundleNameSegment(string message, string source)
        => Error(BuildErrorCodes.InvalidBundleNameSegment, message, source);

    public static BuildMessage UnsupportedBundleEntryAsset(string assetPath, string reason, string source)
        => Warning(BuildErrorCodes.InvalidBundleEntryAsset,
            string.Concat("Asset '", assetPath, "' cannot be used as an AssetBundle entry and was skipped: ", reason),
            source);

    public static BuildMessage InvalidLabel(string message, string source)
        => Error(BuildErrorCodes.InvalidLabel, message, source);

    public static BuildMessage EmptyCollector(string collectorPath, string source)
        => Warning(BuildErrorCodes.EmptyCollector,
            string.Concat("Collector collected zero assets: ", collectorPath), source);

    public static BuildMessage DuplicateGuid(string guid, string source)
        => Error(BuildErrorCodes.DuplicateGuid,
            string.Concat("Duplicate Asset GUID: ", guid), source);

    #endregion
}

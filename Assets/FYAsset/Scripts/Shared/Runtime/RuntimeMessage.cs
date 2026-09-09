using System;

/// <summary>
/// 运行时消息严重级别。
/// </summary>
public enum RuntimeSeverity
{
    /// <summary>警告 —— 操作成功但存在值得关注的异常</summary>
    Warning = 0,

    /// <summary>错误 —— 操作失败</summary>
    Error = 1
}

/// <summary>
/// 运行时错误码常量 —— 集中管理所有运行时消息代码。
/// </summary>
public static class RuntimeErrorCodes
{
    /// <summary>未找到匹配查询条件的条目</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>多条条目匹配，无法消歧（需要 Labels）</summary>
    public const string AmbiguousMatch = "AMBIGUOUS_MATCH";

    /// <summary>找到条目但 PrimaryType 与请求类型不兼容</summary>
    public const string TypeMismatch = "TYPE_MISMATCH";

    /// <summary>Resolve 成功但底层加载操作失败</summary>
    public const string LoadFailed = "LOAD_FAILED";

    /// <summary>Bundle 文件在磁盘上不存在（热更目录 + StreamingAssets 均未找到）</summary>
    public const string BundleNotFound = "BUNDLE_NOT_FOUND";

    /// <summary>AssetBundle.LoadFromFile 返回 null（文件损坏、加密异常等）</summary>
    public const string BundleLoadFailed = "BUNDLE_LOAD_FAILED";

    /// <summary>依赖 Bundle 加载失败（级联失败）</summary>
    public const string DependencyFailed = "DEPENDENCY_FAILED";

    /// <summary>从 Bundle 中提取 Asset 失败（SourcePath 不正确或类型不匹配）</summary>
    public const string AssetExtractionFailed = "ASSET_EXTRACTION_FAILED";

    /// <summary>请求的加载 API 与资产 PayloadKind 不匹配</summary>
    public const string InvalidPayloadKind = "INVALID_PAYLOAD_KIND";

    /// <summary>当前后端或平台不支持该操作</summary>
    public const string UnsupportedOperation = "UNSUPPORTED_OPERATION";

    /// <summary>参数无效（null / 空字符串 / 越界等）</summary>
    public const string InvalidArgument = "INVALID_ARG";
}

/// <summary>
/// 运行时诊断消息。只能通过静态工厂方法构造。
/// </summary>
/// <remarks>
/// [Serializable] 用于支持 Unity 序列化（Inspector 调试面板 / 热重载异常跨域传递）。
/// 只读字段通过 private constructor 初始化，序列化系统通过反射写入。
/// </remarks>
[Serializable]
public class RuntimeMessage
{
    #region 字段

    public readonly RuntimeSeverity Severity;
    public readonly string Code;
    public readonly string Message;

    #endregion

    #region 构造（私有 — 只能通过工厂方法创建）

    private RuntimeMessage(
        RuntimeSeverity severity,
        string code,
        string message)
    {
        Severity = severity;
        Code = code;
        Message = message;
    }

    #endregion

    #region 通用工厂方法

    public static RuntimeMessage Error(string code, string message)
    {
        return new RuntimeMessage(RuntimeSeverity.Error, code, message);
    }

    public static RuntimeMessage Warning(string code, string message)
    {
        return new RuntimeMessage(RuntimeSeverity.Warning, code, message);
    }

    #endregion

    #region 语义化工厂方法

    public static RuntimeMessage NotFound(string query)
        => Error(RuntimeErrorCodes.NotFound, string.Concat("未找到匹配条目: ", query));

    public static RuntimeMessage Ambiguous(string query, int candidateCount)
        => Error(RuntimeErrorCodes.AmbiguousMatch,
            string.Concat("多条条目匹配: ", query, " (", candidateCount.ToString(), " 个候选)"));

    public static RuntimeMessage TypeMismatch(string query, string expectedType, string actualType)
        => Error(RuntimeErrorCodes.TypeMismatch,
            string.Concat("类型不匹配: ", query, ", 期望可赋值给 ", expectedType, ", 实际 ", actualType));

    public static RuntimeMessage LoadFailed(string entryId, string reason)
        => Error(RuntimeErrorCodes.LoadFailed,
            string.Concat("加载失败, EntryId=[", entryId, "]: ", reason));

    public static RuntimeMessage BundleNotFound(string bundleName)
        => Error(RuntimeErrorCodes.BundleNotFound,
            string.Concat("Bundle 文件未找到: ", bundleName, "（热更目录 + StreamingAssets 均不存在）"));

    public static RuntimeMessage BundleLoadFailed(string bundleName, string path)
        => Error(RuntimeErrorCodes.BundleLoadFailed,
            string.Concat("AssetBundle.LoadFromFile 失败: ", bundleName, ", 路径=", path));

    public static RuntimeMessage DependencyFailed(string bundleName, string depBundleName)
        => Error(RuntimeErrorCodes.DependencyFailed,
            string.Concat("依赖 Bundle 加载失败: ", depBundleName, " (被 ", bundleName, " 依赖)"));

    public static RuntimeMessage AssetExtractionFailed(string entryId, string sourcePath, string bundleName)
        => Error(RuntimeErrorCodes.AssetExtractionFailed,
            string.Concat("从 Bundle 提取 Asset 失败: SourcePath=", sourcePath,
                ", Bundle=", bundleName, ", EntryId=", entryId));

    public static RuntimeMessage InvalidPayloadKind(string entryId, string expected, string actual)
        => Error(RuntimeErrorCodes.InvalidPayloadKind,
            string.Concat("PayloadKind 不匹配, EntryId=[", entryId, "], 期望 ",
                expected, ", 实际 ", actual));

    public static RuntimeMessage UnsupportedOperation(string operation, string reason)
        => Error(RuntimeErrorCodes.UnsupportedOperation,
            string.Concat(operation, " 不支持: ", reason));

    #endregion

    #region 诊断

    public override string ToString() => string.Concat("[", Code, "] ", Message);

    #endregion
}

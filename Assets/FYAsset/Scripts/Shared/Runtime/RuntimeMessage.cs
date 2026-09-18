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

    /// <summary>Resolve 成功但底层加载操作失败</summary>
    public const string LoadFailed = "LOAD_FAILED";

    /// <summary>Bundle 文件在当前激活包根下不存在</summary>
    public const string BundleNotFound = "BUNDLE_NOT_FOUND";

    /// <summary>AssetBundle.LoadFromFile 返回 null（文件损坏、加密异常等）</summary>
    public const string BundleLoadFailed = "BUNDLE_LOAD_FAILED";

    /// <summary>依赖 Bundle 加载失败（级联失败）</summary>
    public const string DependencyFailed = "DEPENDENCY_FAILED";

    /// <summary>从 Bundle 中提取 Asset 失败（AssetPath 不正确或类型不匹配）</summary>
    public const string AssetExtractionFailed = "ASSET_EXTRACTION_FAILED";

    /// <summary>请求的加载 API 与资产内容类型（AssetContentType）不匹配</summary>
    public const string InvalidPayloadKind = "INVALID_PAYLOAD_KIND";

    /// <summary>当前后端或平台不支持该操作</summary>
    public const string UnsupportedOperation = "UNSUPPORTED_OPERATION";

    /// <summary>公共 Address 在同一个包内重复（大小写不敏感）</summary>
    public const string DuplicateAddress = "DUPLICATE_ADDRESS";

    /// <summary>关闭资源管理器时仍有活跃 Handle 未释放</summary>
    public const string ActiveHandlesRemain = "ACTIVE_HANDLES_REMAIN";

    /// <summary>Scene 加载、校验或卸载失败</summary>
    public const string SceneLoadFailed = "SCENE_LOAD_FAILED";

    /// <summary>同一 Entry 的异步加载仍在进行：同步调用不得阻塞或重复获取</summary>
    public const string LoadInProgress = "LOAD_IN_PROGRESS";

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
    public readonly RuntimeSeverity Severity;
    public readonly string Code;
    public readonly string Message;

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

    public static RuntimeMessage LoadFailed(string entryId, string reason)
        => Error(RuntimeErrorCodes.LoadFailed,
            string.Concat("加载失败, EntryId=[", entryId, "]: ", reason));

    public static RuntimeMessage BundleNotFound(string bundleName)
        => Error(RuntimeErrorCodes.BundleNotFound,
            string.Concat("当前激活包根下未找到内容文件: ", bundleName));

    public static RuntimeMessage BundleLoadFailed(string bundleName, string path)
        => Error(RuntimeErrorCodes.BundleLoadFailed,
            string.Concat("AssetBundle.LoadFromFile 失败: ", bundleName, ", 路径=", path));

    public static RuntimeMessage DependencyFailed(string bundleName, string depBundleName)
        => Error(RuntimeErrorCodes.DependencyFailed,
            string.Concat("依赖 Bundle 加载失败: ", depBundleName, " (被 ", bundleName, " 依赖)"));

    public static RuntimeMessage AssetExtractionFailed(string entryId, string sourcePath, string bundleName)
        => Error(RuntimeErrorCodes.AssetExtractionFailed,
            string.Concat("从 Bundle 提取 Asset 失败: AssetPath=", sourcePath,
                ", Bundle=", bundleName, ", EntryId=", entryId));

    public static RuntimeMessage InvalidPayloadKind(string entryId, string expected, string actual)
        => Error(RuntimeErrorCodes.InvalidPayloadKind,
            string.Concat("内容类型不匹配, EntryId=[", entryId, "], 期望 ",
                expected, ", 实际 ", actual));

    public static RuntimeMessage UnsupportedOperation(string operation, string reason)
        => Error(RuntimeErrorCodes.UnsupportedOperation,
            string.Concat(operation, " 不支持: ", reason));

    public static RuntimeMessage DuplicateAddress(string address, int candidateCount)
        => Error(RuntimeErrorCodes.DuplicateAddress,
            string.Concat("公共 Address 重复: ", address, "（", candidateCount.ToString(), " 个条目）"));

    public static RuntimeMessage ActiveHandlesBlockShutdown(int activeHandleCount, string detail)
        => Error(RuntimeErrorCodes.ActiveHandlesRemain,
            string.Concat("仍有 ", activeHandleCount.ToString(),
                " 个活跃 Handle 未释放，拒绝关闭资源管理器: ", detail));

    /// <summary>同一 Entry 的加载正在进行：调用方应稍后重试，不得阻塞异步请求。</summary>
    public static RuntimeMessage LoadInProgress(string entryId)
        => Error(RuntimeErrorCodes.LoadInProgress, string.Concat("条目正在异步加载中: ", entryId ?? string.Empty));

    public static RuntimeMessage SceneLoadFailed(string entryId, string reason)
        => Error(RuntimeErrorCodes.SceneLoadFailed,
            string.Concat("场景加载失败, EntryId=[", entryId ?? "", "]: ", reason));

    #endregion

    #region 诊断

    public override string ToString() => string.Concat("[", Code, "] ", Message);

    #endregion
}

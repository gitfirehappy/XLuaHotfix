using System;
using System.Collections.Generic;

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
    public const string NotFound = "NOT_FOUND";
    public const string AmbiguousMatch = "AMBIGUOUS_MATCH";
    public const string TypeMismatch = "TYPE_MISMATCH";
    public const string LoadFailed = "LOAD_FAILED";
    public const string IndexNotSupported = "INDEX_NOT_SUPPORTED";
    public const string BundleNotFound = "BUNDLE_NOT_FOUND";
    public const string BundleLoadFailed = "BUNDLE_LOAD_FAILED";
    public const string DependencyFailed = "DEPENDENCY_FAILED";
    public const string AssetExtractionFailed = "ASSET_EXTRACTION_FAILED";
}

/// <summary>
/// 运行时诊断消息 —— 替代旧的 AssetLoadError。
/// 通过静态工厂方法构造，禁止裸 new。
/// </summary>
[Serializable]
public class RuntimeMessage
{
    #region 字段

    public readonly RuntimeSeverity Severity;
    public readonly string Code;
    public readonly string Message;

    /// <summary>
    /// AmbiguousMatch 时的候选条目列表，帮助开发者添加 Labels 消歧。
    /// 其他错误类型时为 null 或空。
    /// </summary>
    public readonly IReadOnlyList<RuntimeAssetEntry> Candidates;

    #endregion

    #region 构造（私有 — 只能通过工厂方法创建）

    private RuntimeMessage(
        RuntimeSeverity severity,
        string code,
        string message,
        IReadOnlyList<RuntimeAssetEntry> candidates = null)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Candidates = candidates;
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

    #region 语义化工厂方法（保持与旧 AssetLoadError 同名 API，方便迁移）

    public static RuntimeMessage NotFound(string query)
        => Error(RuntimeErrorCodes.NotFound, string.Concat("未找到匹配条目: ", query));

    public static RuntimeMessage Ambiguous(string query, IReadOnlyList<RuntimeAssetEntry> candidates)
        => new RuntimeMessage(RuntimeSeverity.Error, RuntimeErrorCodes.AmbiguousMatch,
            string.Concat("多条条目匹配: ", query, " (", candidates.Count.ToString(), " 个候选)"), candidates);

    public static RuntimeMessage TypeMismatch(string query, string expectedType, string actualType)
        => Error(RuntimeErrorCodes.TypeMismatch,
            string.Concat("类型不匹配: ", query, ", 期望可赋值给 ", expectedType, ", 实际 ", actualType));

    public static RuntimeMessage LoadFailed(string entryId, string reason)
        => Error(RuntimeErrorCodes.LoadFailed,
            string.Concat("加载失败, EntryId=[", entryId, "]: ", reason));

    public static RuntimeMessage IndexNotSupported(string indexType)
        => Error(RuntimeErrorCodes.IndexNotSupported,
            string.Concat("索引 ", indexType, " 不支持条目级查询，请使用基于 RuntimeAssetEntry 的索引实现。"));

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

    #endregion

    #region 诊断

    public override string ToString() => string.Concat("[", Code, "] ", Message);

    #endregion
}

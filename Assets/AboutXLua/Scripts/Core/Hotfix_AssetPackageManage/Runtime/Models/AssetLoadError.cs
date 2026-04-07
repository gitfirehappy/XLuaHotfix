using System;
using System.Collections.Generic;

/// <summary>
/// 资源解析/加载失败的结构化错误模型。
/// ResolveResult 和 AssetHandle 使用此类传达失败原因。
/// </summary>
[Serializable]
public class AssetLoadError
{
    #region 错误码

    public enum Code
    {
        /// <summary> 无错误 </summary>
        None = 0,

        /// <summary> 未找到匹配查询条件的条目 </summary>
        NotFound,

        /// <summary> 多条条目匹配，无法消歧（需要 Labels） </summary>
        AmbiguousMatch,

        /// <summary> 找到条目但 PrimaryType 与请求类型不兼容 </summary>
        TypeMismatch,

        /// <summary> Resolve 成功但底层加载操作失败 </summary>
        LoadFailed,

        /// <summary> 索引不支持条目级查询（旧版 AddressableLabelsConfig） </summary>
        IndexNotSupported,

        /// <summary> Bundle 文件在磁盘上不存在（热更目录 + StreamingAssets 均未找到） </summary>
        BundleNotFound,

        /// <summary> AssetBundle.LoadFromFile 返回 null（文件损坏、加密异常等） </summary>
        BundleLoadFailed,

        /// <summary> 依赖 Bundle 加载失败（级联失败） </summary>
        DependencyFailed,

        /// <summary> 从 Bundle 中提取 Asset 失败（SourcePath 不正确或类型不匹配） </summary>
        AssetExtractionFailed,
    }

    #endregion

    #region 字段

    public Code ErrorCode;
    public string Message;

    /// <summary>
    /// AmbiguousMatch 时的候选条目列表，帮助开发者添加 Labels 消歧。
    /// 其他错误类型时为 null 或空。
    /// </summary>
    public IReadOnlyList<RuntimeAssetEntry> Candidates;

    #endregion

    #region 工厂方法

    public static AssetLoadError NotFound(string query)
        => new() { ErrorCode = Code.NotFound, Message = string.Concat("未找到匹配条目: ", query) };

    public static AssetLoadError Ambiguous(string query, IReadOnlyList<RuntimeAssetEntry> candidates)
        => new()
        {
            ErrorCode = Code.AmbiguousMatch,
            Message = string.Concat("多条条目匹配: ", query, " (", candidates.Count.ToString(), " 个候选)"),
            Candidates = candidates
        };

    public static AssetLoadError TypeMismatch(string query, string expectedType, string actualType)
        => new()
        {
            ErrorCode = Code.TypeMismatch,
            Message = string.Concat("类型不匹配: ", query, ", 期望可赋值给 ", expectedType, ", 实际 ", actualType)
        };

    public static AssetLoadError LoadFailed(string entryId, string reason)
        => new()
        {
            ErrorCode = Code.LoadFailed,
            Message = string.Concat("加载失败, EntryId=[", entryId, "]: ", reason)
        };

    public static AssetLoadError IndexNotSupported(string indexType)
        => new()
        {
            ErrorCode = Code.IndexNotSupported,
            Message = string.Concat("索引 ", indexType, " 不支持条目级查询，请使用基于 RuntimeAssetEntry 的索引实现。")
        };

    public static AssetLoadError BundleNotFound(string bundleName)
        => new()
        {
            ErrorCode = Code.BundleNotFound,
            Message = string.Concat("Bundle 文件未找到: ", bundleName, "（热更目录 + StreamingAssets 均不存在）")
        };

    public static AssetLoadError BundleLoadFailed(string bundleName, string path)
        => new()
        {
            ErrorCode = Code.BundleLoadFailed,
            Message = string.Concat("AssetBundle.LoadFromFile 失败: ", bundleName, ", 路径=", path)
        };

    public static AssetLoadError DependencyFailed(string bundleName, string depBundleName)
        => new()
        {
            ErrorCode = Code.DependencyFailed,
            Message = string.Concat("依赖 Bundle 加载失败: ", depBundleName, " (被 ", bundleName, " 依赖)")
        };

    public static AssetLoadError AssetExtractionFailed(string entryId, string sourcePath, string bundleName)
        => new()
        {
            ErrorCode = Code.AssetExtractionFailed,
            Message = string.Concat("从 Bundle 提取 Asset 失败: SourcePath=", sourcePath,
                ", Bundle=", bundleName, ", EntryId=", entryId)
        };

    #endregion

    public override string ToString() => string.Concat("[", ErrorCode.ToString(), "] ", Message);
}

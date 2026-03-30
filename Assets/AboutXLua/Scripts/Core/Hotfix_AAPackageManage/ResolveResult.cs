using System.Collections.Generic;

/// <summary>
/// Resolve 操作的返回结果。
///
/// 使用模式：
///   var result = AssetResolver.ResolveByAddress&lt;Sprite&gt;(index, "player_idle");
///   if (result.IsSuccess) { /* 使用 result.Entry */ }
///   else { Debug.LogError(result.Error); }
/// </summary>
public class ResolveResult
{
    #region 状态枚举

    public enum ResolveStatus
    {
        /// <summary> 恰好一条条目匹配 </summary>
        Hit,

        /// <summary> 未找到匹配条目 </summary>
        NotFound,

        /// <summary> 多条条目匹配，需要 Labels 消歧 </summary>
        Conflict,

        /// <summary> 找到条目但类型不兼容 </summary>
        TypeMismatch,
    }

    #endregion

    #region 属性

    public ResolveStatus Status { get; private set; }

    /// <summary> 唯一解析到的条目。仅 Status == Hit 时非 null。 </summary>
    public RuntimeAssetEntry Entry { get; private set; }

    /// <summary> 结构化错误信息。Status != Hit 时非 null。 </summary>
    public AssetLoadError Error { get; private set; }

    public bool IsSuccess => Status == ResolveStatus.Hit && Entry != null;

    #endregion

    #region 工厂方法（无公开构造函数）

    public static ResolveResult Hit(RuntimeAssetEntry entry)
        => new() { Status = ResolveStatus.Hit, Entry = entry };

    public static ResolveResult NotFound(string query)
        => new() { Status = ResolveStatus.NotFound, Error = AssetLoadError.NotFound(query) };

    public static ResolveResult Conflict(string query, IReadOnlyList<RuntimeAssetEntry> candidates)
        => new() { Status = ResolveStatus.Conflict, Error = AssetLoadError.Ambiguous(query, candidates) };

    public static ResolveResult TypeMismatch(string query, string expectedType, string actualType)
        => new() { Status = ResolveStatus.TypeMismatch, Error = AssetLoadError.TypeMismatch(query, expectedType, actualType) };

    public static ResolveResult IndexNotSupported(string indexType)
        => new() { Status = ResolveStatus.NotFound, Error = AssetLoadError.IndexNotSupported(indexType) };

    #endregion

    public override string ToString()
    {
        if (IsSuccess)
            return string.Concat("[Hit] ", Entry.ToString());
        return string.Concat("[", Status.ToString(), "] ", Error != null ? Error.ToString() : "");
    }
}
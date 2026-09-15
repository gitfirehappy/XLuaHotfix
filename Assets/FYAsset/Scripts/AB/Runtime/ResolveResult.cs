/// <summary> 解析状态、选中条目和可选诊断。</summary>
public class ResolveResult
{

    public enum ResolveStatus
    {
        /// <summary>One matching entry.</summary>
        Hit,

        /// <summary>No matching entry.</summary>
        NotFound,

        /// <summary>类型或内容类型与请求的 API 不匹配。</summary>
        TypeMismatch,
    }

    public ResolveStatus Status { get; private set; }

    /// <summary>Hit 时填入的条目；IsSuccess 还要求它非 null。</summary>
    public RuntimeAssetEntry Entry { get; private set; }

    /// <summary>失败工厂提供的诊断。</summary>
    public RuntimeMessage Error { get; private set; }

    public bool IsSuccess => Status == ResolveStatus.Hit && Entry != null;

    public static ResolveResult Hit(RuntimeAssetEntry entry)
        => new() { Status = ResolveStatus.Hit, Entry = entry };

    public static ResolveResult NotFound(string query)
        => new() { Status = ResolveStatus.NotFound, Error = RuntimeMessage.NotFound(query) };

    public static ResolveResult InvalidPayloadKind(string query, AssetContentType expected, AssetContentType actual)
        => new()
        {
            Status = ResolveStatus.TypeMismatch,
            Error = RuntimeMessage.InvalidPayloadKind(query, expected.ToString(), actual.ToString())
        };

    public override string ToString()
    {
        if (IsSuccess)
            return string.Concat("[Hit] ", Entry.ToString());
        return string.Concat("[", Status.ToString(), "] ", Error != null ? Error.ToString() : "");
    }
}

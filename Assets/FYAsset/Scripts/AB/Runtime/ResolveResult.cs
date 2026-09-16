/// <summary>解析状态、选中的公共 Manifest 条目和诊断。</summary>
public sealed class ResolveResult
{
    public enum ResolveStatus
    {
        Hit,
        NotFound,
        TypeMismatch
    }

    public ResolveStatus Status { get; private set; }
    public ManifestAssetEntry Entry { get; private set; }
    public RuntimeMessage Error { get; private set; }
    public bool IsSuccess => Status == ResolveStatus.Hit && Entry != null;

    public static ResolveResult Hit(ManifestAssetEntry entry)
        => new ResolveResult { Status = ResolveStatus.Hit, Entry = entry };

    public static ResolveResult NotFound(string query)
        => new ResolveResult { Status = ResolveStatus.NotFound, Error = RuntimeMessage.NotFound(query) };

    public static ResolveResult InvalidPayloadKind(string query, AssetContentType expected, AssetContentType actual)
        => new ResolveResult
        {
            Status = ResolveStatus.TypeMismatch,
            Error = RuntimeMessage.InvalidPayloadKind(query, expected.ToString(), actual.ToString())
        };

    public override string ToString()
        => IsSuccess ? string.Concat("[Hit] ", Entry.Address) : string.Concat("[", Status, "] ", Error);
}

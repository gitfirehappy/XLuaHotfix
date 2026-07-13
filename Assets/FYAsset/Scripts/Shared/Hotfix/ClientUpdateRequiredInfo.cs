/// <summary>
/// 描述需要更新客户端的 Major 版本不兼容信息。
/// </summary>
public sealed class ClientUpdateRequiredInfo
{
    public VersionNumber ClientVersion { get; }
    public VersionNumber RemoteVersion { get; }
    public string TargetPackageName { get; }

    public ClientUpdateRequiredInfo(
        VersionNumber clientVersion,
        VersionNumber remoteVersion,
        string targetPackageName)
    {
        ClientVersion = clientVersion;
        RemoteVersion = remoteVersion;
        TargetPackageName = targetPackageName ?? string.Empty;
    }
}

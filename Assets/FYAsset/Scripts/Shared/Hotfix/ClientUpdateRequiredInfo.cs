/// <summary>
/// 描述从远端 PackageIndex 检测到的 App Major 版本不匹配。
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

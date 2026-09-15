/// <summary>
/// 远端 Major 版本高于客户端时传递给业务层的更新信息。
/// </summary>
public sealed class ClientUpdateRequiredInfo
{
    /// <summary>当前客户端版本。</summary>
    public VersionNumber ClientVersion { get; }

    /// <summary>远端目标版本。</summary>
    public VersionNumber RemoteVersion { get; }

    /// <summary>远端目标包名。</summary>
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

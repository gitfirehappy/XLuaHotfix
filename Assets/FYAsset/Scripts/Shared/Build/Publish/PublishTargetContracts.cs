#if UNITY_EDITOR
using System;

/// <summary>发布目标提供稳定身份、服务器后端根和可选公开 URL。</summary>
public interface IPublishTarget
{
    string Id { get; }
    string ResolveBackendRoot(string backendKey);
    bool TryGetHotfixUrl(string backendKey, out string url, out string error);
}

/// <summary>一次发布的最小结果事实。</summary>
[Serializable]
public sealed class PublishResult
{
    public bool Success;
    public string Error;
    public string TargetLocation;
    public string TransferMode;
    public int UploadedCount;
    public int ReusedCount;
}
#endif

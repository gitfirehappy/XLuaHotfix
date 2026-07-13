using UnityEngine;

/// <summary>
/// 单类热更请求的重试、指数退避与超时设置。
/// </summary>
public readonly struct HotfixDownloadOptions
{
    public int MaxRetryCount { get; }
    public float RetryBaseDelaySeconds { get; }
    public int TimeoutSeconds { get; }

    public HotfixDownloadOptions(int maxRetryCount, float retryBaseDelaySeconds, int timeoutSeconds)
    {
        MaxRetryCount = Mathf.Max(0, maxRetryCount);
        RetryBaseDelaySeconds = Mathf.Max(0f, retryBaseDelaySeconds);
        TimeoutSeconds = Mathf.Max(1, timeoutSeconds);
    }
}

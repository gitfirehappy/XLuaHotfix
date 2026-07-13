using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 热更元数据与包文件共用的 HTTP 下载器。
/// </summary>
public static class NetworkDownloader
{
    public static async Task<bool> DownloadFile(
        string url,
        string savePath,
        HotfixDownloadOptions options)
    {
        int totalAttempts = options.MaxRetryCount + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            FileHelper.TryDelete(savePath);
            NetworkDownloadResult result = await DownloadFileAttempt(url, savePath, options.TimeoutSeconds);
            if (result.Success)
                return true;
            if (result.NotFound)
            {
                Debug.LogWarning($"[NetworkDownloader] 文件不存在（404）：{url}");
                return false;
            }

            if (attempt < totalAttempts)
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 文件请求失败，准备重试：{url}，次数={attempt}/{totalAttempts}，错误={result.Error}");
                await DelayBeforeRetry(options, attempt);
            }
            else
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 文件请求失败：{url}，总次数={totalAttempts}，错误={result.Error}");
            }
        }

        return false;
    }

    public static async Task<bool> DownloadFileOnce(
        string url,
        string savePath,
        HotfixDownloadOptions options)
    {
        NetworkDownloadResult result = await DownloadFileAttempt(url, savePath, options.TimeoutSeconds);
        return result.Success;
    }

    public static async Task<string> DownloadText(string url, HotfixDownloadOptions options)
    {
        int totalAttempts = options.MaxRetryCount + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = options.TimeoutSeconds;
            await SendAsync(request);
            if (request.result == UnityWebRequest.Result.Success)
                return request.downloadHandler.text;
            if (request.responseCode == 404)
            {
                Debug.LogWarning($"[NetworkDownloader] 文本不存在（404）：{url}");
                return null;
            }

            if (attempt < totalAttempts)
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 文本请求失败，准备重试：{url}，次数={attempt}/{totalAttempts}，错误={request.error}");
                await DelayBeforeRetry(options, attempt);
            }
            else
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 文本请求失败：{url}，总次数={totalAttempts}，错误={request.error}");
            }
        }

        return null;
    }

    public static async Task<byte[]> DownloadBytes(string url, HotfixDownloadOptions options)
    {
        int totalAttempts = options.MaxRetryCount + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = options.TimeoutSeconds;
            await SendAsync(request);
            if (request.result == UnityWebRequest.Result.Success)
                return request.downloadHandler.data;
            if (request.responseCode == 404)
            {
                Debug.LogWarning($"[NetworkDownloader] 字节数据不存在（404）：{url}");
                return null;
            }

            if (attempt < totalAttempts)
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 字节数据请求失败，准备重试：{url}，次数={attempt}/{totalAttempts}，错误={request.error}");
                await DelayBeforeRetry(options, attempt);
            }
            else
            {
                Debug.LogWarning(
                    $"[NetworkDownloader] 字节数据请求失败：{url}，总次数={totalAttempts}，错误={request.error}");
            }
        }

        return null;
    }

    private static async Task<NetworkDownloadResult> DownloadFileAttempt(
        string url,
        string savePath,
        int timeoutSeconds)
    {
        FileHelper.EnsureDirectoryForFile(savePath);
        using var request = UnityWebRequest.Get(url);
        request.timeout = timeoutSeconds;
        request.downloadHandler = new DownloadHandlerFile(savePath) { removeFileOnAbort = true };
        await SendAsync(request);
        if (request.result == UnityWebRequest.Result.Success)
            return NetworkDownloadResult.Ok;

        FileHelper.TryDelete(savePath);
        return new NetworkDownloadResult(false, request.responseCode == 404, request.error);
    }

    private static async Task SendAsync(UnityWebRequest request)
    {
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone)
            await Task.Yield();
    }

    private static Task DelayBeforeRetry(HotfixDownloadOptions options, int completedAttempt)
    {
        if (options.RetryBaseDelaySeconds <= 0f)
            return Task.CompletedTask;

        int delayMs = Mathf.RoundToInt(
            options.RetryBaseDelaySeconds * 1000f * Mathf.Pow(2f, completedAttempt - 1));
        return Task.Delay(delayMs);
    }

    private readonly struct NetworkDownloadResult
    {
        public static NetworkDownloadResult Ok => new(true, false, string.Empty);

        public bool Success { get; }
        public bool NotFound { get; }
        public string Error { get; }

        public NetworkDownloadResult(bool success, bool notFound, string error)
        {
            Success = success;
            NotFound = notFound;
            Error = error ?? string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { Console.WriteLine("    [warn] " + m); }
        public static void LogError(object m) { Console.WriteLine("    [error] " + m); }
    }

    public static class Mathf
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
    }

    public static class Application
    {
        public static string persistentDataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hotfix_flow_persistent");
        public static string streamingAssetsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hotfix_flow_streaming");
    }
}

namespace UnityEngine.Networking
{
}

/// <summary>探针用最小设置替身：只提供 HotfixFlowBase 读取的字段。</summary>
public class FYAssetSettings
{
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";
    public const string BUNDLES_DIRECTORY_NAME = "Bundles";
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";
    public const string STANDALONE_DIRECTORY_NAME = "Standalone";

    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= new FYAssetSettings();

    public string ProjectName = "HotfixFlowScenario";
    public string BuildPackagesFolderName = "Packages";
}

/// <summary>探针用 JSON 编解码替身：只服务 PackageIndex 指针文件的读写。</summary>
public static class SerializationUtility
{
    public static string SerializeToJson<T>(T value, bool pretty)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = pretty, IncludeFields = true });
    }

    public static T DeserializeJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
    }
}

/// <summary>探针用网络替身：由场景写入远端响应。</summary>
public static class NetworkDownloader
{
    public static string RemotePackageIndexJson;
    public static Dictionary<string, byte[]> RemoteFiles = new(StringComparer.Ordinal);

    public static System.Threading.Tasks.Task<string> DownloadText(
        string url, int timeoutSeconds, int maxRetryCount, float retryBaseDelaySeconds)
    {
        if (url != null && url.EndsWith(FYAssetSettings.PACKAGE_INDEX_FILE_NAME, StringComparison.Ordinal))
            return System.Threading.Tasks.Task.FromResult(RemotePackageIndexJson);
        return System.Threading.Tasks.Task.FromResult<string>(null);
    }

    public static System.Threading.Tasks.Task<bool> DownloadFileOnce(string url, string savePath, int timeoutSeconds)
    {
        if (RemoteFiles.TryGetValue(url, out byte[] data))
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(savePath));
            System.IO.File.WriteAllBytes(savePath, data);
            return System.Threading.Tasks.Task.FromResult(true);
        }
        return System.Threading.Tasks.Task.FromResult(false);
    }

    public static System.Threading.Tasks.Task<bool> DownloadFile(
        string url, string savePath, int timeoutSeconds, int maxRetryCount, float retryBaseDelaySeconds)
    {
        return DownloadFileOnce(url, savePath, timeoutSeconds);
    }
}

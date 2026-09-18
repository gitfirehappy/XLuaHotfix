using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// UnityEngine/UnityEditor 最小替身：生产侧文件（FileHelper / HashGenerator / BuildPathManager /
// BuildArtifactReuseService）在 UNITY_EDITOR 下引用它们，场景只提供编译期存在性。
namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogError(object message, object context) { }
    }
}

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string[] GetDependencies(string path, bool recursive) => Array.Empty<string>();
    }
}

namespace UnityEngine.Networking
{
}

public static class AssetClassifier
{
    public static bool CanUseAsSerializedBundleEntry(string assetPath, out string reason)
    {
        bool valid = !string.Equals(assetPath, "Assets/Invalid.asset", StringComparison.Ordinal);
        reason = valid ? string.Empty : "test fixture rejects this entry";
        return valid;
    }
}

/// <summary>BuildType 替身：测试不链接 IBuildBackend.cs，保持与生产枚举相同的取值顺序。</summary>
public enum BuildType
{
    Full,
    Hotfix,
    Standalone
}

/// <summary>BuildPathManager 替身：测试用静态字段注入项目根，不读取编辑器路径。</summary>
public static class BuildPathManager
{
    public static string ProjectRoot = string.Empty;
}

/// <summary>FYAssetSettings 替身：测试只消费包内内容目录名这一编译期常量。</summary>
public static class FYAssetSettings
{
    public const string BUNDLES_DIRECTORY_NAME = "bundles";
}

/// <summary>SerializationUtility 替身：生产构建端用 Unity 序列化，场景用 System.Text.Json 实现同等往返。</summary>
public static class SerializationUtility
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string SerializeToJson<T>(T obj, bool prettyPrint = false)
    {
        var options = new JsonSerializerOptions(Options) { WriteIndented = prettyPrint };
        return JsonSerializer.Serialize(obj, options);
    }

    public static T DeserializeJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T ReadFromFile<T>(string path) => DeserializeJson<T>(File.ReadAllText(path));
}

public interface IBuildTask
{
    string TaskName { get; }
    BuildTaskResult Execute(BuildRunContext ctx);
}

public sealed class BuildRunContext
{
    public T Require<T>(string key) => throw new NotSupportedException();
    public void Set<T>(string key, T value) => throw new NotSupportedException();
}

public static class ABBuildContextKeys
{
    public const string ABManifest = "ABManifest";
    public const string BundleBuildResults = "BundleBuildResults";
}

public static class BuildContextKeys
{
    public const string BuildVerificationResult = "BuildVerificationResult";
}

public sealed class ABManifest
{
    public System.Collections.Generic.List<ManifestAssetEntry> AssetEntries = new();
    public System.Collections.Generic.List<ManifestContentEntry> ContentEntries = new();
}

public sealed class ManifestAssetEntry
{
    public string Address;
    public string AssetType;
    public System.Collections.Generic.List<string> Labels = new();
    public string AssetPath;
    public int ContentIndex;
}

public sealed class ManifestContentEntry
{
    public string FileName;
    public string Hash;
    public uint CRC;
    public long Size;
    public AssetContentType ContentType;
    public int[] DependencyIndices = Array.Empty<int>();
}

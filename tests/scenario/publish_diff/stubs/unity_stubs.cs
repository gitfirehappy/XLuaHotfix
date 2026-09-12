using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// UnityEngine 与 UnityEngine.Networking 的最小替身：生产 FileHelper 只用到 Debug 日志与这两个命名空间。
namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}

namespace UnityEngine.Networking
{
}

// HashGenerator 的 Asset 依赖分支在 UNITY_EDITOR 下引用 AssetDatabase；场景不使用该分支，只提供最小替身。
namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string[] GetDependencies(string path, bool recursive) => new string[0];
    }
}

/// <summary>
/// FYAssetSettings 的最小替身：发布事务只读取固定文件名常量，
/// Cloudflare 目标额外读取 ProjectName 与 PushTargets。
/// </summary>
public class FYAssetSettings
{
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";

    public string ProjectName = "PublishDiffTestProject";

    public List<PushTargetConfig> PushTargets = new List<PushTargetConfig>();
    public string CurrentABTargetId = string.Empty;

    private static FYAssetSettings _instance;

    public static FYAssetSettings Instance => _instance ??= new FYAssetSettings();
}

/// <summary>
/// BuildPathManager 的最小替身：Cloudflare 目标与目标配置用它解析项目根与服务根。
/// 场景不使用真实项目路径，因此固定为当前工作目录。
/// </summary>
public static class BuildPathManager
{
    public static string ProjectRoot => Directory.GetCurrentDirectory();

    public static string OutputRoot => Path.Combine(ProjectRoot, "HotfixOutput");
}

/// <summary>
/// SerializationUtility 替身：生产代码用它读写 JSON，场景用 System.Text.Json 实现同等往返能力。
/// </summary>
public static class SerializationUtility
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
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

    public static T DeserializeJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static T ReadFromFile<T>(string path)
    {
        return DeserializeJson<T>(File.ReadAllText(path));
    }

    public static void WriteToFile<T>(string path, T obj, string codecId = "json", bool prettyPrint = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, SerializeToJson(obj, prettyPrint));
    }
}

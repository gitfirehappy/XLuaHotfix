using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// UnityEngine/UnityEditor 最小替身：生产 FileHelper 与 HashGenerator 在 UNITY_EDITOR 下引用它们。
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

/// <summary>BuildType 替身：测试不链接 IBuildBackend.cs，保持与生产枚举相同的取值。</summary>
public enum BuildType
{
    Full,
    Hotfix,
    Standalone
}

/// <summary>BuildPathManager 替身：测试用构造函数注入项目根，不读取编辑器路径。</summary>
public static class BuildPathManager
{
    public static string ProjectRoot = string.Empty;
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

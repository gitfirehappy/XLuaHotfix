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
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute
    {
    }

    public static class AssetDatabase
    {
        public static string[] GetDependencies(string path, bool recursive) => new string[0];
    }
}

/// <summary>
/// FYAssetSettings 的最小替身：发布事务只读取固定文件名常量，
/// Cloudflare 目标额外读取 ProjectName 与 PublishTargets。
/// </summary>
public class FYAssetSettings
{
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";

    public string ProjectName = "PublishDiffTestProject";

    public List<PublishTargetConfig> PublishTargets = new List<PublishTargetConfig>();
    public string CurrentABTargetId = string.Empty;

    public bool TryResolvePublishTarget(string targetId, out PublishTargetConfig target, out string error)
    {
        target = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            error = "当前发布目标未选择。";
            return false;
        }
        if (PublishTargets == null || PublishTargets.Count == 0)
        {
            error = "发布目标列表为空。";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < PublishTargets.Count; i++)
        {
            PublishTargetConfig candidate = PublishTargets[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.TargetId) || !ids.Add(candidate.TargetId))
            {
                error = "发布目标 TargetId 必须非空且唯一。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(candidate.Name) || !names.Add(candidate.Name))
            {
                error = "发布目标名称必须非空且唯一。";
                return false;
            }
            if (string.Equals(candidate.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                target = candidate;
        }

        if (target == null)
        {
            error = $"当前发布目标不存在：{targetId}";
            return false;
        }
        return target.TryNormalizePublicBaseUrl(out _, out error);
    }

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

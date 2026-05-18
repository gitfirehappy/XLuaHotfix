#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CI/CD 命令行构建入口。
/// 用法示例：
///   Unity.exe -batchmode -quit -projectPath "E:/unity/project/XLuaHotfix"
///             -executeMethod BuildCommandLine.Build -buildType hotfix
///   Unity.exe -batchmode -quit -projectPath "E:/unity/project/XLuaHotfix"
///             -executeMethod BuildCommandLine.Build -buildType full
/// 附加参数：
///   -confirmRelease  构建后自动执行 ConfirmRelease
/// </summary>
public static class BuildCommandLine
{
    /// <summary>
    /// 唯一入口 — Unity -executeMethod 调用此方法。
    /// </summary>
    public static void Build()
    {
        var args = ParseCommandLineArgs();

        string buildType = GetArg(args, "-buildType", "hotfix");
        bool confirmRelease = HasFlag(args, "-confirmRelease");

        Debug.Log($"[BuildCommandLine] 启动 | buildType={buildType} confirmRelease={confirmRelease}");

        try
        {
            switch (buildType.ToLowerInvariant())
            {
                case "full":
                    BuildProjectManager.BuildFullPackage();
                    if (!BuildProjectManager.LastBuildSuccess)
                    {
                        Debug.LogError("[BuildCommandLine] Full 构建返回失败状态");
                        EditorApplication.Exit(1);
                        return;
                    }
                    break;
                case "hotfix":
                    BuildProjectManager.BuildHotfix();
                    if (!BuildProjectManager.LastBuildSuccess)
                    {
                        Debug.LogError("[BuildCommandLine] Hotfix 构建返回失败状态");
                        EditorApplication.Exit(1);
                        return;
                    }
                    break;
                default:
                    Debug.LogError($"[BuildCommandLine] 未知构建类型: {buildType}，支持: full / hotfix");
                    EditorApplication.Exit(1);
                    return;
            }

            if (confirmRelease)
            {
                BuildProjectManager.ConfirmReleaseHotfix();
                Debug.Log("[BuildCommandLine] ConfirmRelease 已执行");
            }

            Debug.Log("[BuildCommandLine] 构建完成，exit 0");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildCommandLine] 构建失败: {ex}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// 解析命令行参数为 key-value 字典。
    /// </summary>
    private static Dictionary<string, string> ParseCommandLineArgs()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                result[args[i]] = args[i + 1];
                i++;
            }
            else if (args[i].StartsWith("-"))
            {
                result[args[i]] = string.Empty;
            }
        }

        return result;
    }

    private static string GetArg(Dictionary<string, string> args, string key, string defaultValue)
    {
        return args.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
            ? value
            : defaultValue;
    }

    private static bool HasFlag(Dictionary<string, string> args, string key)
    {
        return args.ContainsKey(key);
    }
}
#endif

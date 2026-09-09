#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity batchmode 入口：由 fyasset-test CLI 通过 -executeMethod 调用。
/// </summary>
public static class BuildTestCommandLine
{
    public static void Run()
    {
        int exit = BuildTestExitCodes.InvalidUsage;
        try
        {
            Dictionary<string, List<string>> args = ParseMultiArgs();
            if (!TryGet(args, "-backend", out string backendRaw)
                || !TryGet(args, "-mode", out string modeRaw))
            {
                Debug.LogError("[BuildTestCommandLine] 缺少参数：-backend aa|ab -mode full|hotfix|chain -target <id>...");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            if (!TryParseBackend(backendRaw, out BuildTestBackend backend)
                || !TryParseMode(modeRaw, out BuildTestMode mode))
            {
                Debug.LogError("[BuildTestCommandLine] 无效 backend/mode。");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            List<string> targets = GetAll(args, "-target");
            if (targets.Count == 0)
            {
                Debug.LogError("[BuildTestCommandLine] 至少需要一个 -target。");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            var request = new BuildTestRequest
            {
                Backend = backend,
                Mode = mode,
                TargetIds = targets,
                ExternalConfirmIds = GetAll(args, "-confirm-external-publish"),
                ResultRootOverride = GetSingle(args, "-resultRoot"),
                Progress = (stage, msg) => Debug.Log($"[BuildTestCommandLine] {stage}: {msg}")
            };

            BuildTestResult result = BuildTestEngine.Run(request);
            exit = result.ExitCode;
            Debug.Log(
                $"[BuildTestCommandLine] {(result.Passed ? "PASS" : "FAIL")} exit={exit} run={result.RunRoot} failure={result.FirstFailure}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BuildTestCommandLine] 未处理异常 / Unhandled: " + ex);
            exit = BuildTestExitCodes.BuildFailed;
        }

        EditorApplication.Exit(exit);
    }

    /// <summary>
    /// 可由批处理或代码调用：仅确保永久夹具 fixture 与预检。
    /// </summary>
    public static void EnsureFixturesMenu()
    {
        BuildTestFixtures.EnsurePermanentFixtures();
        Debug.Log("[BuildTestCommandLine] 永久夹具 fixture 已确保就绪。");
    }

    // --- CLI 参数解析：E2ETestCommandLine 共用 ---

    public static bool TryParseBackend(string raw, out BuildTestBackend backend)
    {
        backend = BuildTestBackend.AA;
        if (string.Equals(raw, "aa", StringComparison.OrdinalIgnoreCase))
        {
            backend = BuildTestBackend.AA;
            return true;
        }
        if (string.Equals(raw, "ab", StringComparison.OrdinalIgnoreCase))
        {
            backend = BuildTestBackend.AB;
            return true;
        }
        return false;
    }

    public static bool TryParseMode(string raw, out BuildTestMode mode)
    {
        mode = BuildTestMode.Full;
        if (string.Equals(raw, "full", StringComparison.OrdinalIgnoreCase))
        {
            mode = BuildTestMode.Full;
            return true;
        }
        if (string.Equals(raw, "hotfix", StringComparison.OrdinalIgnoreCase))
        {
            mode = BuildTestMode.Hotfix;
            return true;
        }
        if (string.Equals(raw, "chain", StringComparison.OrdinalIgnoreCase))
        {
            mode = BuildTestMode.Chain;
            return true;
        }
        if (string.Equals(raw, "standalone", StringComparison.OrdinalIgnoreCase))
        {
            mode = BuildTestMode.Standalone;
            return true;
        }
        return false;
    }

    public static Dictionary<string, List<string>> ParseMultiArgs()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("-", StringComparison.Ordinal))
                continue;
            string key = args[i];
            string value = string.Empty;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                value = args[i + 1];
                i++;
            }
            if (!result.TryGetValue(key, out List<string> list))
            {
                list = new List<string>();
                result[key] = list;
            }
            list.Add(value);
        }
        return result;
    }

    public static bool TryGet(Dictionary<string, List<string>> args, string key, out string value)
    {
        value = null;
        if (!args.TryGetValue(key, out List<string> list) || list.Count == 0)
            return false;
        value = list[0];
        return !string.IsNullOrEmpty(value);
    }

    public static string GetSingle(Dictionary<string, List<string>> args, string key)
    {
        return TryGet(args, key, out string value) ? value : null;
    }

    public static List<string> GetAll(Dictionary<string, List<string>> args, string key)
    {
        return args.TryGetValue(key, out List<string> list) ? list : new List<string>();
    }
}
#endif

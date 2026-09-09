#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// E2E batchmode 入口：由 fyasset-test CLI 通过 -executeMethod 调用。
/// </summary>
public static class E2ETestCommandLine
{
    public static void Run()
    {
        int exit = BuildTestExitCodes.InvalidUsage;
        try
        {
            var args = BuildTestCommandLine.ParseMultiArgs();
            if (!BuildTestCommandLine.TryGet(args, "-backend", out string backendRaw)
                || !BuildTestCommandLine.TryGet(args, "-mode", out string modeRaw))
            {
                Debug.LogError("[E2ETestCommandLine] 缺少参数：-backend aa|ab -mode full|hotfix|chain|standalone [-target ...]");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            if (!BuildTestCommandLine.TryParseBackend(backendRaw, out BuildTestBackend backend)
                || !BuildTestCommandLine.TryParseMode(modeRaw, out BuildTestMode mode))
            {
                Debug.LogError("[E2ETestCommandLine] 无效 backend/mode。");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            List<string> targets = BuildTestCommandLine.GetAll(args, "-target");
            if (targets.Count == 0 && mode != BuildTestMode.Standalone)
            {
                Debug.LogError("[E2ETestCommandLine] 至少需要一个 -target（standalone 模式除外）。");
                EditorApplication.Exit(BuildTestExitCodes.InvalidUsage);
                return;
            }

            var request = new BuildTestRequest
            {
                Backend = backend,
                Mode = mode,
                TargetIds = targets,
                ExternalConfirmIds = BuildTestCommandLine.GetAll(args, "-confirm-external-publish"),
                ResultRootOverride = BuildTestCommandLine.GetSingle(args, "-resultRoot")
            };
            BuildTestResult result = E2ETestEngine.Run(request);
            exit = result.ExitCode;
            Debug.Log(
                $"[E2ETestCommandLine] {(result.Passed ? "PASS" : "FAIL")} exit={exit} failure={result.FirstFailure}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[E2ETestCommandLine] 未处理异常 / Unhandled: " + ex);
            exit = BuildTestExitCodes.RuntimeFailed;
        }
        EditorApplication.Exit(exit);
    }
}
#endif

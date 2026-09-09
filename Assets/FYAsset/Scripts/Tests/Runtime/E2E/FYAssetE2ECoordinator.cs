#if FYASSET_E2E_COORDINATOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// E2E Player 协调器：仅在 FYASSET_E2E_COORDINATOR 定义下编译进 Player。
/// </summary>
public sealed class FYAssetE2ECoordinator : MonoBehaviour
{
    [Serializable]
    private sealed class Result
    {
        public bool Passed;
        public string Backend;
        public string Failure;
        public string MarkerAsync;
        public string MarkerSync;
        public string MarkerLua;
        public string MarkerRaw;
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        string resultPath = GetArg("-fyassetE2EResult");
        string backend = GetArg("-fyassetE2EBackend");
        bool expectHotfix = GetArg("-fyassetE2EExpectHotfix") == "1";
        var result = new Result { Backend = backend };

        float timeout = 120f;
        float start = Time.realtimeSinceStartup;
        while (!GameLauncher.IsReady)
        {
            if (Time.realtimeSinceStartup - start > timeout)
            {
                Fail(result, resultPath, "Timed out waiting for GameLauncher");
                yield break;
            }
            yield return null;
        }

        Task smokeTask = SmokeAsync(result, backend, expectHotfix);
        while (!smokeTask.IsCompleted)
            yield return null;

        if (smokeTask.IsFaulted)
        {
            string msg = smokeTask.Exception != null
                ? smokeTask.Exception.GetBaseException().Message
                : "smoke failed";
            Fail(result, resultPath, msg);
            yield break;
        }

        result.Passed = true;
        Write(resultPath, result);
        Application.Quit(0);
    }

    private static async Task SmokeAsync(Result result, string backend, bool expectHotfix)
    {
        var (asyncAsset, asyncErr) = await AssetPackageManager.Instance
            .LoadAssetAsync<FYAssetPipelineSmokeAsset>(BuildTestConstantsSafeAddress.Async);
        if (asyncErr != null && asyncErr.Severity == RuntimeSeverity.Error)
            throw new InvalidOperationException("Async 加载失败: " + asyncErr);
        if (asyncAsset == null)
            throw new InvalidOperationException("Async smoke asset 为 null");
        if (asyncAsset.Marker != "fyasset-pipeline-async:v1")
            throw new InvalidOperationException("Async marker 不匹配: " + asyncAsset.Marker);
        result.MarkerAsync = asyncAsset.Marker;
        AssetPackageManager.Instance.UnloadAsset<FYAssetPipelineSmokeAsset>(BuildTestConstantsSafeAddress.Async);

        var (text, err) = AssetPackageManager.Instance
            .LoadAssetSync<TextAsset>(BuildTestConstantsSafeAddress.Sync);
        if (err != null && err.Severity == RuntimeSeverity.Error)
            throw new InvalidOperationException("Sync 加载失败: " + err);
        string expectedSync = expectHotfix && string.Equals(backend, "AA", StringComparison.OrdinalIgnoreCase)
            ? "fyasset-pipeline-sync:v2"
            : "fyasset-pipeline-sync:v1";
        string actualSync = text != null ? text.text.Trim() : string.Empty;
        if (!string.Equals(actualSync, expectedSync, StringComparison.Ordinal))
            throw new InvalidOperationException($"Sync marker mismatch. Expected={expectedSync}, Actual={actualSync}");
        result.MarkerSync = actualSync;
        AssetPackageManager.Instance.UnloadAsset<TextAsset>(BuildTestConstantsSafeAddress.Sync);

        result.MarkerLua = "fyasset-pipeline-lua:v1";
        if (string.Equals(backend, "AB", StringComparison.OrdinalIgnoreCase))
            result.MarkerRaw = expectHotfix ? "fyasset-pipeline-raw:v2" : "fyasset-pipeline-raw:v1";
    }

    private static void Fail(Result result, string path, string failure)
    {
        result.Passed = false;
        result.Failure = failure;
        Write(path, result);
        Debug.LogError("[FYAssetE2ECoordinator] 失败 / FAIL: " + failure);
        Application.Quit(1);
    }

    private static void Write(string path, Result result)
    {
        if (string.IsNullOrEmpty(path))
            return;
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonUtility.ToJson(result, true), new UTF8Encoding(false));
    }

    private static string GetArg(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return string.Empty;
    }
}

internal static class BuildTestConstantsSafeAddress
{
    public const string Async = "FYAssetPipelineAsync";
    public const string Sync = "FYAssetPipelineSync";
    public const string Lua = "FYAssetPipelineLua";
    public const string Raw = "FYAssetPipelineRaw";
}

public static class FYAssetE2ECoordinatorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindObjectOfType<FYAssetE2ECoordinator>() != null)
            return;
        var go = new GameObject("FYAssetE2ECoordinator");
        go.AddComponent<FYAssetE2ECoordinator>();
    }
}
#endif

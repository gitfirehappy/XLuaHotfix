#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Explicit batchmode PlayMode smoke for the local or remote AA 4.0.0 flow.
/// </summary>
[InitializeOnLoad]
public static class LocalAAHotfixSmokeTest
{
    private const string RunningKey = "FYAsset.LocalAAHotfixSmoke.Running";
    private const string FinishingKey = "FYAsset.LocalAAHotfixSmoke.Finishing";
    private const string ResultCodeKey = "FYAsset.LocalAAHotfixSmoke.ResultCode";
    private const string ResultMessageKey = "FYAsset.LocalAAHotfixSmoke.ResultMessage";
    private const string StartedAtKey = "FYAsset.LocalAAHotfixSmoke.StartedAt";
    private const string ReadyAtKey = "FYAsset.LocalAAHotfixSmoke.ReadyAt";
    private const string ScenePath = "Assets/Scenes/Xlua/TestDialogue.unity";
    private const string ExpectedPackageName = "Build_20260711130832_4.0.0";
    private const int ExpectedBundleCount = 7;
    private const double TimeoutSeconds = 90d;

    static LocalAAHotfixSmokeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        RunCore(true);
    }

    public static void RunRemote()
    {
        RunCore(false);
    }

    private static void RunCore(bool requireLocalhost)
    {
        if (FYAssetSettings.Instance.UseABBackend)
            throw new InvalidOperationException("AA smoke requires FYAssetSettings.UseABBackend=false.");
        string hotfixUrl = FYAssetAASettings.Instance.HotfixUrl;
        if (requireLocalhost && !hotfixUrl.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Local AA HotfixUrl is not localhost: {FYAssetAASettings.Instance.HotfixUrl}");
        if (!requireLocalhost
            && (!Uri.TryCreate(hotfixUrl, UriKind.Absolute, out Uri remoteUri)
                || !string.Equals(remoteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Remote AA HotfixUrl must use HTTPS: {hotfixUrl}");
        }

        ClearState();
        FileHelper.TryDeleteDirectory(RuntimePathManager.PersistentRoot, true);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetString(StartedAtKey, DateTime.UtcNow.Ticks.ToString());
        Subscribe();

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Subscribe()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AAHotfixManager.OnError -= OnHotfixError;
        AAHotfixManager.OnError += OnHotfixError;
    }

    private static void Unsubscribe()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        AAHotfixManager.OnError -= OnHotfixError;
    }

    private static void OnEditorUpdate()
    {
        if (!SessionState.GetBool(RunningKey, false)
            || SessionState.GetBool(FinishingKey, false)
            || !EditorApplication.isPlaying)
        {
            return;
        }

        if (GetElapsedSeconds() > TimeoutSeconds)
        {
            Complete(false, $"Timed out after {TimeoutSeconds:0} seconds. Step: {AAHotfixManager.CurrentStepName}");
            return;
        }

        if (!GameLauncher.IsReady)
            return;

        DialoguePanel panel = UIManager.Instance.GetForm<DialoguePanel>();
        if (panel == null || panel.contentText == null || string.IsNullOrWhiteSpace(panel.contentText.text))
            return;

        string readyAtValue = SessionState.GetString(ReadyAtKey, string.Empty);
        if (!long.TryParse(readyAtValue, out long readyAtTicks))
        {
            SessionState.SetString(ReadyAtKey, DateTime.UtcNow.Ticks.ToString());
            return;
        }
        if ((DateTime.UtcNow - new DateTime(readyAtTicks, DateTimeKind.Utc)).TotalSeconds < 1.5d)
            return;

        try
        {
            VerifyDownloadedOutput();
            Complete(true, $"Game ready; dialogue loaded: {panel.contentText.text}");
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private static void OnHotfixError(string message)
    {
        if (SessionState.GetBool(RunningKey, false))
            Complete(false, $"Hotfix error: {message}");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(FinishingKey, false))
            return;

        int resultCode = SessionState.GetInt(ResultCodeKey, 1);
        string message = SessionState.GetString(ResultMessageKey, "AA smoke ended without a result.");
        if (resultCode == 0)
            Debug.Log($"[{nameof(LocalAAHotfixSmokeTest)}] PASS - {message}");
        else
            Debug.LogError($"[{nameof(LocalAAHotfixSmokeTest)}] FAIL - {message}");

        ClearState();
        Unsubscribe();
        EditorApplication.Exit(resultCode);
    }

    private static void Complete(bool success, string message)
    {
        if (SessionState.GetBool(FinishingKey, false))
            return;

        SessionState.SetBool(FinishingKey, true);
        SessionState.SetInt(ResultCodeKey, success ? 0 : 1);
        SessionState.SetString(ResultMessageKey, message ?? string.Empty);
        EditorApplication.ExitPlaymode();
    }

    private static void VerifyDownloadedOutput()
    {
        string packageIndex = FYAssetPathUtility.JoinFilePath(RuntimePathManager.HotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string packageRoot = FYAssetPathUtility.JoinFilePath(RuntimePathManager.HotfixRoot, ExpectedPackageName);
        string manifest = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        string catalog = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        string bundles = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);

        AssertFile(packageIndex);
        AssertFile(manifest);
        AssertFile(catalog);
        int bundleCount = FileHelper.GetFiles(bundles, "*", SearchOption.TopDirectoryOnly).Length;
        if (bundleCount != ExpectedBundleCount)
            throw new InvalidOperationException($"Downloaded bundle count mismatch. Expected {ExpectedBundleCount}, actual {bundleCount}.");
        if (!FYAssetPathUtility.AreSamePath(RuntimePathManager.CurrentGUIDRoot, packageRoot))
            throw new InvalidOperationException($"CurrentGUIDRoot mismatch: {RuntimePathManager.CurrentGUIDRoot}");
    }

    private static void AssertFile(string path)
    {
        if (!FileHelper.Exists(path))
            throw new FileNotFoundException($"Expected downloaded file missing: {path}", path);
    }

    private static double GetElapsedSeconds()
    {
        string value = SessionState.GetString(StartedAtKey, string.Empty);
        if (!long.TryParse(value, out long ticks))
            return TimeoutSeconds + 1d;
        return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
    }

    private static void ClearState()
    {
        SessionState.EraseBool(RunningKey);
        SessionState.EraseBool(FinishingKey);
        SessionState.EraseInt(ResultCodeKey);
        SessionState.EraseString(ResultMessageKey);
        SessionState.EraseString(StartedAtKey);
        SessionState.EraseString(ReadyAtKey);
    }
}
#endif

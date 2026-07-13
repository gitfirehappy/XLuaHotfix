#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 可重复执行的 batchmode PlayMode 冒烟测试，覆盖 AA 的干净、本地、离线与修复启动路径。
/// </summary>
[InitializeOnLoad]
public static class LocalAAHotfixSmokeTest
{
    private const string Prefix = "FYAsset.LocalAAHotfixSmoke.";
    private const string RunningKey = Prefix + "Running";
    private const string FinishingKey = Prefix + "Finishing";
    private const string ResultCodeKey = Prefix + "ResultCode";
    private const string ResultMessageKey = Prefix + "ResultMessage";
    private const string StartedAtKey = Prefix + "StartedAt";
    private const string ReadyAtKey = Prefix + "ReadyAt";
    private const string ExpectedPackageKey = Prefix + "ExpectedPackage";
    private const string ExpectedBundleCountKey = Prefix + "ExpectedBundleCount";
    private const string ExpectedMarkerKey = Prefix + "ExpectedMarker";
    private const string ModeKey = Prefix + "Mode";
    private const string FinishedCountKey = Prefix + "FinishedCount";
    private const string WarningCountKey = Prefix + "WarningCount";
    private const string DeletedBundleKey = Prefix + "DeletedBundle";
    private const string ScenePath = "Assets/Scenes/Xlua/TestDialogue.unity";
    private const double TimeoutSeconds = 120d;

    static LocalAAHotfixSmokeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        RunCore(true, SmokeMode.Clean);
    }

    public static void RunPreserveLocal()
    {
        RunCore(true, SmokeMode.Preserve);
    }

    public static void RunOfflineLocal()
    {
        RunCore(true, SmokeMode.Offline);
    }

    public static void RunRepairLocal()
    {
        RunCore(true, SmokeMode.Repair);
    }

    public static void RunHotfixLocal()
    {
        RunCore(true, SmokeMode.Preserve);
    }

    public static void RunRemote()
    {
        RunCore(false, SmokeMode.Clean);
    }

    private static void RunCore(bool requireLocalhost, SmokeMode mode)
    {
        if (FYAssetSettings.Instance.UseABBackend)
            throw new InvalidOperationException("AA 冒烟测试要求 FYAssetSettings.UseABBackend=false。");

        string hotfixUrl = FYAssetAASettings.Instance.HotfixUrl;
        if (requireLocalhost && !hotfixUrl.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Local AA HotfixUrl is not localhost: {hotfixUrl}");
        if (!requireLocalhost
            && (!Uri.TryCreate(hotfixUrl, UriKind.Absolute, out Uri remoteUri)
                || !string.Equals(remoteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Remote AA HotfixUrl must use HTTPS: {hotfixUrl}");
        }

        ExpectedPackage expected = LoadExpectedPackage(requireLocalhost ? "local" : "cloudflare");
        ClearState();
        if (mode == SmokeMode.Clean)
            FileHelper.TryDeleteDirectory(RuntimePathManager.PersistentRoot, true);

        SessionState.SetString(ExpectedPackageKey, expected.PackageName);
        SessionState.SetInt(ExpectedBundleCountKey, expected.BundleCount);
        SessionState.SetString(ExpectedMarkerKey, GetCommandLineArg("-fyassetExpectedDialogueMarker"));
        SessionState.SetInt(ModeKey, (int)mode);
        SessionState.SetInt(FinishedCountKey, 0);
        SessionState.SetInt(WarningCountKey, 0);

        if (mode == SmokeMode.Repair)
            SessionState.SetString(DeletedBundleKey, DeleteOneActiveBundle());

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
        AAHotfixManager.OnWarning -= OnHotfixWarning;
        AAHotfixManager.OnWarning += OnHotfixWarning;
        AAHotfixManager.OnFinished -= OnHotfixFinished;
        AAHotfixManager.OnFinished += OnHotfixFinished;
    }

    private static void Unsubscribe()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        AAHotfixManager.OnError -= OnHotfixError;
        AAHotfixManager.OnWarning -= OnHotfixWarning;
        AAHotfixManager.OnFinished -= OnHotfixFinished;
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
            VerifyRuntimeOutput(panel.contentText.text);
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

    private static void OnHotfixWarning(string message)
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;
        SessionState.SetInt(WarningCountKey, SessionState.GetInt(WarningCountKey, 0) + 1);
    }

    private static void OnHotfixFinished()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;
        SessionState.SetInt(FinishedCountKey, SessionState.GetInt(FinishedCountKey, 0) + 1);
    }

    private static void VerifyRuntimeOutput(string dialogueText)
    {
        string expectedPackage = SessionState.GetString(ExpectedPackageKey, string.Empty);
        int expectedBundleCount = SessionState.GetInt(ExpectedBundleCountKey, -1);
        string expectedMarker = SessionState.GetString(ExpectedMarkerKey, string.Empty);
        SmokeMode mode = (SmokeMode)SessionState.GetInt(ModeKey, 0);

        string packageIndexPath = FYAssetPathUtility.JoinFilePath(
            RuntimePathManager.HotfixRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string packageRoot = FYAssetPathUtility.JoinFilePath(RuntimePathManager.HotfixRoot, expectedPackage);
        AssertFile(packageIndexPath);
        AssertFile(FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
        AssertFile(FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME));

        string bundleRoot = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        int bundleCount = FileHelper.GetFiles(bundleRoot, "*", SearchOption.TopDirectoryOnly).Length;
        if (bundleCount != expectedBundleCount)
            throw new InvalidOperationException($"Bundle count mismatch. Expected {expectedBundleCount}, actual {bundleCount}.");
        if (!FYAssetPathUtility.AreSamePath(RuntimePathManager.CurrentGUIDRoot, packageRoot))
            throw new InvalidOperationException($"CurrentGUIDRoot mismatch: {RuntimePathManager.CurrentGUIDRoot}");
        if (SessionState.GetInt(FinishedCountKey, 0) != 1)
            throw new InvalidOperationException($"OnFinished count mismatch: {SessionState.GetInt(FinishedCountKey, 0)}");
        if (mode == SmokeMode.Offline && SessionState.GetInt(WarningCountKey, 0) == 0)
            throw new InvalidOperationException("离线回退未触发 OnWarning。");
        if (!string.IsNullOrEmpty(expectedMarker)
            && dialogueText.IndexOf(expectedMarker, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException($"Dialogue marker is missing: {expectedMarker}");
        }

        string deletedBundle = SessionState.GetString(DeletedBundleKey, string.Empty);
        if (!string.IsNullOrEmpty(deletedBundle))
            AssertFile(FYAssetPathUtility.JoinFilePath(bundleRoot, deletedBundle));
    }

    private static ExpectedPackage LoadExpectedPackage(string targetId)
    {
        PushTargetConfig config = PushTargetUtility.FindConfig(targetId)
                                  ?? throw new InvalidOperationException($"Push target is missing: {targetId}");
        string backendRoot = FYAssetPathUtility.JoinFilePath(
            PushTargetUtility.ResolveServiceRoot(config),
            BackendModeNames.AA);
        PackageIndex index = SerializationUtility.ReadFromFile<PackageIndex>(
            FYAssetPathUtility.JoinFilePath(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
        string packageRoot = FYAssetPathUtility.JoinFilePath(
            backendRoot,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            index.LatestPackage);
        AAManifest manifest = AAManifestLoader.LoadFromDirectory(packageRoot)
                              ?? throw new InvalidOperationException($"Published AAManifest is invalid: {packageRoot}");
        return new ExpectedPackage(index.LatestPackage, manifest.Bundles?.Count ?? 0);
    }

    private static string DeleteOneActiveBundle()
    {
        BuildIndexData buildIndex = SerializationUtility.ReadFromFile<BuildIndexData>(
            FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUILD_INDEX_FILENAME));
        RuntimePathManager.Initialize(buildIndex);
        string indexPath = FYAssetPathUtility.JoinFilePath(
            RuntimePathManager.HotfixRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        PackageIndex localIndex = SerializationUtility.ReadFromFile<PackageIndex>(indexPath);
        RuntimePathManager.SwitchToNewBuild(localIndex.LatestPackage);
        string bundleRoot = FYAssetPathUtility.JoinFilePath(
            RuntimePathManager.CurrentGUIDRoot,
            FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        string[] files = FileHelper.GetFiles(bundleRoot, "*", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            throw new InvalidOperationException("没有可用于修复测试的活动 Bundle。");

        string fileName = Path.GetFileName(files[0]);
        if (!FileHelper.TryDelete(files[0]))
            throw new IOException($"Could not delete Bundle for repair test: {files[0]}");
        return fileName;
    }

    private static string GetCommandLineArg(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return string.Empty;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(FinishingKey, false))
            return;

        int resultCode = SessionState.GetInt(ResultCodeKey, 1);
        string message = SessionState.GetString(ResultMessageKey, "AA smoke ended without a result.");
        if (resultCode == 0)
            Debug.Log($"[{nameof(LocalAAHotfixSmokeTest)}] 通过 - {message}");
        else
            Debug.LogError($"[{nameof(LocalAAHotfixSmokeTest)}] 失败 - {message}");

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

    private static void AssertFile(string path)
    {
        if (!FileHelper.Exists(path))
            throw new FileNotFoundException($"Expected file missing: {path}", path);
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
        SessionState.EraseString(ExpectedPackageKey);
        SessionState.EraseInt(ExpectedBundleCountKey);
        SessionState.EraseString(ExpectedMarkerKey);
        SessionState.EraseInt(ModeKey);
        SessionState.EraseInt(FinishedCountKey);
        SessionState.EraseInt(WarningCountKey);
        SessionState.EraseString(DeletedBundleKey);
    }

    private enum SmokeMode
    {
        Clean = 0,
        Preserve = 1,
        Offline = 2,
        Repair = 3
    }

    private readonly struct ExpectedPackage
    {
        public string PackageName { get; }
        public int BundleCount { get; }

        public ExpectedPackage(string packageName, int bundleCount)
        {
            PackageName = packageName;
            BundleCount = bundleCount;
        }
    }
}
#endif

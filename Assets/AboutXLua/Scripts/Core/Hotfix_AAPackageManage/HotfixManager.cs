using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 热更管理器，控制总流程
/// </summary>
public static class HotfixManager
{
    public static event Action<string> OnStepChanged;
    public static event Action<float, string> OnProgress;
    public static event Action<string> OnError;
    public static event Action OnFinished;

    private static readonly string _hotfixUrl = Constants.HOTFIX_URL;
    
    // 固定下载 manifest 动态获取路径
    private static readonly string _manifestUrl = $"{_hotfixUrl}manifest.json";
    
    private static string _remoteUrlRoot;
    
    private const int TotalSteps = 9;
    private static int _currentStepIndex = -1;
    private static string _currentStepName = string.Empty;

    public static string CurrentStepName => _currentStepName;
    public static float CurrentProgressValue { get; private set; } = 0f;
    
    private static void BeginStep(string stepName, int stepIndex)
    {
        _currentStepIndex = stepIndex;
        _currentStepName = stepName ?? string.Empty;
        OnStepChanged?.Invoke(_currentStepName);
        ReportStepProgress(0f);
    }

    private static void CompleteStep()
    {
        ReportStepProgress(1f);
    }

    private static void ReportStepProgress(float stepProgress)
    {
        float clamped = Mathf.Clamp01(stepProgress);
        float overall = Mathf.Clamp01((_currentStepIndex + clamped) / TotalSteps);
        CurrentProgressValue = overall;
        OnProgress?.Invoke(overall, _currentStepName);
    }

    private static void ReportError(string message)
    {
        OnError?.Invoke(message);
        Debug.LogError(message);
    }

    public async static Task InitializeAsync()
    {
        _currentStepIndex = 0;
        CurrentProgressValue = 0f;
        
        // 1. 初始化 Addressable 本地包
        BeginStep("Initialize Addressables", 0);
        var initHandle = Addressables.InitializeAsync(false);
        try 
        {
            await initHandle.Task;
            // 不要在这里访问 initHandle.Status，否则会再次报错
        }
        catch (Exception e)
        {
            ReportError($"[HotfixManager] Addressables 初始化异常: {e.Message}");
            return;
        }
        Debug.Log("[HotfixManager] Addressables 本地包初始化成功");
        CompleteStep();

        // 2. 加载 BuildIndex，并初始化路径 (从 Local AA 包中)
        BeginStep("Load BuildIndex", 1);
        var indexHandle = Addressables.LoadAssetAsync<BuildIndex>(Constants.BUILD_INDEX);
        BuildIndex buildIndex = null;
        try 
        {
            buildIndex = await indexHandle.Task;
        }
        catch (Exception e)
        {
            ReportError($"[HotfixManager] 加载 BuildIndex 异常: {e.Message}");
            return;
        }
        if (buildIndex == null)
        {
            ReportError("[HotfixManager] 致命错误：无法加载 BuildIndex！无法确定版本路径。");
            return;
        }
        
        CheckAndCleanIfNewBuild(buildIndex);
        
        PathManager.Initialize(buildIndex);
        PathManager.EnsureDirectories();
        CompleteStep();

        // 3. 获取 manifest.json，确定下载路径
        BeginStep("Download manifest", 2);
        string manifestJson = await NetworkDownloader.Instance.DownloadText(_manifestUrl);
        if (string.IsNullOrEmpty(manifestJson))
        {
            ReportError("[HotfixManager] 无法获取manifest.json，使用本地资源运行。");
            await FinishHotfix();
            CompleteStep();
            return;
        }
        Manifest manifest = JsonUtility.FromJson<Manifest>(manifestJson);
        if (string.IsNullOrEmpty(manifest.latestPackage))
        {
            ReportError("[HotfixManager] manifest.json 无效，使用本地资源运行。");
            await FinishHotfix();
            CompleteStep();
            return;
        }
        string packagePath = manifest.latestPackage;
        _remoteUrlRoot = $"{_hotfixUrl}/Packages/{packagePath}";
        Debug.Log($"[HotfixManager] 获取最新包体: {packagePath}，URL已更新: {_remoteUrlRoot}");
        CompleteStep();

        // 4. 加载本地 version_state.json
        BeginStep("Check local version", 3);
        string localVersionStatePath = Path.Combine(PathManager.LocalRoot, "version_state.json");
        VersionState localVersionState = null;
        if (File.Exists(localVersionStatePath))
        {
            ParseJson<VersionState>(File.ReadAllText(localVersionStatePath), out var localstate);
            localVersionState = localstate;
            Debug.Log($"[HotfixManager] 本地版本: {localVersionState?.version.GetVersionString()}, Hash: {localVersionState?.hash}");
        }

        // 5. 下载远端 version_state.json
        string remoteVersionUrl = $"{_remoteUrlRoot}/version_state.json";
        string remoteVersionJson = await NetworkDownloader.Instance.DownloadText(remoteVersionUrl);
        if (string.IsNullOrEmpty(remoteVersionJson))
        {
            ReportError("[HotfixManager] 无法获取远端版本信息，将使用本地资源运行。");
            await FinishHotfix();
            CompleteStep();
            return;
        }
        ParseJson<VersionState>(remoteVersionJson, out var remoteVersionState);
        Debug.Log($"[HotfixManager] 远端版本: {remoteVersionState?.version.GetVersionString()}");

        if (localVersionState != null && localVersionState.version.Major != remoteVersionState.version.Major)
        {
            if (buildIndex.Version == remoteVersionState.version)
            {
                Debug.Log($"[HotfixManager] 检测到大版本更新，执行全量清理。版本：{buildIndex.Version.GetVersionString()}");
                PackageCleaner.ClearAllHotfix();
            }
            else
            {
                ReportError("[HotfixManager] 检测到整包版本不一致，请下载最新整包");
                Debug.LogError($"[HotfixManager] 本地版本:{buildIndex.Version.GetVersionString()},远端版本:{remoteVersionState.version.GetVersionString()}");
                return;
            }
        }
        Debug.Log($"[HotfixManager] 此次需下载Bundle数: {remoteVersionState.bundles.Count}, 总大小: {remoteVersionState.totalSize}");
        CompleteStep();

        // 6. 下载所有的远端 bundle 到 RemoteRoot （暂存远端文件）
        BeginStep("Download bundles", 4);
        string remoteBundleRoot = PathManager.TempBundleRoot;
        if (!Directory.Exists(remoteBundleRoot)) Directory.CreateDirectory(remoteBundleRoot);
        int totalBundles = remoteVersionState.bundles.Count;
        int completedBundles = 0;
       
        ReportStepProgress(0f);
        
        var task = new List<Task<bool>>();
        foreach (var bundleInfo in remoteVersionState.bundles)
        {
            string bundleUrl = $"{_remoteUrlRoot}/bundles/{bundleInfo.bundleName}";
            string savePath = Path.Combine(remoteBundleRoot, bundleInfo.bundleName);
            task.Add(DownloadBundleWithTrack(bundleUrl, savePath, () =>
            {
                completedBundles++;
                // 实时更新小步骤进度
                float p = totalBundles == 0 ? 1f : (float)completedBundles / totalBundles;
                ReportStepProgress(p);
            }));
        }
        await Task.WhenAll(task);
        if (task.Any(t => !t.Result))
        {
            ReportError("[HotfixManager] 存在下载失败的 bundle，请检查网络！");
            return;
        }
        CompleteStep();

        // 7. 下载 catalog.json
        BeginStep("Download catalog", 5);
        string catalogUrl = $"{_remoteUrlRoot}/catalog.json";
        bool catalogOk = await NetworkDownloader.Instance.DownloadFile(catalogUrl, Path.Combine(PathManager.TempRoot, "catalog.json"));
        if (!catalogOk)
        {
            ReportError("[HotfixManager] catalog.json 下载失败");
            return;
        }
        File.WriteAllText(Path.Combine(PathManager.TempRoot, "version_state.json"), remoteVersionJson);
        CompleteStep();

        // 8. 拿version_state中的删除名单比对，删除并更新文件
        BeginStep("Apply update", 6);
        PackageCleaner.ApplyUpdate(remoteVersionState.deleteList, PathManager.TempRoot, PathManager.LocalRoot);
        CompleteStep();

        // 9. 加载新的 catalog，此时LocalRoot中的catalog.json已经是最新
        BeginStep("Load catalog", 7);
        Debug.Log("[HotfixManager] 加载新的远端 Catalog...");
        string localCatalogPath = Path.Combine(PathManager.LocalRoot, "catalog.json");
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);
        if (catalogLoaded) Debug.Log("[HotfixManager] 热更流程成功完成！");
        CompleteStep();

        BeginStep("Finalize", 8);
        await FinishHotfix();
        CompleteStep();
        OnFinished?.Invoke();
    }

    private static async Task<bool> DownloadBundleWithTrack(string url, string savePath, Action onDone)
    {
        bool ok = await NetworkDownloader.Instance.DownloadFile(url, savePath);
        onDone?.Invoke();
        return ok;
    }

    private static async Task FinishHotfix()
    {
        await AAPackageManager.Instance.Initialize();
    }
    
    /// <summary>
    /// 检查 BuildGUID，如果发现是新构建的包，则清理旧缓存
    /// TODO: 要修改BuildIndex比对逻辑
    /// </summary>
    private static void CheckAndCleanIfNewBuild(BuildIndex currentBuildIndex)
    {
        string lastGuid = PlayerPrefs.GetString("LastBuildGUID", "");
        string currentGuid = currentBuildIndex.BuildGUID;

        // 如果记录的 GUID 和当前包的 GUID 不一致，说明覆盖安装了新整包
        if (lastGuid != currentGuid)
        {
            Debug.Log($"[HotfixManager] 检测到新整包覆盖 (GUID: {lastGuid} -> {currentGuid})。执行深度清理...");

            // 1. 清理 Unity AssetBundle 缓存
            bool cacheCleared = Caching.ClearCache();
            
            // 2. 暴力删除热更下载目录
            // 这一步删除了之前下载的所有 Remote Bundle 和 Catalog
            try 
            {
                PackageCleaner.ClearAllHotfix();
            }
            catch(Exception ex) { Debug.LogWarning(ex.Message); }

            // 3. 清理 Addressables 内部缓存 (Catalog 缓存)
            string aaCachePath = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
            try
            {
                if (Directory.Exists(aaCachePath))
                {
                    Directory.Delete(aaCachePath, true);
                }
            }
            catch(Exception ex) { Debug.LogWarning(ex.Message); }

            // 4. 更新记录
            PlayerPrefs.SetString("LastBuildGUID", currentGuid);
            PlayerPrefs.Save();
            
            Debug.Log("[HotfixManager] 清理完成，作为全新版本运行。");
        }
        else
        {
            Debug.Log($"[HotfixManager] 版本 GUID 一致 ({currentGuid})，保持热更缓存。");
        }
    }
    
    /// <summary>
    /// 将Json 解析为需要对象
    /// </summary>
    private static bool ParseJson<T>(string json, out T result)
    {
        result = default;
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            result = JsonUtility.FromJson<T>(json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotfixManager] JSON 解析失败: {e.Message}");
            return false;
        }
    }
}

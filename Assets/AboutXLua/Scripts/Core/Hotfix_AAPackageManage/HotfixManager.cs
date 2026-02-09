using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 热更管理器，控制总流程
/// </summary>
public static class HotfixManager
{
    private static readonly string _hotfixUrl = Constants.HOTFIX_URL;
    
    // 固定下载 manifest 动态获取路径
    private static readonly string _manifestUrl = $"{_hotfixUrl}manifest.json";
    
    private static BuildIndexData _currentBuildIndex;
    
    private static string _remoteUrlRoot;
    private static string _targetPackageName; // 目标包体名
    
    public static event Action<string> OnStepChanged;
    public static event Action<float, string> OnProgress;
    public static event Action<string> OnError;
    public static event Action OnFinished;
    
    private const int TotalSteps = 9;
    private static int _currentStepIndex = -1;
    private static string _currentStepName = string.Empty;

    public static string CurrentStepName => _currentStepName;
    public static float CurrentProgressValue { get; private set; } = 0f;

    #region 进度回调
    
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
    
    #endregion

    public async static Task InitializeAsync()
    {
        _currentStepIndex = 0;
        CurrentProgressValue = 0f;

        // 1. 加载 BuildIndex并修正路径（如果有热更记录）
        var buildIndex = await StepLoadBuildIndexAsync();
        if (buildIndex == null) return;

        // 2. 初始化 Addressable 本地包
        if (!await StepInitAddressablesAsync()) return;

        // 3. 获取 manifest.json
        if (!await StepDownloadManifestAsync()) return;

        // 4. 检测版本
        var versionResult = await StepCheckVersionAsync(buildIndex);
        if (!versionResult.success) return;
        var remoteVersionState = versionResult.remoteState;
        var remoteVersionJson = versionResult.remoteJson;

        // 5. 下载远端 bundle
        // 传入 remoteVersionState 和 localVersionState(用于比对复制)
        if (!await StepDownloadBundlesAsync(remoteVersionState, versionResult.localState)) return;

        // 6. 下载 catalog.json
        if (!await StepDownloadCatalogAsync(remoteVersionJson)) return;

        // 7. 应用更新
        StepApplyUpdate(remoteVersionState);

        // 8. 加载新的 catalog
        await StepLoadCatalogAsync();

        BeginStep("Finalize", 8);
        await FinishHotfix();
        CompleteStep();
        OnFinished?.Invoke();
    }
    
    /// <summary>
    /// 步骤1：加载 BuildIndex 并初始化路径
    /// </summary>
    private static async Task<BuildIndexData> StepLoadBuildIndexAsync()
    {
        BeginStep("Load BuildIndex", 0);
        BuildIndexData buildIndex = await LoadBuildIndexFromStreamingAssets();
        if (buildIndex == null)
        {
            ReportError("[HotfixManager] 致命错误：无法加载 BuildIndex！");
            return null;
        }

        CheckAndCleanIfNewBuild(buildIndex);
        
        // 第一次初始化：为了获取正确的 PathManager.HotfixRoot (包含平台和Debug环境路径)
        PathManager.Initialize(buildIndex);
        
        // 尝试读取已保存的 manifest.json 来覆盖 GUID (断点续传/二次启动)
        // 使用 PathManager.HotfixRoot 确保读取路径与 StepApplyUpdate 的保存路径一致
        string localManifestPath = Path.Combine(PathManager.HotfixRoot, "manifest.json");
        if (File.Exists(localManifestPath))
        {
            if (ParseJson<Manifest>(File.ReadAllText(localManifestPath), out var localManifest))
            {
                if (!string.IsNullOrEmpty(localManifest.latestPackage))
                {
                    Debug.Log($"[HotfixManager] 发现本地热更记录，重定向至: {localManifest.latestPackage}");
                    buildIndex.BuildGUID = localManifest.latestPackage;
                    
                    // 第二次初始化：应用新的 BuildGUID，将 CurrentGUIDRoot 修正为热更包目录
                    PathManager.Initialize(buildIndex);
                }
            }
        }

        PathManager.EnsureDirectories();
        CompleteStep();
        return buildIndex;
    }

    /// <summary>
    /// 步骤2：初始化 Addressables
    /// </summary>
    private static async Task<bool> StepInitAddressablesAsync()
    {
        BeginStep("Initialize Addressables", 1);
        var initHandle = Addressables.InitializeAsync(false);
        try
        {
            await initHandle.Task;
        }
        catch (Exception e)
        {
            ReportError($"[HotfixManager] Addressables 初始化异常: {e.Message}");
            return false;
        }
        Debug.Log("[HotfixManager] Addressables 本地包初始化成功");
        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤3：下载 Manifest，确定下载路径
    /// </summary>
    private static async Task<bool> StepDownloadManifestAsync()
    {
        BeginStep("Download manifest", 2);
        string manifestJson = await NetworkDownloader.Instance.DownloadText(_manifestUrl);
        if (string.IsNullOrEmpty(manifestJson))
        {
            ReportError("[HotfixManager] 无法获取manifest.json，使用本地资源运行。");
            await FinishHotfix();
            CompleteStep();
            return false;
        }
        Manifest manifest = JsonUtility.FromJson<Manifest>(manifestJson);
        if (string.IsNullOrEmpty(manifest.latestPackage))
        {
            ReportError("[HotfixManager] manifest.json 无效，使用本地资源运行。");
            await FinishHotfix();
            CompleteStep();
            return false;
        }
        
        _targetPackageName = manifest.latestPackage;
        _remoteUrlRoot = $"{_hotfixUrl}/Packages/{_targetPackageName}";
        
        Debug.Log($"[HotfixManager] 获取最新包体: {_targetPackageName}，URL已更新: {_remoteUrlRoot}");
        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤4：检查本地版本与远端版本
    /// </summary>
    private static async Task<(bool success, VersionState remoteState, string remoteJson, VersionState localState)> StepCheckVersionAsync(BuildIndexData buildIndex)
    {
        // 4. 加载本地 version_state.json (从 CurrentGUIDRoot)
        BeginStep("Check local version", 3);
        string localVersionStatePath = Path.Combine(PathManager.CurrentGUIDRoot, "version_state.json");
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
            return (false, null, null, null);
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
                return (false, null, null, null);
            }
        }
        Debug.Log($"[HotfixManager] 此次需下载Bundle数: {remoteVersionState.bundles.Count}, 总大小: {remoteVersionState.totalSize}");
        CompleteStep();
        return (true, remoteVersionState, remoteVersionJson, localVersionState);
    }

    /// <summary>
    /// 步骤5：下载所有的远端 bundle (支持同名文件Copy优化)
    /// </summary>
    private static async Task<bool> StepDownloadBundlesAsync(VersionState remoteVersionState, VersionState localVersionState)
    {
        BeginStep("Download bundles", 4);

        // 清理旧的 Build_xxxx 包体目录 (保留最近1个 + 当前正在用的 = 2个? 这里的CleanOldBuildPackages会自动避开 CurrentGUIDRoot)
        PackageCleaner.CleanOldBuildPackages(maxKeepCount: 1); 

        // 目标 Bundles 目录
        string targetGUIDRoot = Path.Combine(PathManager.HotfixRoot, _targetPackageName);
        string targetBundleRoot = Path.Combine(targetGUIDRoot, "bundles");
        if (!Directory.Exists(targetBundleRoot)) Directory.CreateDirectory(targetBundleRoot);
        
        int totalBundles = remoteVersionState.bundles.Count;
        int completedBundles = 0;
        int skippedBundles = 0;

        ReportStepProgress(0f);

        // 建立本地 Bundle 索引 (Hash -> BundleName)
        Dictionary<string, string> localBundleMap = new Dictionary<string, string>();
        if (localVersionState != null && localVersionState.bundles != null)
        {
            foreach (var b in localVersionState.bundles)
            {
                if(!localBundleMap.ContainsKey(b.hash))
                    localBundleMap[b.hash] = b.bundleName;
            }
        }

        var task = new List<Task<bool>>();
        foreach (var bundleInfo in remoteVersionState.bundles)
        {
            string savePath = Path.Combine(targetBundleRoot, bundleInfo.bundleName);
            
            // 优化：检查本地是否有相同 Hash 的文件，直接复制
            bool copied = false;
            if (localBundleMap.TryGetValue(bundleInfo.hash, out string localName))
            {
                string localPath = Path.Combine(PathManager.CurrentGUIDRoot, "bundles", localName);
                if (File.Exists(localPath))
                {
                    try
                    {
                        File.Copy(localPath, savePath, true);
                        copied = true;
                        skippedBundles++;
                        completedBundles++;
                    }
                    catch(Exception ex) 
                    {
                        Debug.LogWarning($"[HotfixManager] 复制资源失败: {localName} -> {savePath}, 错误: {ex.Message}，将回退到下载。");
                    }
                }
            }

            if (!copied)
            {
                string bundleUrl = $"{_remoteUrlRoot}/bundles/{bundleInfo.bundleName}";
                task.Add(DownloadBundleWithTrack(bundleUrl, savePath, () =>
                {
                    completedBundles++;
                    float p = totalBundles == 0 ? 1f : (float)completedBundles / totalBundles;
                    ReportStepProgress(p);
                }));
            }
            else
            {
                 // 更新进度 (因为是同步复制，可能太快了)
                 float p = totalBundles == 0 ? 1f : (float)completedBundles / totalBundles;
                 ReportStepProgress(p);
            }
        }
        
        if (skippedBundles > 0)
        {
            Debug.Log($"[HotfixManager] 智能优化：跳过下载直接复制了 {skippedBundles} 个未改动资源。");
        }

        await Task.WhenAll(task);
        if (task.Any(t => !t.Result))
        {
            ReportError("[HotfixManager] 存在下载失败的 bundle，请检查网络！");
            return false;
        }
        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤6：下载 catalog.json
    /// </summary>
    private static async Task<bool> StepDownloadCatalogAsync(string remoteVersionJson)
    {
        BeginStep("Download catalog", 5);
        string targetGUIDRoot = Path.Combine(PathManager.HotfixRoot, _targetPackageName);
        
        string catalogUrl = $"{_remoteUrlRoot}/catalog.json";
        // 下载 catalog.json 到新目录下
        bool catalogOk = await NetworkDownloader.Instance.DownloadFile(catalogUrl, Path.Combine(targetGUIDRoot, "catalog.json"));
        if (!catalogOk)
        {
            ReportError("[HotfixManager] catalog.json 下载失败");
            return false;
        }
        // 写入 version_state.json 到新目录下
        File.WriteAllText(Path.Combine(targetGUIDRoot, "version_state.json"), remoteVersionJson);
        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤7：应用更新（保存 manifest 记录）
    /// </summary>
    private static void StepApplyUpdate(VersionState remoteVersionState)
    {
        BeginStep("Apply update", 6);
        
        // 更新本地记录的 Manifest，指向新的包体
        string manifestPath = Path.Combine(PathManager.HotfixRoot, "manifest.json");
        var manifest = new Manifest
        {
            latestPackage = _targetPackageName,
            latestversion = remoteVersionState.version
        };
        
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        Debug.Log($"[HotfixManager] 更新 Manifest 指针 -> {_targetPackageName}");
        
        // 关键：立即切换 PathManager 到新目录，确保后续 InternalIdTransformFunc 能找到正确的 bundles
        PathManager.SwitchToNewBuild(_targetPackageName);
        
        CompleteStep();
    }

    /// <summary>
    /// 步骤8：加载新的 Catalog
    /// </summary>
    private static async Task StepLoadCatalogAsync()
    {
        BeginStep("Load catalog", 7);
        Debug.Log("[HotfixManager] 加载新的远端 Catalog...");
        
        // PathManager.CurrentGUIDRoot 已在 StepApplyUpdate 中切换到新目录
        string localCatalogPath = Path.Combine(PathManager.CurrentGUIDRoot, "catalog.json");
        
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);
        if (catalogLoaded) Debug.Log("[HotfixManager] 热更流程成功完成！");
        CompleteStep();
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
    /// 使用本地文件标记
    /// </summary>
    private static void CheckAndCleanIfNewBuild(BuildIndexData currentBuildIndex)
    {
        string guidFilePath = Path.Combine(Application.persistentDataPath, "build_guid.txt");
        string lastGuid = "";
        
        // 从文件读取上次的 GUID
        if (File.Exists(guidFilePath))
        {
            try
            {
                lastGuid = File.ReadAllText(guidFilePath).Trim();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HotfixManager] 读取 build_guid.txt 失败: {ex.Message}");
            }
        }
        
        string currentGuid = currentBuildIndex.BuildGUID;

        // 如果记录的 GUID 和当前包的 GUID 不一致，说明覆盖安装了新整包
        if (lastGuid != currentGuid)
        {
            Debug.Log($"[HotfixManager] 检测到新整包覆盖 (GUID: {lastGuid} -> {currentGuid})。执行深度清理...");

            // 1. 清理 Unity AssetBundle 缓存
            Caching.ClearCache();

            // 2. 暴力删除热更下载目录
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

            // 4. 使用文件存储 GUID（替代 PlayerPrefs）
            try
            {
                File.WriteAllText(guidFilePath, currentGuid);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HotfixManager] 写入 build_guid.txt 失败: {ex.Message}");
            }
            
            Debug.Log("[HotfixManager] 清理完成，作为全新版本运行。");
        }
        else
        {
            Debug.Log($"[HotfixManager] 版本 GUID 一致 ({currentGuid})，保持热更缓存。");
        }
    }
    
    /// <summary>
    /// 从 StreamingAssets 直接读取 BuildIndex，绕过 Addressables 缓存
    /// </summary>
    private static async Task<BuildIndexData> LoadBuildIndexFromStreamingAssets()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "BuildIndex.json");
        
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android 需要使用 UnityWebRequest 读取 StreamingAssets
            using (var request = UnityWebRequest.Get(path))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    return JsonUtility.FromJson<BuildIndexData>(json);
                }
                else
                {
                    Debug.LogError($"[HotfixManager] 读取 BuildIndex 失败: {request.error}");
                    return null;
                }
            }
#else
            // 其他平台可直接读取文件
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<BuildIndexData>(json);
            }
            else
            {
                Debug.LogError($"[HotfixManager] BuildIndex.json 不存在: {path}");
                return null;
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotfixManager] 读取 BuildIndex 异常: {e.Message}");
            return null;
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

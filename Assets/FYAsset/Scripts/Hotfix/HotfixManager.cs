using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 热更管理器，控制总流程
/// </summary>
public static class HotfixManager
{
    private static string HotfixUrl => FYAssetSettings.Instance.HotfixUrl;
    private static string PackageIndexUrl => $"{HotfixUrl}{FYAssetSettings.PACKAGE_INDEX_FILE_NAME}";

    /// <summary>
    /// 当热更步骤发生改变时触发
    /// </summary>
    public static event Action<string> OnStepChanged;

    /// <summary>
    /// 当热更进度更新时触发 (0f~1f, 步骤名称)
    /// </summary>
    public static event Action<float, string> OnProgress;

    /// <summary>
    /// 当热更发生错误时触发
    /// </summary>
    public static event Action<string> OnError;

    /// <summary>
    /// 热更流程全部结束时触发
    /// </summary>
    public static event Action OnFinished;

    private const int TotalSteps = 11;
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

    /// <summary>
    /// 开始执行完整的异步热更流程
    /// </summary>
    public async static Task InitializeAsync()
    {
        _currentStepIndex = -1;
        CurrentProgressValue = 0f;

        var buildIndex = await StepLoadBuildIndexAsync();
        if (buildIndex == null) return;

        var pipeline = CreatePipeline();
        if (pipeline == null)
        {
            ReportError("[HotfixManager] 未能创建热更后端，初始化中止。");
            return;
        }

        var ctx = new HotfixContext
        {
            BuildIndex = buildIndex
        };

        if (!await StepInitializeBackendAsync(pipeline))
        {
            await FinishHotfix();
            return;
        }

        if (!await StepDownloadPackageIndexAsync(ctx))
        {
            await FinishHotfix();
            return;
        }

        var localInfo = await StepLoadLocalVersionAsync(pipeline);
        var remoteInfo = await StepFetchRemoteVersionAsync(pipeline, ctx);
        if (remoteInfo == null)
        {
            ReportError("[HotfixManager] 无法获取远端版本信息，将使用本地资源运行。");
            await FinishHotfix();
            return;
        }

        if (!await StepCompareVersionAsync(buildIndex, localInfo, remoteInfo)) return;

        var downloadList = StepGetBundleDownloadList(pipeline, remoteInfo);
        if (!await StepDownloadBundlesAsync(ctx, downloadList, localInfo)) return;

        if (!await StepPostDownloadAsync(pipeline, ctx)) return;

        StepApplyUpdate(ctx, remoteInfo);

        BeginStep("Finalize", 10);
        await FinishHotfix();
        CompleteStep();
        OnFinished?.Invoke();
    }

    #region 热更流程步骤

    /// <summary>
    /// 步骤1：加载 BuildIndex 并初始化路径
    /// </summary>
    private static async Task<BuildIndexData> StepLoadBuildIndexAsync()
    {
        BeginStep("Load BuildIndex", 0);
        BuildIndexData buildIndex = await LoadBuildIndexFromStreamingAssets();
        if (buildIndex == null)
        {
            ReportError("[HotfixManager] 致命错误：无法加载 BuildIndex。");
            return null;
        }

        RuntimePathManager.Initialize(buildIndex);
        CheckAndCleanIfNewBuild(buildIndex);

        // 尝试读取已保存的 PackageIndex.json 来覆盖 GUID (断点续传/二次启动)
        // 使用 RuntimePathManager.HotfixRoot 确保读取路径与 StepApplyUpdate 的保存路径一致
        string localPackageIndexPath = Path.Combine(RuntimePathManager.HotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (FileHelper.Exists(localPackageIndexPath))
        {
            try
            {
                var localPackageIndex = SerializationUtility.ReadFromFile<PackageIndex>(localPackageIndexPath);
                if (!string.IsNullOrEmpty(localPackageIndex.LatestPackage))
                {
                    Debug.Log($"[HotfixManager] 发现本地热更记录，重定向至: {localPackageIndex.LatestPackage}");
                    buildIndex.BuildGUID = localPackageIndex.LatestPackage;

                    // 第二次初始化：应用新的 BuildGUID，将 CurrentGUIDRoot 修正为热更包目录
                    RuntimePathManager.Initialize(buildIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HotfixManager] 本地 PackageIndex 读取失败: {ex.Message}");
            }
        }

        RuntimePathManager.EnsureDirectories();
        CompleteStep();
        return buildIndex;
    }

    /// <summary>
    /// 步骤2：初始化热更后端
    /// </summary>
    private static async Task<bool> StepInitializeBackendAsync(IHotfixPipeline pipeline)
    {
        BeginStep("Initialize backend", 1);
        var initResult = await pipeline.InitializeBackendAsync();
        if (!initResult.Success)
        {
            ReportError(initResult.Error != null ? initResult.Error.ToString() : "[HotfixManager] 热更后端初始化失败");
            return false;
        }

        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤3：下载包体索引文件(PackageIndex.json)
    /// </summary>
    private static async Task<bool> StepDownloadPackageIndexAsync(HotfixContext ctx)
    {
        BeginStep("Download PackageIndex", 2);
        string packageIndexJson = await NetworkDownloader.DownloadText(PackageIndexUrl);
        if (string.IsNullOrEmpty(packageIndexJson))
        {
            ReportError("[HotfixManager] 无法获取 PackageIndex.json，使用本地资源运行。");
            CompleteStep();
            return false;
        }

        PackageIndex packageIndex = SerializationUtility.DeserializeJson<PackageIndex>(packageIndexJson);
        if (string.IsNullOrEmpty(packageIndex.LatestPackage))
        {
            ReportError("[HotfixManager] PackageIndex.json 无效，使用本地资源运行。");
            CompleteStep();
            return false;
        }

        ctx.TargetPackageName = packageIndex.LatestPackage;
        ctx.RemoteUrlRoot = $"{HotfixUrl}/{FYAssetSettings.Instance.BuildPackagesFolderName}/{ctx.TargetPackageName}";
        ctx.TargetGUIDRoot = Path.Combine(RuntimePathManager.HotfixRoot, ctx.TargetPackageName);

        Debug.Log($"[HotfixManager] 获取最新包体: {ctx.TargetPackageName}，URL已更新: {ctx.RemoteUrlRoot}");
        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤4：加载本地热更版本信息
    /// </summary>
    private static async Task<HotfixVersionInfo> StepLoadLocalVersionAsync(IHotfixPipeline pipeline)
    {
        BeginStep("Load local version", 3);
        var localInfo = await pipeline.LoadLocalVersionAsync(RuntimePathManager.CurrentGUIDRoot);
        CompleteStep();
        return localInfo;
    }

    /// <summary>
    /// 步骤5：获取远端热更版本信息
    /// </summary>
    private static async Task<HotfixVersionInfo> StepFetchRemoteVersionAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        BeginStep("Fetch remote version", 4);
        var remoteInfo = await pipeline.FetchRemoteVersionAsync(ctx.RemoteUrlRoot);
        CompleteStep();
        return remoteInfo;
    }

    /// <summary>
    /// 步骤6：版本比对
    /// </summary>
    private static Task<bool> StepCompareVersionAsync(
        BuildIndexData buildIndex,
        HotfixVersionInfo localInfo,
        HotfixVersionInfo remoteInfo)
    {
        BeginStep("Compare version", 5);

        if (localInfo != null && localInfo.Version != null && remoteInfo.Version != null &&
            localInfo.Version.Major != remoteInfo.Version.Major)
        {
            if (buildIndex.Version == remoteInfo.Version)
            {
                Debug.Log($"[HotfixManager] 检测到大版本更新，执行全量清理。版本：{buildIndex.Version.GetVersionString()}");
                PackageCleaner.ClearAllHotfix();
            }
            else
            {
                ReportError($"[HotfixManager] 检测到整包版本不一致，请下载最新整包。本地版本:{buildIndex.Version.GetVersionString()}, 远端版本:{remoteInfo.Version.GetVersionString()}");
                return Task.FromResult(false);
            }
        }

        Debug.Log($"[HotfixManager] 远端版本: {remoteInfo.Version?.GetVersionString()}");
        Debug.Log($"[HotfixManager] 此次需下载Bundle数: {remoteInfo.BundleCount}, 总大小: {remoteInfo.TotalSize}");
        CompleteStep();
        return Task.FromResult(true);
    }

    /// <summary>
    /// 步骤7：获取需要下载的 Bundle 列表
    /// </summary>
    private static IReadOnlyList<BundleDownloadItem> StepGetBundleDownloadList(IHotfixPipeline pipeline,
        HotfixVersionInfo remoteInfo)
    {
        BeginStep("Prepare download list", 6);
        var downloadList = pipeline.GetBundleDownloadList(remoteInfo) ?? Array.Empty<BundleDownloadItem>();
        CompleteStep();
        return downloadList;
    }

    /// <summary>
    /// 步骤8：下载所有的远端 bundle (支持同名文件Copy优化)
    /// </summary>
    private static async Task<bool> StepDownloadBundlesAsync(
        HotfixContext ctx,
        IReadOnlyList<BundleDownloadItem> remoteBundles,
        HotfixVersionInfo localInfo)
    {
        BeginStep("Download bundles", 7);

        // 清理旧的 Build_xxxx 包体目录 (保留最近1个 + 当前正在用的 = 2个? 这里的CleanOldBuildPackages会自动避开 CurrentGUIDRoot)
        PackageCleaner.CleanOldBuildPackages(maxKeepCount: 1);

        // 目标 Bundles 目录
        string targetBundleRoot = Path.Combine(ctx.TargetGUIDRoot, "bundles");
        if (!FileHelper.DirectoryExists(targetBundleRoot)) FileHelper.EnsureDirectory(targetBundleRoot);
        CleanupStaleTempFiles(targetBundleRoot);

        int totalBundles = remoteBundles.Count;
        int completedBundles = 0;
        int skippedBundles = 0;

        ReportStepProgress(0f);

        // 建立本地 Bundle 索引 (Hash -> BundleName)
        Dictionary<string, string> localBundleMap = new Dictionary<string, string>();
        if (localInfo != null && localInfo.Bundles != null)
        {
            foreach (var b in localInfo.Bundles)
            {
                if (!string.IsNullOrEmpty(b.FileHash) && !localBundleMap.ContainsKey(b.FileHash))
                    localBundleMap[b.FileHash] = b.BundleName;
            }
        }

        // 并发下载控制：限制同时下载数，避免移动端网络和内存压力
        const int maxConcurrent = 6;
        var semaphore = new SemaphoreSlim(maxConcurrent);
        var tasks = new List<Task<bool>>();

        foreach (var bundleInfo in remoteBundles)
        {
            string savePath = Path.Combine(targetBundleRoot, bundleInfo.BundleName);

            // 优化：检查本地是否有相同 Hash 的文件，直接复制
            bool copied = false;
            if (localBundleMap.TryGetValue(bundleInfo.FileHash, out string localName))
            {
                string localPath = Path.Combine(RuntimePathManager.CurrentGUIDRoot, "bundles", localName);
                if (FileHelper.Exists(localPath))
                {
                    string copyTempPath = savePath + ".tmp";
                    try
                    {
                        FileHelper.TryDelete(copyTempPath);
                        FileHelper.CopyFile(localPath, copyTempPath);
                        if (VerifyBundleCRC(copyTempPath, bundleInfo))
                        {
                            FileHelper.ReplaceFile(copyTempPath, savePath);
                            copied = true;
                            Interlocked.Increment(ref skippedBundles);
                            int done = Interlocked.Increment(ref completedBundles);
                            float p = totalBundles == 0 ? 1f : (float)done / totalBundles;
                            ReportStepProgress(p);
                        }
                        else
                        {
                            Debug.LogWarning($"[HotfixManager] 本地复用资源 CRC 校验失败: {localName}，将回退到下载。");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"[HotfixManager] 复制资源失败: {localName} -> {savePath}, 错误: {ex.Message}，将回退到下载。");
                    }
                    finally
                    {
                        FileHelper.TryDelete(copyTempPath);
                    }
                }
            }

            if (!copied)
            {
                string bundleUrl = $"{ctx.RemoteUrlRoot}/bundles/{bundleInfo.BundleName}";
                tasks.Add(DownloadBundleWithThrottle(semaphore, bundleUrl, savePath, bundleInfo, () =>
                {
                    int done = Interlocked.Increment(ref completedBundles);
                    float p = totalBundles == 0 ? 1f : (float)done / totalBundles;
                    ReportStepProgress(p);
                }));
            }
        }

        if (skippedBundles > 0)
        {
            Debug.Log($"[HotfixManager] 智能优化：跳过下载直接复制了 {skippedBundles} 个未改动资源。");
        }

        await Task.WhenAll(tasks);
        semaphore.Dispose();

        if (tasks.Any(t => !t.Result))
        {
            ReportError("[HotfixManager] 存在下载失败的 bundle，请检查网络！");
            return false;
        }

        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤9：后端特定的下载后处理（例如：保存版本信息、Catalog更新）
    /// </summary>
    private static async Task<bool> StepPostDownloadAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        BeginStep("Post download", 8);
        var postResult = await pipeline.PostDownloadAsync(ctx);
        if (!postResult.Success)
        {
            ReportError(postResult.Error != null ? postResult.Error.ToString() : "[HotfixManager] 热更后处理失败");
            return false;
        }

        CompleteStep();
        return true;
    }

    /// <summary>
    /// 步骤10：应用更新（保存 PackageIndex 记录）
    /// </summary>
    private static void StepApplyUpdate(HotfixContext ctx, HotfixVersionInfo remoteVersionInfo)
    {
        BeginStep("Apply update", 9);

        // 更新本地记录的 PackageIndex，指向新的包体
        string packageIndexPath = Path.Combine(RuntimePathManager.HotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        var packageIndex = new PackageIndex
        {
            LatestPackage = ctx.TargetPackageName,
            LatestVersion = remoteVersionInfo.Version
        };

        SerializationUtility.WriteToFile(packageIndexPath, packageIndex);
        Debug.Log($"[HotfixManager] 更新 PackageIndex 指针 -> {ctx.TargetPackageName}");

        // 关键：立即切换 RuntimePathManager 到新目录，确保后续 InternalIdTransformFunc 能找到正确的 bundles
        RuntimePathManager.SwitchToNewBuild(ctx.TargetPackageName);

        CompleteStep();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 下载单个 Bundle 的辅助方法，包含并发控制和 CRC 校验
    /// </summary>
    private static async Task<bool> DownloadBundleWithThrottle(
        SemaphoreSlim semaphore,
        string url,
        string savePath,
        BundleDownloadItem bundleInfo,
        Action onDone)
    {
        await semaphore.WaitAsync();
        try
        {
            return await DownloadBundleWithRetry(url, savePath, bundleInfo, onDone);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<bool> DownloadBundleWithRetry(
        string url,
        string savePath,
        BundleDownloadItem bundleInfo,
        Action onDone)
    {
        int maxRetries = Mathf.Max(0, FYAssetSettings.Instance.HotfixMaxRetryCount);
        int totalAttempts = maxRetries + 1;
        float baseDelaySeconds = Mathf.Max(0f, FYAssetSettings.Instance.HotfixRetryBaseDelaySeconds);
        string tempPath = savePath + ".tmp";

        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            FileHelper.TryDelete(tempPath);

            bool downloaded = await NetworkDownloader.DownloadFileOnce(url, tempPath);
            bool verified = downloaded && VerifyBundleCRC(tempPath, bundleInfo);
            if (verified)
            {
                try
                {
                    FileHelper.ReplaceFile(tempPath, savePath);
                    onDone?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HotfixManager] Bundle 替换失败: {bundleInfo.BundleName}, attempt={attempt}/{totalAttempts}, error={ex.Message}");
                }
            }
            else if (downloaded)
            {
                Debug.LogWarning($"[HotfixManager] Bundle 校验失败，将按下载失败重试: {bundleInfo.BundleName}, attempt={attempt}/{totalAttempts}");
            }
            else
            {
                Debug.LogWarning($"[HotfixManager] Bundle 下载失败: {bundleInfo.BundleName}, attempt={attempt}/{totalAttempts}");
            }

            FileHelper.TryDelete(tempPath);

            if (attempt < totalAttempts && baseDelaySeconds > 0f)
            {
                int delayMs = Mathf.RoundToInt(baseDelaySeconds * 1000f * Mathf.Pow(2f, attempt - 1));
                await Task.Delay(delayMs);
            }
        }

        Debug.LogWarning($"[HotfixManager] Bundle 下载失败且重试已耗尽: {bundleInfo.BundleName}");
        onDone?.Invoke();
        return false;
    }

    /// <summary>
    /// 验证下载完成的 Bundle 文件的 CRC 是否正确
    /// </summary>
    private static bool VerifyBundleCRC(string path, BundleDownloadItem bundleInfo)
    {
        if (bundleInfo.FileCRC == 0)
        {
            Debug.LogWarning($"[HotfixManager] Bundle CRC 为 0，跳过校验: {bundleInfo.BundleName}");
            return true;
        }

        if (!FileHelper.Exists(path))
        {
            Debug.LogWarning($"[HotfixManager] Bundle 文件不存在，无法 CRC 校验: {path}");
            return false;
        }

        uint actualCrc = HashGenerator.GenerateFileCRC(path);
        if (actualCrc == bundleInfo.FileCRC)
            return true;

        Debug.LogWarning(
            $"[HotfixManager] Bundle CRC 校验失败: {bundleInfo.BundleName}, expected={bundleInfo.FileCRC:X8}, actual={actualCrc:X8}");
        return false;
    }

    private static void CleanupStaleTempFiles(string bundleRoot)
    {
        string[] tempFiles = FileHelper.GetFiles(bundleRoot, "*.tmp");
        for (int i = 0; i < tempFiles.Length; i++)
            FileHelper.TryDelete(tempFiles[i]);

        if (tempFiles.Length > 0)
            Debug.LogWarning($"[HotfixManager] 已清理残留临时 Bundle 文件: {tempFiles.Length}");
    }

    /// <summary>
    /// 热更流程收尾：触发回调，初始化 AssetPackageManager 和 LuaEnv
    /// </summary>
    private static async Task FinishHotfix()
    {
        await AssetPackageManager.Instance.Initialize();
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
        if (FileHelper.Exists(guidFilePath))
        {
            try
            {
                lastGuid = FileHelper.ReadAllText(guidFilePath).Trim();
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
            catch (Exception ex)
            {
                Debug.LogWarning(ex.Message);
            }

            // 3. 清理 Addressables 内部缓存 (Catalog 缓存)
            string aaCachePath = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
            try
            {
                if (FileHelper.DirectoryExists(aaCachePath))
                {
                    FileHelper.TryDeleteDirectory(aaCachePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex.Message);
            }

            // 4. 使用文件存储 GUID（替代 PlayerPrefs）
            try
            {
                FileHelper.WriteAllTextAtomic(guidFilePath, currentGuid);
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
            string json = await FileHelper.ReadAllTextAsync(path);
            return SerializationUtility.DeserializeJson<BuildIndexData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotfixManager] 读取 BuildIndex 失败: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 根据全局开关创建对应的热更后端接口实现
    /// </summary>
    private static IHotfixPipeline CreatePipeline()
    {
        return FYAssetSettings.Instance.UseABBackend
            ? new ABHotfixBackend()
            : new AAHotfixBackend();
    }

    #endregion
}

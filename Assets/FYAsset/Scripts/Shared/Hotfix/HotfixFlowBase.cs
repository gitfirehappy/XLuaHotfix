using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AA 与 AB 共用的确定性热更启动状态机。
/// </summary>
public abstract class HotfixFlowBase
{
    protected abstract string HotfixUrl { get; }
    protected abstract string BackendModeName { get; }
    protected abstract int HotfixMaxRetryCount { get; }
    protected abstract float HotfixRetryBaseDelaySeconds { get; }
    protected abstract int HotfixMetadataTimeoutSeconds { get; }
    protected abstract int HotfixBundleTimeoutSeconds { get; }

    private string PackageIndexUrl => FYAssetPathUtility.JoinUrl(
        HotfixUrl,
        FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    public event Action<string> OnStepChanged;
    public event Action<float, string> OnProgress;
    public event Action<string> OnWarning;
    public event Action<string> OnError;
    public event Action<ClientUpdateRequiredInfo> OnClientUpdateRequired;
    public event Action OnFinished;

    private readonly string[] _stepNames =
    {
        "加载 BuildIndex",
        "初始化后端",
        "加载本地版本",
        "下载 PackageIndex",
        "比较版本",
        "获取远端版本",
        "准备下载列表",
        "下载 Bundle",
        "处理下载结果",
        "应用更新",
        "完成初始化"
    };

    private int _currentStepIndex = -1;
    private string _currentStepName = string.Empty;
    private bool _finishedRaised;

    public string CurrentStepName => _currentStepName;
    public float CurrentProgressValue { get; private set; }

    #region 主流程

    /// <summary>
    /// 初始化热更流程，并将未处理异常统一转换为致命错误。
    /// </summary>
    public async Task InitializeAsync()
    {
        _currentStepIndex = -1;
        CurrentProgressValue = 0f;
        _finishedRaised = false;

        try
        {
            await RunAsync();
        }
        catch (HotfixFatalException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ThrowFatal($"[HotfixManager] 热更启动发生未预期异常：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 按状态决策执行本地激活、修复、更新或回退流程。
    /// </summary>
    private async Task RunAsync()
    {
        var ctx = new HotfixContext();
        await LoadStartupStateAsync(ctx);

        if (IsStandaloneMode())
        {
            // 离线包：跳过联网步骤，直接完成 PackageManager 初始化与绑定
            await FinalizeAsync();
            return;
        }

        IHotfixPipeline pipeline = CreatePipeline();
        if (pipeline == null)
            ThrowFatal("[HotfixManager] 热更后端创建失败。");

        await InitializeBackendAsync(pipeline);
        await InspectBaselinePackageAsync(pipeline, ctx);
        await InspectLocalPackageAsync(pipeline, ctx);

        PackageIndex remoteIndex = await DownloadRemotePackageIndexAsync();
        if (remoteIndex == null)
        {
            await HandleRemoteFailureAsync(
                pipeline,
                ctx,
                "[HotfixManager] 远端 PackageIndex 不可用，继续使用本地内容。");
            return;
        }

        ctx.RemotePackageIndex = remoteIndex;
        ConfigureRemoteTarget(ctx);

        if (IsMajorMismatch(ctx.BuildIndex, remoteIndex))
        {
            await HandleMajorMismatchAsync(pipeline, ctx);
            return;
        }

        BeginStep("比较版本");
        HotfixStateDecision decision = HotfixStateDecider.DecideTarget(
            ctx.LocalPackageIndex.LatestPackage,
            ctx.LocalPackageIndex.LatestVersion,
            ctx.LocalPackageInspection?.IsComplete == true,
            ctx.LocalIsBaseline,
            remoteIndex.LatestPackage,
            remoteIndex.LatestVersion);
        CompleteStep();

        switch (decision.Action)
        {
            case HotfixStateAction.ActivateLocal:
                Debug.Log($"[HotfixManager] 本地包已验证：{remoteIndex.LatestPackage}，跳过远端 manifest。");
                await ActivateLocalAndFinalizeAsync(pipeline, ctx);
                return;
            case HotfixStateAction.RepairBaselinePointer:
                Debug.Log("[HotfixManager] 内置整包完整，仅修复本地 PackageIndex。");
                await ActivateLocalAndFinalizeAsync(pipeline, ctx, remoteIndex, true);
                return;
            case HotfixStateAction.RejectRemote:
                ReportWarning("[HotfixManager] 远端目标不是同 Major 前向新包，保持当前本地内容。");
                await ActivateLocalAndFinalizeAsync(pipeline, ctx);
                return;
            case HotfixStateAction.FailStartup:
                ThrowFatal("[HotfixManager] 本地内容需要修复，但远端目标不是可接受的前向版本。");
                return;
        }

        if (decision.Action == HotfixStateAction.UpdateTarget)
            DeleteTargetPackage(ctx, "准备前向更新");

        HotfixVersionInfo remoteInfo = await FetchRemoteVersionAsync(pipeline, ctx);
        if (remoteInfo == null || remoteInfo.Version == null || remoteInfo.Version != remoteIndex.LatestVersion)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                "[HotfixManager] 远端包 manifest 不可用或与 PackageIndex 不一致。");
            return;
        }

        IReadOnlyList<BundleDownloadItem> downloadList = PrepareDownloadList(pipeline, remoteInfo);
        if (!ValidateBundleList(downloadList, out string bundleListError))
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 远端 manifest Bundle 列表无效：{bundleListError}");
            return;
        }

        HotfixPackageInspection reuseInspection = ctx.LocalPackageInspection?.IsComplete == true
            ? ctx.LocalPackageInspection
            : ctx.BaselinePackageInspection;
        string reuseRoot = ctx.LocalPackageInspection?.IsComplete == true
            ? ctx.LocalPackageRoot
            : Application.streamingAssetsPath;
        bool bundlesReady = await DownloadBundlesAsync(
            ctx,
            downloadList,
            reuseInspection?.VersionInfo,
            reuseRoot);
        if (!bundlesReady)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                "[HotfixManager] 一个或多个包内 Bundle 准备失败。");
            return;
        }

        bool refreshRequiredMetadata = !pipeline.HasRequiredMetadata(ctx.TargetGUIDRoot);
        BeginStep("处理下载结果");
        HotfixStepResult metadataResult = await pipeline.PersistRemoteMetadataAsync(
            ctx,
            MetadataOptions,
            refreshRequiredMetadata);
        if (!metadataResult.Success)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 远端元数据持久化失败：{FormatError(metadataResult)}");
            return;
        }
        CompleteStep();

        HotfixPackageInspection targetInspection = await pipeline.InspectPackageAsync(
            ctx.TargetGUIDRoot,
            remoteIndex);
        if (targetInspection == null || !targetInspection.IsComplete)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 目标包不完整：{targetInspection?.FailureReason}");
            return;
        }

        bool applied = await ApplyUpdateAsync(pipeline, ctx);
        if (!applied)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                "[HotfixManager] 目标包激活失败。");
            return;
        }

        PackageIndex packageIndexToPersist = decision.Action == HotfixStateAction.UpdateTarget
            ? ctx.RemotePackageIndex
            : null;
        await FinalizeTargetAsync(pipeline, ctx, packageIndexToPersist);
    }

    #endregion

    #region 主流程函数

    /// <summary>
    /// 加载并校验 BuildIndex，建立内置整包身份。
    /// </summary>
    private async Task LoadStartupStateAsync(HotfixContext ctx)
    {
        BeginStep("加载 BuildIndex");
        BuildIndexData buildIndex = await LoadBuildIndexFromStreamingAssets();
        if (!IsBuildIndexTrusted(buildIndex, out string error))
            ThrowFatal($"[HotfixManager] BuildIndex 缺失或无效：{error}");

        ctx.BuildIndex = buildIndex;
        ctx.BaselinePackageName = buildIndex.BuildGUID;
        ctx.BaselinePackageIndex = new PackageIndex
        {
            LatestPackage = buildIndex.BuildGUID,
            LatestVersion = buildIndex.Version,
            BackendMode = buildIndex.BackendMode
        };
        RuntimePathManager.Initialize(buildIndex);
        RuntimePathManager.EnsureDirectories();
        CompleteStep();
    }

    /// <summary>
    /// 初始化当前 AA 或 AB 热更后端
    /// </summary>
    private async Task InitializeBackendAsync(IHotfixPipeline pipeline)
    {
        BeginStep("初始化后端");
        HotfixStepResult result = await pipeline.InitializeBackendAsync();
        if (!result.Success)
            ThrowFatal($"[HotfixManager] 热更后端初始化失败：{FormatError(result)}");
        CompleteStep();
    }

    /// <summary>
    /// 严格检查内置整包。Android deferred: this iteration validates Windows file paths only.
    /// </summary>
    private async Task InspectBaselinePackageAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        ctx.BaselinePackageInspection = await pipeline.InspectPackageAsync(
            Application.streamingAssetsPath,
            ctx.BaselinePackageIndex,
            false);
        if (ctx.BaselinePackageInspection == null || !ctx.BaselinePackageInspection.IsComplete)
        {
            ThrowFatal(
                $"[HotfixManager] 内置整包损坏：{ctx.BaselinePackageInspection?.FailureReason}");
        }
    }

    /// <summary>
    /// 读取本地指针并检查其指向内容；无效指针以内置身份进入远端修复流程。
    /// </summary>
    private async Task InspectLocalPackageAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        BeginStep("加载本地版本");
        PackageIndex trustedIndex = ReadTrustedLocalPackageIndex();
        if (trustedIndex == null
            || trustedIndex.LatestVersion.Major != ctx.BuildIndex.Version.Major)
        {
            if (trustedIndex != null)
            {
                ReportWarning(
                    $"[HotfixManager] 本地 PackageIndex Major 与整包不一致，等待远端修复。整包={ctx.BuildIndex.Version.Major}，本地={trustedIndex.LatestVersion.Major}。");
            }

            ctx.LocalPackageIndex = ctx.BaselinePackageIndex;
            ctx.LocalPackageRoot = Application.streamingAssetsPath;
            ctx.LocalIsBaseline = true;
            ctx.LocalPackageInspection = HotfixPackageInspection.Incomplete(
                ctx.BaselinePackageInspection.VersionInfo,
                "本地 PackageIndex 缺失、损坏或 Major 不匹配。");
            CompleteStep();
            return;
        }

        ctx.LocalPackageIndex = trustedIndex;
        ctx.LocalIsBaseline = IsSamePackageIdentity(trustedIndex, ctx.BaselinePackageIndex);
        if (ctx.LocalIsBaseline)
        {
            ctx.LocalPackageRoot = Application.streamingAssetsPath;
            ctx.LocalPackageInspection = ctx.BaselinePackageInspection;
        }
        else
        {
            RuntimePathManager.SwitchToNewBuild(trustedIndex.LatestPackage);
            ctx.LocalPackageRoot = RuntimePathManager.CurrentGUIDRoot;
            ctx.LocalPackageInspection = await pipeline.InspectPackageAsync(
                ctx.LocalPackageRoot,
                trustedIndex);
        }

        if (!ctx.LocalPackageInspection.IsComplete)
        {
            Debug.LogWarning(
                $"[HotfixManager] 本地包不完整：{ctx.LocalPackageInspection.FailureReason}");
        }
        CompleteStep();
    }

    /// <summary>
    /// 下载并校验远端 PackageIndex。
    /// </summary>
    private async Task<PackageIndex> DownloadRemotePackageIndexAsync()
    {
        BeginStep("下载 PackageIndex");
        string json = await NetworkDownloader.DownloadText(PackageIndexUrl, MetadataOptions);
        if (string.IsNullOrEmpty(json))
        {
            CompleteStep();
            return null;
        }

        try
        {
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (!IsPackageIndexTrusted(index, out string error))
            {
                Debug.LogWarning($"[HotfixManager] 远端 PackageIndex 校验未通过：{error}");
                CompleteStep();
                return null;
            }

            CompleteStep();
            return index;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HotfixManager] 远端 PackageIndex 解析失败：{ex.Message}");
            CompleteStep();
            return null;
        }
    }

    /// <summary>
    /// 获取目标包的远端 manifest 信息。
    /// </summary>
    private async Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx)
    {
        BeginStep("获取远端版本");
        HotfixVersionInfo info = await pipeline.FetchRemoteVersionAsync(ctx.RemoteUrlRoot, MetadataOptions);
        CompleteStep();
        return info;
    }

    /// <summary>
    /// 由后端生成目标包的 Bundle 准备列表。
    /// </summary>
    private IReadOnlyList<BundleDownloadItem> PrepareDownloadList(
        IHotfixPipeline pipeline,
        HotfixVersionInfo remoteInfo)
    {
        BeginStep("准备下载列表");
        IReadOnlyList<BundleDownloadItem> list = pipeline.GetBundleDownloadList(remoteInfo)
                                                 ?? Array.Empty<BundleDownloadItem>();
        CompleteStep();
        return list;
    }

    /// <summary>
    /// 按目标目录、上一个活动包、网络的优先级准备 Bundle。
    /// </summary>
    private async Task<bool> DownloadBundlesAsync(
        HotfixContext ctx,
        IReadOnlyList<BundleDownloadItem> remoteBundles,
        HotfixVersionInfo previousPackageInfo,
        string previousPackageRoot)
    {
        BeginStep("下载 Bundle");
        try
        {
            string targetBundleRoot = FYAssetPathUtility.JoinFilePath(
                ctx.TargetGUIDRoot,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME);
            FileHelper.EnsureDirectory(targetBundleRoot);
            CleanupStaleTempFiles(targetBundleRoot);

            var previousBundleMap = BuildPreviousBundleMap(previousPackageInfo);
            int totalBundles = remoteBundles.Count;
            int completedBundles = 0;
            int reusedBundles = 0;
            var semaphore = new SemaphoreSlim(6);
            var tasks = new List<Task<bool>>();

            for (int i = 0; i < remoteBundles.Count; i++)
            {
                BundleDownloadItem bundle = remoteBundles[i];
                string savePath = FYAssetPathUtility.JoinFilePath(targetBundleRoot, bundle.BundleName);
                if (VerifyBundle(savePath, bundle))
                {
                    reusedBundles++;
                    completedBundles++;
                    ReportBundleProgress(completedBundles, totalBundles);
                    continue;
                }

                if (TryReusePreviousBundle(bundle, previousBundleMap, previousPackageRoot, savePath))
                {
                    reusedBundles++;
                    completedBundles++;
                    ReportBundleProgress(completedBundles, totalBundles);
                    continue;
                }

                string bundleUrl = FYAssetPathUtility.JoinUrl(
                    ctx.RemoteUrlRoot,
                    FYAssetSettings.BUNDLES_DIRECTORY_NAME,
                    bundle.BundleName);
                tasks.Add(DownloadBundleWithThrottle(
                    semaphore,
                    bundleUrl,
                    savePath,
                    bundle,
                    () => ReportBundleProgress(Interlocked.Increment(ref completedBundles), totalBundles)));
            }

            if (reusedBundles > 0)
                Debug.Log($"[HotfixManager] 已复用 {reusedBundles} 个完整 Bundle，无需网络下载。");

            try
            {
                bool[] results = await Task.WhenAll(tasks);
                if (results.Any(success => !success))
                    return false;
            }
            finally
            {
                semaphore.Dispose();
            }

            CompleteStep();
            return true;
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] Bundle 下载发生异常：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 激活已经完整校验的目标包。
    /// </summary>
    private async Task<bool> ApplyUpdateAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx)
    {
        BeginStep("应用更新");
        RuntimePathManager.SwitchToNewBuild(ctx.TargetPackageName);
        HotfixStepResult activation;
        try
        {
            activation = await pipeline.ActivatePackageAsync(ctx.TargetGUIDRoot);
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] 目标包激活发生异常：{ex.Message}");
            return false;
        }
        if (!activation.Success)
        {
            ReportWarning($"[HotfixManager] 目标包激活失败：{FormatError(activation)}");
            return false;
        }

        CompleteStep();
        return true;
    }

    /// <summary>
    /// 初始化运行时资源管理器，按需清理旧包并触发完成事件。
    /// </summary>
    private async Task FinalizeAsync(
        string activePackageRootToKeep = null,
        PackageIndex packageIndexToPersist = null,
        bool cleanupInactivePackages = false)
    {
        BeginStep("完成初始化");
        bool initialized = await FinishHotfix();
        if (!initialized)
            ThrowFatal("[HotfixManager] PackageManager 初始化失败。");

        if (packageIndexToPersist != null)
            PersistLocalPackageIndex(packageIndexToPersist);

        if (cleanupInactivePackages)
            CleanupInactivePackages(activePackageRootToKeep);

        CompleteInitialization();
    }

    /// <summary>
    /// 完成目标包事务；初始化失败时删除目标并回退到此前完整内容。
    /// </summary>
    private async Task FinalizeTargetAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        PackageIndex packageIndexToPersist)
    {
        BeginStep("完成初始化");
        bool packageManagerInitialized = false;
        try
        {
            packageManagerInitialized = await FinishHotfix();
            if (!packageManagerInitialized)
                throw new InvalidOperationException("PackageManager 初始化返回 false。");

            if (packageIndexToPersist != null)
                PersistLocalPackageIndex(packageIndexToPersist);

            CleanupInactivePackages(ctx.TargetGUIDRoot);
            CompleteInitialization();
        }
        catch (HotfixFatalException) when (!HotfixStateDecider.ShouldDeleteFailedTarget(packageManagerInitialized))
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!HotfixStateDecider.ShouldDeleteFailedTarget(packageManagerInitialized))
                throw;

        // ponytail: 仅保留一个 phase bit；PackageManager 支持反初始化时再添加 rollback。
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 目标 PackageManager 初始化失败：{ex.Message}");
        }
    }

    private void CompleteInitialization()
    {
        RuntimeMessage bindError = BindPackageManager();
        if (bindError != null)
            ThrowFatal($"[HotfixManager] 后端绑定失败：{bindError}");

        CompleteStep();

        if (_finishedRaised)
            return;
        _finishedRaised = true;
        OnFinished?.Invoke();
    }

    #endregion

    #region 辅助函数

    /// <summary>
    /// 根据远端 PackageIndex 固定目标包路径与下载地址。
    /// </summary>
    private void ConfigureRemoteTarget(HotfixContext ctx)
    {
        ctx.TargetPackageName = ctx.RemotePackageIndex.LatestPackage;
        ctx.RemoteUrlRoot = FYAssetPathUtility.JoinUrl(
            HotfixUrl,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            ctx.TargetPackageName);
        ctx.TargetGUIDRoot = FYAssetPathUtility.JoinFilePath(
            RuntimePathManager.HotfixRoot,
            ctx.TargetPackageName);
    }

    private bool IsBuildIndexTrusted(BuildIndexData buildIndex, out string error)
    {
        if (buildIndex == null
            || !HotfixPackageValidator.IsVersionValid(buildIndex.Version)
            || !HotfixPackageValidator.IsPackageName(buildIndex.BuildGUID)
            || !HotfixPackageValidator.IsSafePathSegment(buildIndex.Platform))
        {
            error = "Version、BuildGUID 或 Platform 无效。";
            return false;
        }
        if (string.IsNullOrEmpty(buildIndex.BackendMode)
            || !string.Equals(buildIndex.BackendMode, BackendModeName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"BackendMode 不匹配。预期={BackendModeName}，实际={buildIndex.BackendMode}。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 远端失败时只允许启动此前已验证且拥有可信指针的本地内容。
    /// </summary>
    private async Task HandleRemoteFailureAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        string warning)
    {
        ReportWarning(warning);
        HotfixStateDecision decision = HotfixStateDecider.DecideRemoteFailure(
            ctx.LocalPackageInspection?.IsComplete == true);
        if (decision.Action == HotfixStateAction.FailStartup)
            ThrowFatal(warning);

        await ActivateLocalAndFinalizeAsync(pipeline, ctx);
    }

    /// <summary>
    /// 删除失败目标，再按固定远端失败规则回退或阻断。
    /// </summary>
    private async Task HandleTargetFailureAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        string warning)
    {
        DeleteTargetPackage(ctx, "目标准备失败");
        await HandleRemoteFailureAsync(
            pipeline,
            ctx,
            warning);
    }

    /// <summary>
    /// 处理客户端与远端包的 Major 版本不匹配。
    /// </summary>
    private async Task HandleMajorMismatchAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        int clientMajor = ctx.BuildIndex.Version.Major;
        int remoteMajor = ctx.RemotePackageIndex.LatestVersion.Major;
        HotfixStateDecision decision = HotfixStateDecider.DecideMajorMismatch(
            clientMajor,
            remoteMajor,
            ctx.LocalPackageInspection?.IsComplete == true);
        if (decision.NotifyClientUpdate)
        {
            OnClientUpdateRequired?.Invoke(new ClientUpdateRequiredInfo(
                ctx.BuildIndex.Version,
                ctx.RemotePackageIndex.LatestVersion,
                ctx.RemotePackageIndex.LatestPackage));
        }

        string message = remoteMajor > clientMajor
            ? $"[HotfixManager] 远端 Major 更高，跳过热更并继续当前客户端内容。客户端={clientMajor}，远端={remoteMajor}。"
            : $"[HotfixManager] 远端 Major 低于客户端，可能存在发布或 Channel 配置异常。客户端={clientMajor}，远端={remoteMajor}。";
        ReportWarning(message);
        if (decision.Action == HotfixStateAction.FailStartup)
            ThrowFatal(message);

        await ActivateLocalAndFinalizeAsync(pipeline, ctx);
    }

    /// <summary>
    /// 激活此前已验证的本地内容并完成初始化。
    /// </summary>
    private async Task ActivateLocalAndFinalizeAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        PackageIndex packageIndexToPersist = null,
        bool cleanupInactivePackages = false)
    {
        if (ctx.LocalIsBaseline)
        {
            RuntimePathManager.SwitchToNewBuild(ctx.BaselinePackageName);
            HotfixStepResult baselineActivation = await pipeline.ActivatePackageAsync(
                Application.streamingAssetsPath);
            if (!baselineActivation.Success)
            {
                ThrowFatal(
                    $"[HotfixManager] 内置整包激活失败：{FormatError(baselineActivation)}");
            }
        }
        else
        {
            RuntimePathManager.SwitchToNewBuild(ctx.LocalPackageIndex.LatestPackage);
            HotfixStepResult activation = await pipeline.ActivatePackageAsync(ctx.LocalPackageRoot);
            if (!activation.Success)
                ThrowFatal($"[HotfixManager] 本地包激活失败：{FormatError(activation)}");
        }

        RuntimePathManager.EnsureDirectories();
        await FinalizeAsync(null, packageIndexToPersist, cleanupInactivePackages);
    }

    /// <summary>
    /// 创建当前模式对应的热更后端。
    /// </summary>
    protected abstract IHotfixPipeline CreatePipeline();

    /// <summary>
    /// 返回 true 表示当前运行为单机离线模式，跳过所有热更联网步骤。
    /// 子类按需 override；默认为 false（在线模式）。
    /// </summary>
    protected virtual bool IsStandaloneMode() => false;

    /// <summary>
    /// 完成 AA 或 AB 运行时资源管理器初始化。
    /// </summary>
    protected abstract Task<bool> FinishHotfix();

    /// <summary>
    /// 包持久化与清理成功后的绑定钩子（跨后端互斥绑定属 Compat facade 职责；默认 no-op）。
    /// </summary>
    /// <summary>
    /// 后端绑定校验钩子。跨后端"仅绑定一个 backend"互斥检查属于 Compat facade 的职责；
    /// 单后端导出集中无此语义，默认返回 null（成功）。后端有自有绑定时可覆写。
    /// </summary>
    protected virtual RuntimeMessage BindPackageManager()
    {
        return null;
    }

    /// <summary>
    /// 在运行时初始化成功后持久化新的本地活动包指针。
    /// </summary>
    private void PersistLocalPackageIndex(PackageIndex packageIndex)
    {
        try
        {
            string indexPath = FYAssetPathUtility.JoinFilePath(
                RuntimePathManager.HotfixRoot,
                FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
            string json = SerializationUtility.SerializeToJson(packageIndex, true);
            FileHelper.WriteAllTextAtomic(indexPath, json);
        }
        catch (Exception ex)
        {
            ThrowFatal($"[HotfixManager] 本地 PackageIndex 持久化失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 读取并校验本地活动包指针。
    /// </summary>
    private PackageIndex ReadTrustedLocalPackageIndex()
    {
        string path = FYAssetPathUtility.JoinFilePath(
            RuntimePathManager.HotfixRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (!FileHelper.Exists(path))
            return null;

        try
        {
            PackageIndex index = SerializationUtility.ReadFromFile<PackageIndex>(path);
            if (IsPackageIndexTrusted(index, out string error))
                return index;
            Debug.LogWarning($"[HotfixManager] 本地 PackageIndex 校验未通过：{error}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HotfixManager] 本地 PackageIndex 读取失败：{ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 校验 PackageIndex 的必要字段与 BackendMode。
    /// </summary>
    private bool IsPackageIndexTrusted(PackageIndex index, out string error)
    {
        if (index == null
            || !HotfixPackageValidator.IsPackageName(index.LatestPackage)
            || !HotfixPackageValidator.IsVersionValid(index.LatestVersion))
        {
            error = "LatestPackage 或 LatestVersion 无效。";
            return false;
        }
        if (string.IsNullOrEmpty(index.BackendMode))
        {
            error = $"BackendMode 无效：{index.BackendMode}";
            return false;
        }
        if (!string.Equals(index.BackendMode, BackendModeName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"BackendMode 不匹配。预期={BackendModeName}，实际={index.BackendMode}。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSamePackageIdentity(PackageIndex left, PackageIndex right)
    {
        return left != null
               && right != null
               && string.Equals(left.LatestPackage, right.LatestPackage, StringComparison.Ordinal)
               && left.LatestVersion == right.LatestVersion;
    }

    private static bool ValidateBundleList(
        IReadOnlyList<BundleDownloadItem> bundles,
        out string error)
    {
        if (bundles == null)
        {
            error = "Bundle 列表为空。";
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bundles.Count; i++)
        {
            BundleDownloadItem bundle = bundles[i];
            if (!HotfixPackageValidator.IsBundleMetadataValid(
                    bundle.BundleName,
                    bundle.FileSize,
                    bundle.FileCRC))
            {
                error = $"索引 {i} 的 Bundle 元数据无效。";
                return false;
            }
            if (!names.Add(bundle.BundleName))
            {
                error = $"Bundle 名称重复：{bundle.BundleName}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 判断远端包是否要求不同的客户端 Major 版本。
    /// </summary>
    private static bool IsMajorMismatch(BuildIndexData buildIndex, PackageIndex remoteIndex)
    {
        return buildIndex?.Version != null
               && remoteIndex?.LatestVersion != null
               && buildIndex.Version.Major != remoteIndex.LatestVersion.Major;
    }

    /// <summary>
    /// 建立上一个活动包的 Hash 到 BundleName 索引。
    /// </summary>
    private static Dictionary<string, string> BuildPreviousBundleMap(HotfixVersionInfo previousPackageInfo)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (previousPackageInfo?.Bundles == null)
            return map;

        for (int i = 0; i < previousPackageInfo.Bundles.Count; i++)
        {
            BundleDownloadItem bundle = previousPackageInfo.Bundles[i];
            if (HotfixPackageValidator.IsBundleMetadataValid(
                    bundle.BundleName,
                    bundle.FileSize,
                    bundle.FileCRC)
                && !string.IsNullOrEmpty(bundle.FileHash)
                && !map.ContainsKey(bundle.FileHash))
            {
                map[bundle.FileHash] = bundle.BundleName;
            }
        }
        return map;
    }

    /// <summary>
    /// 从上一个活动包复制并校验同 Hash Bundle。
    /// </summary>
    private bool TryReusePreviousBundle(
        BundleDownloadItem bundle,
        Dictionary<string, string> previousBundleMap,
        string previousPackageRoot,
        string savePath)
    {
        if (string.IsNullOrEmpty(bundle.FileHash)
            || string.IsNullOrEmpty(previousPackageRoot)
            || !previousBundleMap.TryGetValue(bundle.FileHash, out string previousBundleName))
        {
            return false;
        }
        if (!HotfixPackageValidator.IsSafePathSegment(previousBundleName))
            return false;

        string previousBundlePath = FYAssetPathUtility.JoinFilePath(
            previousPackageRoot,
            FYAssetSettings.BUNDLES_DIRECTORY_NAME,
            previousBundleName);
        if (FYAssetPathUtility.AreSamePath(previousBundlePath, savePath)
            || !IsFileSizeValid(previousBundlePath, bundle))
            return false;

        string tempPath = savePath + ".tmp";
        try
        {
            FileHelper.TryDelete(tempPath);
            FileHelper.CopyFile(previousBundlePath, tempPath);
            if (!VerifyBundle(tempPath, bundle))
                return false;
            FileHelper.ReplaceFile(tempPath, savePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[HotfixManager] 上一个包的 Bundle 复用失败：{previousBundleName}，错误={ex.Message}");
            return false;
        }
        finally
        {
            FileHelper.TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 在并发限制内下载单个 Bundle。
    /// </summary>
    private async Task<bool> DownloadBundleWithThrottle(
        SemaphoreSlim semaphore,
        string url,
        string savePath,
        BundleDownloadItem bundle,
        Action onDone)
    {
        await semaphore.WaitAsync();
        try
        {
            return await DownloadBundleWithRetry(url, savePath, bundle, onDone);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 下载并校验 Bundle，失败时按配置重试。
    /// </summary>
    private async Task<bool> DownloadBundleWithRetry(
        string url,
        string savePath,
        BundleDownloadItem bundle,
        Action onDone)
    {
        int totalAttempts = BundleOptions.MaxRetryCount + 1;
        string tempPath = savePath + ".tmp";
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            FileHelper.TryDelete(tempPath);
            bool downloaded = await NetworkDownloader.DownloadFileOnce(url, tempPath, BundleOptions);
            if (downloaded && VerifyBundle(tempPath, bundle))
            {
                FileHelper.ReplaceFile(tempPath, savePath);
                onDone?.Invoke();
                return true;
            }

            FileHelper.TryDelete(tempPath);
            if (attempt < totalAttempts && BundleOptions.RetryBaseDelaySeconds > 0f)
            {
                int delayMs = Mathf.RoundToInt(
                    BundleOptions.RetryBaseDelaySeconds * 1000f * Mathf.Pow(2f, attempt - 1));
                await Task.Delay(delayMs);
            }
        }

        onDone?.Invoke();
        Debug.LogWarning($"[HotfixManager] Bundle 重试后仍下载失败：{bundle.BundleName}");
        return false;
    }

    /// <summary>
    /// 校验 Bundle 文件大小与 CRC。
    /// </summary>
    private static bool VerifyBundle(string path, BundleDownloadItem bundle)
    {
        if (!HotfixPackageValidator.IsBundleMetadataValid(
                bundle.BundleName,
                bundle.FileSize,
                bundle.FileCRC)
            || !IsFileSizeValid(path, bundle))
            return false;
        return HashGenerator.GenerateFileCRC(path) == bundle.FileCRC;
    }

    /// <summary>
    /// 校验 Bundle 文件是否存在且大小匹配。
    /// </summary>
    private static bool IsFileSizeValid(string path, BundleDownloadItem bundle)
    {
        if (!FileHelper.Exists(path))
            return false;
        return bundle.FileSize >= 0
               && new FileInfo(path).Length == bundle.FileSize;
    }

    /// <summary>
    /// 上报 Bundle 准备阶段进度。
    /// </summary>
    private void ReportBundleProgress(int completed, int total)
    {
        ReportStepProgress(total == 0 ? 1f : (float)completed / total);
    }

    /// <summary>
    /// 清理目标目录中上次中断遗留的临时文件。
    /// </summary>
    private void CleanupStaleTempFiles(string bundleRoot)
    {
        string[] tempFiles = FileHelper.GetFiles(bundleRoot, "*.tmp");
        for (int i = 0; i < tempFiles.Length; i++)
            FileHelper.TryDelete(tempFiles[i]);
    }

    private HotfixDownloadOptions MetadataOptions => new(
        HotfixMaxRetryCount,
        HotfixRetryBaseDelaySeconds,
        HotfixMetadataTimeoutSeconds);

    private HotfixDownloadOptions BundleOptions => new(
        HotfixMaxRetryCount,
        HotfixRetryBaseDelaySeconds,
        HotfixBundleTimeoutSeconds);

    /// <summary>
    /// 开始一个热更步骤并重置步骤进度。
    /// </summary>
    private void BeginStep(string stepName)
    {
        _currentStepName = stepName ?? string.Empty;
        _currentStepIndex = Array.IndexOf(_stepNames, _currentStepName);
        if (_currentStepIndex < 0)
            _currentStepIndex = 0;
        OnStepChanged?.Invoke(_currentStepName);
        ReportStepProgress(0f);
    }

    /// <summary>
    /// 将当前步骤标记为完成。
    /// </summary>
    private void CompleteStep()
    {
        ReportStepProgress(1f);
    }

    /// <summary>
    /// 将步骤进度换算为全流程进度并上报。
    /// </summary>
    private void ReportStepProgress(float stepProgress)
    {
        float clamped = Mathf.Clamp01(stepProgress);
        float overall = Mathf.Clamp01((_currentStepIndex + clamped) / _stepNames.Length);
        CurrentProgressValue = overall;
        OnProgress?.Invoke(overall, _currentStepName);
    }

    /// <summary>
    /// 记录并广播可恢复警告。
    /// </summary>
    private void ReportWarning(string message)
    {
        OnWarning?.Invoke(message);
        Debug.LogWarning(message);
    }

    /// <summary>
    /// 记录并广播致命错误。
    /// </summary>
    private void ReportError(string message)
    {
        OnError?.Invoke(message);
        Debug.LogError(message);
    }

    /// <summary>
    /// 广播错误并抛出统一的热更致命异常。
    /// </summary>
    private void ThrowFatal(string message, Exception innerException = null)
    {
        ReportError(message);
        if (innerException == null)
            throw new HotfixFatalException(message);
        throw new HotfixFatalException(message, innerException);
    }

    /// <summary>
    /// 合并步骤错误消息与异常信息
    /// </summary>
    private static string FormatError(HotfixStepResult result)
    {
        return result.Error != null ? result.Error.ToString() : "未知错误";
    }

    /// <summary>
    /// 删除本次目标目录。拒绝删除 HotfixRoot 以外或非直接子级路径。
    /// </summary>
    private void DeleteTargetPackage(HotfixContext ctx, string reason)
    {
        if (ctx == null || !IsDirectPackageRoot(ctx.TargetGUIDRoot))
            ThrowFatal($"[HotfixManager] 拒绝删除不安全的目标目录：{ctx?.TargetGUIDRoot}");
        if (!FileHelper.DirectoryExists(ctx.TargetGUIDRoot))
            return;
        if (!FileHelper.TryDeleteDirectory(ctx.TargetGUIDRoot, true))
            ThrowFatal($"[HotfixManager] 目标目录删除失败：{ctx.TargetGUIDRoot}");

        Debug.Log($"[HotfixManager] 已删除目标目录：{ctx.TargetPackageName}，原因={reason}。");
    }

    private static bool IsDirectPackageRoot(string packageRoot)
    {
        if (string.IsNullOrEmpty(packageRoot))
            return false;

        try
        {
            string packageName = Path.GetFileName(packageRoot);
            return HotfixPackageValidator.IsPackageName(packageName)
                   && FYAssetPathUtility.AreSamePath(
                       Path.GetDirectoryName(packageRoot),
                       RuntimePathManager.HotfixRoot);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 删除 HotfixRoot 下除目标包外的直接子级 Build_* 目录；目标为空时全部删除。
    /// </summary>
    private static void CleanupInactivePackages(string activePackageRoot)
    {
        string hotfixRoot = RuntimePathManager.HotfixRoot;
        try
        {
            bool keepActive = !string.IsNullOrEmpty(activePackageRoot);
            if (keepActive && !IsDirectPackageRoot(activePackageRoot))
            {
                Debug.LogWarning($"[HotfixManager] 目标包不在 HotfixRoot 直接子级，跳过旧包清理：{activePackageRoot}");
                return;
            }

            string[] packageDirs = FileHelper.GetDirectories(hotfixRoot, "Build_*");
            int deletedCount = 0;
            long freedBytes = 0L;
            for (int i = 0; i < packageDirs.Length; i++)
            {
                string packageDir = packageDirs[i];
                if (!IsDirectPackageRoot(packageDir))
                {
                    Debug.LogWarning($"[HotfixManager] 跳过不安全的历史目录：{packageDir}");
                    continue;
                }
                if (keepActive && FYAssetPathUtility.AreSamePath(packageDir, activePackageRoot))
                    continue;

                long packageBytes = FileHelper.GetDirectorySize(packageDir);
                if (!FileHelper.TryDeleteDirectory(packageDir, true))
                    continue;

                deletedCount++;
                freedBytes += packageBytes;
                Debug.Log($"[HotfixManager] 已删除非活动包：{Path.GetFileName(packageDir)}，释放 {FileHelper.FormatBytes(packageBytes)}。");
            }

            if (deletedCount > 0)
            {
                Debug.Log($"[HotfixManager] 旧包清理完成：删除 {deletedCount} 个，释放 {FileHelper.FormatBytes(freedBytes)}。");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HotfixManager] 旧包清理失败，不影响当前包启动：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 StreamingAssets 加载内置 BuildIndex。
    /// </summary>
    private async Task<BuildIndexData> LoadBuildIndexFromStreamingAssets()
    {
        string path = FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.BUILD_INDEX_FILENAME);
        try
        {
            string json = await FileHelper.ReadAllTextAsync(path);
            return SerializationUtility.DeserializeJson<BuildIndexData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HotfixManager] BuildIndex 读取失败：{ex.Message}");
            return null;
        }
    }

    #endregion
}

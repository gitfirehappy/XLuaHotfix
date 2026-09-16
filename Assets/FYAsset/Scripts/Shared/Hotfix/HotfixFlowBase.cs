using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AA 与 AB 共用的确定性热更状态机：启动检查与运行中 Check / Prepare / Apply。
/// </summary>
/// <remarks>
/// Shared 负责状态决策、摘要校验、隔离目录、下载和错误分类；后端实现负责清单、下载项、元数据和激活。
/// 所有运行时读取都相对 ActivePackageRoot。目标包先写 staging，Apply 成功换入正式 Build_* 后才激活。
/// </remarks>
public abstract class HotfixFlowBase
{
    protected abstract string HotfixUrl { get; }
    protected abstract string BackendModeName { get; }
    protected virtual string HotfixConfigurationError =>
        $"{BackendModeName} HotfixUrl 未配置，请先在对应 Settings 资产中填写热更根地址。";
    protected abstract int HotfixMaxRetryCount { get; }
    protected abstract float HotfixRetryBaseDelaySeconds { get; }
    protected abstract int HotfixMetadataTimeoutSeconds { get; }
    protected abstract int HotfixBundleTimeoutSeconds { get; }

    /// <summary>Apply 门禁使用的活跃 Handle 数（Asset + Scene）；由后端提供，Shared 不感知句柄类型。</summary>
    protected abstract int GetActiveHandleCount();

    /// <summary>关闭当前资源管理器；返回 null 表示已关闭，非 null 表示拒绝关闭（例如仍有活跃 Handle）。</summary>
    protected abstract RuntimeMessage ShutdownPackageManager();

    private string PackageIndexUrl => FYAssetPathUtility.JoinUrl(
        HotfixUrl,
        FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    public event Action<string> OnStepChanged;
    public event Action<float, string> OnProgress;
    public event Action<string> OnWarning;
    public event Action<string> OnError;
    public event Action<(VersionNumber ClientVersion, VersionNumber RemoteVersion, string TargetPackageName)> OnClientUpdateRequired;
    public event Action OnFinished;

    private readonly string[] _stepNames =
    {
        "加载 BuildIndex",
        "校验内置包",
        "初始化后端",
        "检查本地包",
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

    private HotfixContext _context;
    private IHotfixPipeline _pipeline;
    private bool _startupCompleted;

    public string CurrentStepName => _currentStepName;
    public float CurrentProgressValue { get; private set; }

    /// <summary>当前流程选中的包根；来源由它与内置包根的比较确定。</summary>
    public string CurrentPackageRoot => _context?.CurrentPackageRoot ?? string.Empty;

    /// <summary>安装包内置完整包根。</summary>
    public string BuiltInPackageRoot => _context?.BuiltInPackageRoot ?? string.Empty;

    /// <summary>最近一次执行的运行中热更阶段，供业务展示进度或决定是否继续操作。</summary>
    public HotfixPhase CurrentPhase { get; private set; }

    /// <summary>已准备且通过精确校验的目标包名；没有待应用目标时为空字符串。</summary>
    public string PreparedTargetName =>
        _context != null && _context.TargetPrepared ? _context.TargetPackageName : string.Empty;

    #region 主流程

    /// <summary>
    /// 初始化热更流程，并将未处理异常统一转换为致命错误。
    /// </summary>
    public async Task InitializeAsync()
    {
        _currentStepIndex = -1;
        CurrentProgressValue = 0f;
        _finishedRaised = false;
        _startupCompleted = false;

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
    /// 按启动流程执行：校验 BuildIndex 与内置包、决定当前内容、准备并激活目标，最后写指针并清理旧包。
    /// </summary>
    private async Task RunAsync()
    {
        var ctx = new HotfixContext();
        _context = ctx;

        await LoadStartupStateAsync(ctx);

        IHotfixPipeline pipeline = CreatePipeline();
        if (pipeline == null)
            ThrowFatal("[HotfixManager] 热更后端创建失败。");
        _pipeline = pipeline;

        await InspectBuiltInPackageAsync(pipeline, ctx);

        if (HotfixStateDecider.IsStandalone(ctx.RuntimeMode))
        {
            // 单机包只使用内置完整包：不读远端 PackageIndex，不创建任何远端目标。
            // 内置 catalog 已随 Player 内置，这里不重复加载外部 catalog。
            await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null, false);
            return;
        }

        if (string.IsNullOrWhiteSpace(HotfixUrl))
            ThrowFatal("[HotfixManager] " + HotfixConfigurationError);

        await InitializeBackendAsync(pipeline);
        await InspectCurrentPackageAsync(pipeline, ctx);

        PackageIndex remoteIndex = await DownloadRemotePackageIndexAsync();
        if (remoteIndex == null)
        {
            await HandleRemoteFailureAsync(
                pipeline,
                ctx,
                "[HotfixManager] 远端 PackageIndex 不可用，继续使用当前完整包。");
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
        HotfixStateDecision decision = DecideTarget(ctx, remoteIndex);
        ctx.PendingAction = decision.Action;
        CompleteStep();

        switch (decision.Action)
        {
            case HotfixStateAction.KeepCurrent:
                Debug.Log($"[HotfixManager] 当前内容已验证：{ctx.CurrentPackageIndex.LatestPackage}，跳过远端 manifest。");
                await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null);
                return;
            case HotfixStateAction.RepairPointer:
                Debug.Log("[HotfixManager] 当前完整包可用，仅补写本地 PackageIndex。");
                await ActivateCurrentAndFinalizeAsync(pipeline, ctx, remoteIndex);
                return;
            case HotfixStateAction.RejectRemote:
                ReportWarning("[HotfixManager] 远端目标不是同 Major 前向新包，保持当前完整包。");
                await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null);
                return;
            case HotfixStateAction.Block:
                ThrowFatal("[HotfixManager] 当前内容需要修复，但远端目标不是可接受的前向版本。");
                return;
        }

        HotfixStepResult prepared = await PrepareTargetCoreAsync(pipeline, ctx);
        if (!prepared.Success)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 目标包准备失败：{FormatError(prepared)}");
            return;
        }

        HotfixStepResult applied = await ApplyTargetCoreAsync(pipeline, ctx);
        if (!applied.Success)
        {
            await HandleTargetFailureAsync(
                pipeline,
                ctx,
                $"[HotfixManager] 目标包激活失败：{FormatError(applied)}");
        }
    }

    #endregion



    #region 运行中 Check / Prepare / Apply

    /// <summary>运行中检查远端 PackageIndex 并做版本决策，不下载内容或切换包根。</summary>
    public async Task<HotfixCheckResult> CheckAsync()
    {
        CurrentPhase = HotfixPhase.Check;
        HotfixContext ctx = _context;
        if (ctx == null || !_startupCompleted)
            return BlockedCheck("热更启动流程尚未完成，无法检查更新。");

        if (HotfixStateDecider.IsStandalone(ctx.RuntimeMode))
            return new HotfixCheckResult(
                HotfixStateAction.KeepCurrent,
                string.Empty,
                default,
                "单机模式只使用内置完整包，不检查远端更新。");

        if (_pipeline == null || string.IsNullOrWhiteSpace(HotfixUrl))
            return BlockedCheck("热更后端不可用，或" + HotfixConfigurationError);

        PackageIndex remoteIndex = await DownloadRemotePackageIndexAsync();
        if (remoteIndex == null)
            return new HotfixCheckResult(
                HotfixStateAction.KeepCurrent,
                string.Empty,
                default,
                "远端 PackageIndex 不可用，保持当前完整包。");

        ctx.RemotePackageIndex = remoteIndex;
        ConfigureRemoteTarget(ctx);

        if (IsMajorMismatch(ctx.BuildIndex, remoteIndex))
        {
            bool remoteIsNewer = remoteIndex.LatestVersion.Major > ctx.BuildIndex.Version.Major;
            if (remoteIsNewer)
            {
                OnClientUpdateRequired?.Invoke((
                    ClientVersion: ctx.BuildIndex.Version,
                    RemoteVersion: remoteIndex.LatestVersion,
                    TargetPackageName: remoteIndex.LatestPackage));
            }

            ctx.PendingAction = HotfixStateAction.KeepCurrent;
            return new HotfixCheckResult(
                HotfixStateAction.KeepCurrent,
                remoteIndex.LatestPackage,
                remoteIndex.LatestVersion,
                remoteIsNewer
                    ? "远端 Major 更高，需要更新整个客户端。"
                    : "远端 Major 低于客户端，可能存在发布或 Channel 配置异常。",
                remoteIsNewer);
        }

        HotfixStateDecision decision = DecideTarget(ctx, remoteIndex);
        ctx.PendingAction = decision.Action;

        switch (decision.Action)
        {
            case HotfixStateAction.PrepareTarget:
                return new HotfixCheckResult(
                    decision.Action,
                    remoteIndex.LatestPackage,
                    remoteIndex.LatestVersion,
                    "存在同 Major 前向目标包，可以准备。");
            case HotfixStateAction.RepairPackage:
                return new HotfixCheckResult(
                    decision.Action,
                    remoteIndex.LatestPackage,
                    remoteIndex.LatestVersion,
                    "本地包损坏且远端是同版本同包，可以准备修复。");
            case HotfixStateAction.RejectRemote:
                return new HotfixCheckResult(
                    decision.Action,
                    remoteIndex.LatestPackage,
                    remoteIndex.LatestVersion,
                    "远端目标是同版本换包或降级，拒绝自动切换。");
            default:
                return new HotfixCheckResult(
                    decision.Action,
                    remoteIndex.LatestPackage,
                    remoteIndex.LatestVersion,
                    "当前内容已是最新，无需准备。");
        }
    }

    /// <summary>运行中在 staging 内准备目标包，当前包继续运行；失败时当前包不受影响。</summary>
    public async Task<HotfixStepResult> PrepareAsync()
    {
        CurrentPhase = HotfixPhase.Prepare;
        HotfixContext ctx = _context;
        if (ctx == null || !_startupCompleted || _pipeline == null)
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.UnsupportedOperation, "热更启动流程尚未完成，无法准备更新。"));

        if (ctx.PendingAction != HotfixStateAction.PrepareTarget
            && ctx.PendingAction != HotfixStateAction.RepairPackage)
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.InvalidArgument, "没有可准备的目标包，请先执行 CheckAsync。"));
        }

        if (ctx.TargetPrepared)
            return HotfixStepResult.Ok;

        HotfixStepResult result = await PrepareTargetCoreAsync(_pipeline, ctx);
        if (!result.Success)
        {
            // staging 产物保留为诊断物：它不可能被激活，下次 Prepare 会复用其中已通过校验的文件
            ctx.TargetPrepared = false;
            ReportWarning(
                $"[HotfixManager] 目标包准备失败，隔离目录保留用于诊断：{ctx.TargetGUIDRoot}");
        }
        return result;
    }

    /// <summary>运行中应用已准备目标：释放 Handle 后切换包根并重新初始化。</summary>
    /// <remarks>
    /// 活跃 Handle 会阻止切换且不会被强制释放；切换或初始化失败时恢复此前完整包，并保留失败 staging。
    /// </remarks>
    public async Task<HotfixStepResult> ApplyAsync()
    {
        CurrentPhase = HotfixPhase.Apply;
        HotfixContext ctx = _context;
        if (ctx == null || !_startupCompleted || _pipeline == null)
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.UnsupportedOperation, "热更启动流程尚未完成，无法应用更新。"));

        if (!ctx.TargetPrepared)
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.InvalidArgument, "没有已准备的目标包，请先执行 PrepareAsync。"));

        return await ApplyTargetCoreAsync(_pipeline, ctx);
    }

    #endregion

    #region 启动流程步骤

    /// <summary>
    /// 读取并校验 BuildIndex，锁定运行模式与内置包身份。
    /// </summary>
    private async Task LoadStartupStateAsync(HotfixContext ctx)
    {
        BeginStep("加载 BuildIndex");
        BuildIndexData buildIndex = await LoadBuildIndexAsync();
        if (!IsBuildIndexTrusted(buildIndex, out string error))
            ThrowFatal($"[HotfixManager] BuildIndex 缺失或无效：{error}");

        ctx.BuildIndex = buildIndex;
        ctx.RuntimeMode = buildIndex.RuntimeMode;
        RuntimePathManager.Initialize(buildIndex);
        RuntimePathManager.EnsureDirectories();
        ctx.BuiltInPackageRoot = RuntimePathManager.BuiltInPackageRoot;
        ctx.BuiltInPackageIndex = new PackageIndex
        {
            LatestPackage = buildIndex.BuildGUID,
            LatestVersion = buildIndex.Version,
            BackendMode = buildIndex.BackendMode
        };
        CompleteStep();
    }

    /// <summary>
    /// 严格检查内置完整包，不完整时致命失败；同时把它设为当前候选内容。
    /// </summary>
    private async Task InspectBuiltInPackageAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        BeginStep("校验内置包");
        ctx.BuiltInPackageInspection = await pipeline.InspectPackageAsync(
            ctx.BuiltInPackageRoot,
            ctx.BuiltInPackageIndex,
            false);
        HotfixStateDecision decision = HotfixStateDecider.DecideBuiltInUsable(
            ctx.BuiltInPackageInspection?.IsComplete == true);
        if (decision.Action == HotfixStateAction.Block)
        {
            ThrowFatal(
                $"[HotfixManager] 内置完整包缺失或不完整：{ctx.BuiltInPackageInspection?.FailureReason}");
        }
        ctx.CurrentPackageRoot = ctx.BuiltInPackageRoot;
        ctx.CurrentPackageIndex = ctx.BuiltInPackageIndex;
        ctx.CurrentPointerTrusted = false;
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
    /// 读取本地 PackageIndex 并精确检查它指向的包；指针不可用或指向的包损坏时原子移除指针，
    /// 把当前候选重置为已验证的内置包，再继续远端检查。
    /// </summary>
    private async Task InspectCurrentPackageAsync(IHotfixPipeline pipeline, HotfixContext ctx)
    {
        BeginStep("检查本地包");
        string localIndexPath = LocalPackageIndexPath;
        bool localIndexExists = FileHelper.Exists(localIndexPath);

        PackageIndex trustedIndex = ReadTrustedLocalPackageIndex();
        bool pointerTrusted = trustedIndex != null
                              && trustedIndex.LatestVersion.Major == ctx.BuildIndex.Version.Major;
        if (trustedIndex != null && !pointerTrusted)
        {
            ReportWarning(
                "[HotfixManager] 本地 PackageIndex Major 与内置包不一致，改用内置包继续远端检查。"
                + $"内置={ctx.BuildIndex.Version.Major}，本地={trustedIndex.LatestVersion.Major}。");
        }

        bool localIsBuiltInIdentity = pointerTrusted
                                      && IsSamePackageIdentity(trustedIndex, ctx.BuiltInPackageIndex);

        // 只有指针可信且指向本地热更包时才读包根：它是候选内容的唯一完整性输入
        HotfixPackageInspection localInspection = null;
        if (pointerTrusted && !localIsBuiltInIdentity)
        {
            string localPackageRoot = RuntimePathManager.GetHotfixPackageRoot(trustedIndex.LatestPackage);
            localInspection = await pipeline.InspectPackageAsync(localPackageRoot, trustedIndex);
        }

        bool localPackageComplete = localInspection?.IsComplete == true;
        bool localPackageDamaged = pointerTrusted && !localIsBuiltInIdentity && !localPackageComplete;
        // 指针文件存在但无法解析：它同样不能继续被本次或下次启动当作可用指针
        bool localPointerUnreadable = trustedIndex == null && localIndexExists;

        bool useLocalPackage = HotfixStateDecider.ShouldUseLocalPackage(
            pointerTrusted,
            localIsBuiltInIdentity,
            localPackageComplete);
        if (useLocalPackage)
        {
            ctx.CurrentPackageIndex = trustedIndex;
            ctx.CurrentPackageRoot = RuntimePathManager.GetHotfixPackageRoot(trustedIndex.LatestPackage);
            ctx.CurrentPackageInspection = localInspection;
        }
        else
        {
            if (localPointerUnreadable || localPackageDamaged)
            {
                string reason = localPackageDamaged
                    ? $"本地包不完整：{localInspection?.FailureReason}"
                    : "本地 PackageIndex 校验未通过";
                ClearLocalPackageIndex(reason);
                ctx.CurrentPointerTrusted = false;
                ReportWarning(
                    $"[HotfixManager] {reason}，已移除本地指针并回退内置包继续远端检查。");
            }

            // 损坏包不得继续作为当前内容：候选及其身份、包根、检查结果全部重置为已验证内置包
            ctx.CurrentPackageIndex = ctx.BuiltInPackageIndex;
            ctx.CurrentPackageRoot = ctx.BuiltInPackageRoot;
            ctx.CurrentPackageInspection = ctx.BuiltInPackageInspection;
        }

        CompleteStep();
    }

    /// <summary>
    /// 下载并校验远端 PackageIndex。
    /// </summary>
    private async Task<PackageIndex> DownloadRemotePackageIndexAsync()
    {
        BeginStep("下载 PackageIndex");
        string json = await NetworkDownloader.DownloadText(
            PackageIndexUrl, HotfixMetadataTimeoutSeconds, HotfixMaxRetryCount, HotfixRetryBaseDelaySeconds);
        if (string.IsNullOrEmpty(json))
        {
            CompleteStep();
            return null;
        }

        try
        {
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (!IsPackageIndexTrusted(index, json, out string error))
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

    #endregion

    #region 决策处理

    /// <summary>
    /// 远端失败时只允许启动此前已验证且拥有可信指针的当前完整包。
    /// </summary>
    private async Task HandleRemoteFailureAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        string warning)
    {
        ReportWarning(warning);
        HotfixFallbackDecision fallback = HotfixStateDecider.DecideRemoteFailure(
            ctx.RuntimeMode,
            ctx.CurrentPackageInspection?.IsComplete == true,
            IsCurrentBuiltIn(ctx));
        if (fallback.DegradedToBuiltIn)
        {
            // 退化只影响本次读取根，不修改 RuntimeMode：下次启动照常按 Online 检查远端。
            ReportWarning(
                "[HotfixManager] 本次退化使用内置完整包；运行模式保持 "
                + $"{fallback.RuntimeMode}，下次启动仍会正常检查远端。");
        }
        if (fallback.Action == HotfixStateAction.Block)
            ThrowFatal(warning);

        await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null);
    }

    /// <summary>
    /// 目标失败后的收尾：回收换入痕迹后按固定远端失败规则回退或阻断。
    /// </summary>
    /// <remarks>
    /// staging 诊断物不在这里删除：失败目标不允许成为活动包，隔离产物保留下来供定位问题；
    /// 回退到当前完整包的路径不写本地 PackageIndex。
    /// </remarks>
    private async Task HandleTargetFailureAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        string warning)
    {
        ctx.TargetPrepared = false;
        RestoreTargetRoot(ctx);
        // 本次失败目标的隔离产物是诊断物：随后的旧包回收必须跳过它
        ctx.TargetDiagnosticRoot = ctx.TargetGUIDRoot;
        ReportWarning(warning);
        HotfixStateAction action = HotfixStateDecider.DecideTargetFailure(
            ctx.CurrentPackageInspection?.IsComplete == true);
        if (action == HotfixStateAction.Block)
            ThrowFatal(warning);

        await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null);
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
            ctx.CurrentPackageInspection?.IsComplete == true);
        ctx.PendingAction = decision.Action;
        if (decision.NotifyClientUpdate)
        {
            OnClientUpdateRequired?.Invoke((
                ClientVersion: ctx.BuildIndex.Version,
                RemoteVersion: ctx.RemotePackageIndex.LatestVersion,
                TargetPackageName: ctx.RemotePackageIndex.LatestPackage));
        }

        string message = remoteMajor > clientMajor
            ? $"[HotfixManager] 远端 Major 更高，跳过热更并继续当前客户端内容。客户端={clientMajor}，远端={remoteMajor}。"
            : $"[HotfixManager] 远端 Major 低于客户端，可能存在发布或 Channel 配置异常。客户端={clientMajor}，远端={remoteMajor}。";
        ReportWarning(message);
        if (decision.Action == HotfixStateAction.Block)
            ThrowFatal(message);

        await ActivateCurrentAndFinalizeAsync(pipeline, ctx, null);
    }

    /// <summary>
    /// 激活当前候选内容并完成资源管理器初始化；包根必须在这里显式决定。
    /// </summary>
    private async Task ActivateCurrentAndFinalizeAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        PackageIndex packageIndexToPersist,
        bool activateCatalog = true)
    {
        BeginStep("应用更新");
        bool activated = await ActivatePackageRootAsync(
            pipeline,
            IsCurrentBuiltIn(ctx),
            ctx.CurrentPackageRoot,
            ctx.CurrentPackageIndex?.LatestPackage,
            activateCatalog);
        if (!activated)
            ThrowFatal($"[HotfixManager] 当前完整包激活失败：{ctx.CurrentPackageRoot}");
        CompleteStep();

        await FinalizeInitializationAsync(
            packageIndexToPersist,
            !IsCurrentBuiltIn(ctx) ? ctx.CurrentPackageRoot : null,
            ctx.TargetDiagnosticRoot);
    }

    #endregion

    #region 目标准备与激活

    /// <summary>
    /// 在隔离 staging 目录内准备目标包：复用同 Hash 内容、下载剩余内容、持久化元数据并精确校验文件集合。
    /// </summary>
    /// <remarks>
    /// 只写 ctx.TargetGUIDRoot（staging）与后端元数据，不写正式 Build_* 根、不切换包根、不写本地 PackageIndex；
    /// 同包修复与换包前向都走这一条路径，因此不可能在原地覆写当前或损坏的正式包目录。
    /// 失败时 staging 产物保留为诊断物，由调用方决定是否回收，且绝不会被激活。
    /// </remarks>
    private async Task<HotfixStepResult> PrepareTargetCoreAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx)
    {
        if (ctx.RemotePackageIndex == null)
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.InvalidArgument, "缺少远端 PackageIndex，无法准备目标包。"));

        // 目标写入根必须是 HotfixRoot 直接子级下的 staging：越界路径不允许被创建或写入
        if (!IsDirectPackageRoot(ctx.TargetGUIDRoot))
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.InvalidArgument, $"目标隔离目录不安全：{ctx.TargetGUIDRoot}"));
        }

        HotfixVersionInfo remoteInfo = await FetchRemoteVersionAsync(pipeline, ctx);
        if (remoteInfo == null
            || !HotfixPackageValidator.IsVersionValid(remoteInfo.Version)
            || remoteInfo.Version != ctx.RemotePackageIndex.LatestVersion)
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.NotFound, "远端包 manifest 不可用或与 PackageIndex 不一致。"));
        }

        IReadOnlyList<BundleDownloadItem> downloadList = PrepareDownloadList(pipeline, remoteInfo);
        if (!ValidateBundleList(downloadList, out string bundleListError))
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.InvalidArgument, $"远端 manifest Bundle 列表无效：{bundleListError}"));
        }

        bool bundlesReady = await DownloadBundlesAsync(ctx, downloadList);
        if (!bundlesReady)
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.BundleNotFound, "一个或多个包内 Bundle 准备失败。"));
        }

        BeginStep("处理下载结果");
        HotfixStepResult metadataResult = await pipeline.PersistRemoteMetadataAsync(
            ctx,
            HotfixMetadataTimeoutSeconds,
            HotfixMaxRetryCount,
            HotfixRetryBaseDelaySeconds);
        if (!metadataResult.Success)
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.LoadFailed, $"远端元数据持久化失败：{FormatError(metadataResult)}"));
        }
        CompleteStep();

        HotfixPackageInspection targetInspection = await pipeline.InspectPackageAsync(
            ctx.TargetGUIDRoot,
            ctx.RemotePackageIndex,
            false);
        if (targetInspection == null || !targetInspection.IsComplete)
        {
            return HotfixStepResult.Fail(RuntimeMessage.Error(
                RuntimeErrorCodes.LoadFailed,
                $"目标包不完整：{targetInspection?.FailureReason}"));
        }

        ctx.TargetPrepared = true;
        Debug.Log($"[HotfixManager] 目标包已准备并校验通过：{ctx.TargetPackageName}");
        return HotfixStepResult.Ok;
    }

    /// <summary>
    /// 获取目标包的远端 manifest 信息。
    /// </summary>
    private async Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx)
    {
        BeginStep("获取远端版本");
        HotfixVersionInfo info = await pipeline.FetchRemoteVersionAsync(
            ctx.RemoteUrlRoot, HotfixMetadataTimeoutSeconds, HotfixMaxRetryCount, HotfixRetryBaseDelaySeconds);
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
    /// 切换包根、激活目标包；activateCatalog 为 false 时只切换读取根（单机内置包已内置 catalog）。
    /// </summary>
    private async Task<bool> ActivatePackageRootAsync(
        IHotfixPipeline pipeline,
        bool isBuiltIn,
        string packageRoot,
        string packageName,
        bool activateCatalog = true)
    {
        if (isBuiltIn)
        {
            // 内置包根由激活流程显式决定；CurrentGUIDRoot 仍记录本地包身份，不参与读取
            RuntimePathManager.ActivateBuiltInPackage();
        }
        else
        {
            RuntimePathManager.SwitchToNewBuild(packageName);
        }

        if (!activateCatalog)
            return true;

        try
        {
            HotfixStepResult activation = await pipeline.ActivatePackageAsync(packageRoot);
            if (!activation.Success)
            {
                ReportWarning($"[HotfixManager] 包根激活失败：{packageRoot}，原因={FormatError(activation)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] 包根激活发生异常：{packageRoot}，原因={ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 激活并初始化已准备的目标包：旧正式目录先 move 到 backup，再把 staging 原子换入正式 Build_* 路径；
    /// 成功后最后写本地 PackageIndex，并删除 backup 与旧包。
    /// </summary>
    /// <remarks>
    /// 存在活跃 Handle 时拒绝切换且不释放句柄；
    /// 换入、激活或初始化失败时把换入内容退回 staging、恢复此前完整包，恢复失败返回阻断错误；
    /// 失败路径不写本地 PackageIndex，也不删除 staging 诊断物。
    /// </remarks>
    private async Task<HotfixStepResult> ApplyTargetCoreAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx)
    {
        BeginStep("应用更新");

        HotfixApplyDecision gate = HotfixStateDecider.DecideApply(GetActiveHandleCount());
        if (!gate.CanApply)
        {
            RuntimeMessage gateError = RuntimeMessage.ActiveHandlesBlockShutdown(
                gate.ActiveHandleCount,
                "热更 Apply 需要业务先释放全部 Asset 与 Scene Handle");
            ReportWarning($"[HotfixManager] 拒绝 Apply：{gateError}");
            return HotfixStepResult.Fail(gateError);
        }

        RuntimeMessage shutdownError = ShutdownPackageManager();
        if (shutdownError != null)
        {
            ReportWarning($"[HotfixManager] 关闭旧资源管理器失败：{shutdownError}");
            return HotfixStepResult.Fail(shutdownError);
        }

        bool previousIsBuiltIn = IsCurrentBuiltIn(ctx);
        string previousRoot = ctx.CurrentPackageRoot;
        string previousName = ctx.CurrentPackageIndex?.LatestPackage;

        // 正式包根只在换入阶段写入；staging 与正式根只在这一步发生目录级替换
        string finalRoot = RuntimePathManager.GetHotfixPackageRoot(ctx.TargetPackageName);
        if (!TryPromoteStagingToTargetRoot(ctx, finalRoot, out string promoteError))
        {
            bool promoteRestored = await RollbackToCurrentAsync(pipeline, ctx, previousIsBuiltIn, previousRoot, previousName);
            return ActivationFailure($"目标包换入失败：{promoteError}", promoteRestored);
        }

        bool activated = await ActivatePackageRootAsync(
            pipeline,
            false,
            finalRoot,
            ctx.TargetPackageName);
        if (!activated)
        {
            // 换入已发生：先把正式路径还原为换入前的内容，再恢复运行根
            RestoreTargetRoot(ctx);
            bool restored = await RollbackToCurrentAsync(pipeline, ctx, previousIsBuiltIn, previousRoot, previousName);
            return ActivationFailure("目标包激活失败", restored);
        }

        RuntimePathManager.EnsureDirectories();
        bool initialized = await FinishHotfix();
        if (!initialized)
        {
            RestoreTargetRoot(ctx);
            bool restored = await RollbackToCurrentAsync(pipeline, ctx, previousIsBuiltIn, previousRoot, previousName);
            return ActivationFailure("目标 PackageManager 初始化失败", restored);
        }

        RuntimeMessage bindError = BindPackageManager();
        if (bindError != null)
        {
            RestoreTargetRoot(ctx);
            bool restored = await RollbackToCurrentAsync(pipeline, ctx, previousIsBuiltIn, previousRoot, previousName);
            return ActivationFailure($"目标后端绑定失败：{bindError}", restored);
        }

        // 激活与初始化都成功之后才写指针：失败路径绝不留下指向未激活包的指针
        if (ctx.RemotePackageIndex != null
            && HotfixStateDecider.ShouldPersistLocalPackageIndex(true, true))
        {
            PersistLocalPackageIndex(ctx.RemotePackageIndex);
        }

        // 换入成功且指针已提交：此时才删除旧包目录（含同包修复的旧损坏目录）与其他遗留隔离目录
        ctx.TargetRootBackedUp = false;
        DiscardTargetBackup(ctx);
        CleanupInactivePackages(finalRoot);
        ctx.CurrentPackageRoot = finalRoot;
        ctx.TargetGUIDRoot = finalRoot;
        ctx.CurrentPackageIndex = ctx.RemotePackageIndex;
        ctx.CurrentPointerTrusted = true;
        ctx.TargetPrepared = false;
        ctx.PendingAction = HotfixStateAction.KeepCurrent;
        CompleteStep();

        // 启动路径在这里补齐启动完成事件；运行中 Apply 不重复广播，完成信号就是本方法的返回值
        RaiseInitializationCompleted();
        Debug.Log($"[HotfixManager] 热更内容已激活：{ctx.TargetPackageName}，来源={(IsCurrentBuiltIn(ctx) ? "内置" : "本地")}。");
        return HotfixStepResult.Ok;
    }

    /// <summary>
    /// 目标激活失败时恢复此前的完整包。
    /// </summary>
    /// <returns>true 表示已恢复到此前完整包；false 表示恢复失败，调用方应阻断。</returns>
    private async Task<bool> RollbackToCurrentAsync(
        IHotfixPipeline pipeline,
        HotfixContext ctx,
        bool previousIsBuiltIn,
        string previousRoot,
        string previousName)
    {
        ReportWarning($"[HotfixManager] 正在恢复此前的完整包：{previousName}");

        bool activated = false;
        try
        {
            activated = await ActivatePackageRootAsync(
                pipeline,
                previousIsBuiltIn,
                previousRoot,
                previousName);
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] 恢复此前包根发生异常：{ex.Message}");
        }

        if (!activated)
            return false;

        try
        {
            bool initialized = await FinishHotfix();
            if (initialized)
                ctx.CurrentPackageRoot = previousRoot;
            return initialized;
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] 恢复此前 PackageManager 发生异常：{ex.Message}");
            return false;
        }
    }

    private static HotfixStepResult ActivationFailure(string reason, bool restored)
    {
        return HotfixStepResult.Fail(RuntimeMessage.Error(
            RuntimeErrorCodes.LoadFailed,
            restored
                ? $"{reason}，已恢复此前完整包，本地 PackageIndex 未修改。"
                : $"{reason}，且此前完整包恢复失败，启动内容不可信。"));
    }

    /// <summary>
    /// 初始化运行时资源管理器，最后写指针、回收旧包并触发完成事件。
    /// </summary>
    /// <param name="activeLocalRootToKeep">本次激活的本地包根；为空表示当前使用内置包，跳过旧包清理。</param>
    /// <param name="keepDiagnosticRoot">
    /// 本次失败目标的 staging 根：保留为诊断物，本次回收不删除它；为空表示没有待保留项。
    /// </param>
    private async Task FinalizeInitializationAsync(
        PackageIndex packageIndexToPersist,
        string activeLocalRootToKeep,
        string keepDiagnosticRoot = null)
    {
        BeginStep("完成初始化");
        RuntimePathManager.EnsureDirectories();
        bool initialized = await FinishHotfix();
        if (!initialized)
            ThrowFatal("[HotfixManager] PackageManager 初始化失败。");

        if (packageIndexToPersist != null
            && HotfixStateDecider.ShouldPersistLocalPackageIndex(true, initialized))
        {
            PersistLocalPackageIndex(packageIndexToPersist);
        }

        if (!string.IsNullOrEmpty(activeLocalRootToKeep))
            CleanupInactivePackages(activeLocalRootToKeep, keepDiagnosticRoot);

        CompleteInitialization();
    }

    private void CompleteInitialization()
    {
        RuntimeMessage bindError = BindPackageManager();
        if (bindError != null)
            ThrowFatal($"[HotfixManager] 后端绑定失败：{bindError}");

        CompleteStep();
        RaiseInitializationCompleted();
    }

    /// <summary>
    /// 标记启动流程完成并广播完成事件；重复调用只更新状态，不重复广播。
    /// </summary>
    private void RaiseInitializationCompleted()
    {
        _startupCompleted = true;
        if (_finishedRaised)
            return;
        _finishedRaised = true;
        OnFinished?.Invoke();
    }

    #endregion

    #region 辅助函数

    /// <summary>
    /// 按当前上下文与远端指针执行版本比较决策。
    /// </summary>
    private static HotfixStateDecision DecideTarget(HotfixContext ctx, PackageIndex remoteIndex)
    {
        return HotfixStateDecider.DecideTarget(
            ctx.CurrentPackageIndex.LatestPackage,
            ctx.CurrentPackageIndex.LatestVersion,
            ctx.CurrentPackageInspection?.IsComplete == true,
            ctx.CurrentPointerTrusted,
            IsCurrentBuiltIn(ctx),
            remoteIndex.LatestPackage,
            remoteIndex.LatestVersion);
    }

    /// <summary>
    /// 根据远端 PackageIndex 固定目标包隔离写入根与下载地址。
    /// </summary>
    /// <remarks>
    /// TargetGUIDRoot 固定为 HotfixRoot 下的 staging 根，与正式 Build_* 路径不同名：
    /// 准备阶段不得写入当前、损坏或即将使用的正式包目录。
    /// </remarks>
    private void ConfigureRemoteTarget(HotfixContext ctx)
    {
        ctx.TargetPackageName = ctx.RemotePackageIndex.LatestPackage;
        ctx.RemoteUrlRoot = FYAssetPathUtility.JoinUrl(
            HotfixUrl,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            ctx.TargetPackageName);
        ctx.TargetGUIDRoot = RuntimePathManager.GetHotfixStagingRoot(ctx.TargetPackageName);
        ctx.TargetPrepared = false;
        ctx.TargetRootBackedUp = false;
        ctx.TargetRootSwapped = false;
        ctx.TargetDiagnosticRoot = null;
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
        if (buildIndex.RuntimeMode != RuntimeMode.Online
            && buildIndex.RuntimeMode != RuntimeMode.Standalone)
        {
            error = $"RuntimeMode 无效：{buildIndex.RuntimeMode}。";
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
    /// 创建当前模式对应的热更后端。
    /// </summary>
    protected abstract IHotfixPipeline CreatePipeline();

    /// <summary>
    /// 完成 AA 或 AB 运行时资源管理器初始化。
    /// </summary>
    protected abstract Task<bool> FinishHotfix();

    /// <summary>
    /// 后端绑定校验钩子；返回非 null 视为失败。跨后端"仅绑定一个 backend"互斥检查属 Compat facade 职责，单后端默认返回 null（成功）。
    /// </summary>
    protected virtual RuntimeMessage BindPackageManager()
    {
        return null;
    }

    /// <summary>本地活动包指针文件路径：HotfixRoot 下的 PackageIndex 文件。</summary>
    private static string LocalPackageIndexPath => FYAssetPathUtility.JoinFilePath(
        RuntimePathManager.HotfixRoot,
        FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    /// <summary>
    /// 移除本地包指针文件；文件不存在时无操作。
    /// </summary>
    /// <remarks>
    /// 本地包损坏或指针文件不可解析时必须移除：否则同一份不可用内容会在下次启动继续被选中。
    /// 移除失败只告警：本次启动已确定使用内置包，残留指针不会指向活动包。
    /// </remarks>
    private void ClearLocalPackageIndex(string reason)
    {
        string path = LocalPackageIndexPath;
        if (!FileHelper.Exists(path))
            return;

        if (FileHelper.TryDelete(path))
        {
            Debug.Log($"[HotfixManager] 已移除本地 PackageIndex 指针：{reason}。");
            return;
        }

        ReportWarning($"[HotfixManager] 本地 PackageIndex 指针移除失败，请检查文件权限：{path}");
    }

    /// <summary>
    /// 在运行时初始化成功后持久化新的本地活动包指针。
    /// </summary>
    private void PersistLocalPackageIndex(PackageIndex packageIndex)
    {
        try
        {
            string json = SerializationUtility.SerializeToJson(packageIndex, true);
            FileHelper.WriteAllTextAtomic(LocalPackageIndexPath, json);
        }
        catch (Exception ex)
        {
            ThrowFatal($"[HotfixManager] 本地 PackageIndex 持久化失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 读取并校验本地活动包指针；缺失或损坏时返回 null，由调用方退化为内置包。
    /// </summary>
    private PackageIndex ReadTrustedLocalPackageIndex()
    {
        string path = LocalPackageIndexPath;
        if (!FileHelper.Exists(path))
            return null;

        try
        {
            string json = FileHelper.ReadAllText(path);
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (IsPackageIndexTrusted(index, json, out string error))
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
    private bool IsPackageIndexTrusted(PackageIndex index, string json, out string error)
    {
        if (index == null
            || !VersionNumber.JsonHasObjectField(json, nameof(PackageIndex.LatestVersion))
            || !HotfixPackageValidator.IsPackageName(index.LatestPackage)
            || !HotfixPackageValidator.IsVersionValid(index.LatestVersion))
        {
            error = "LatestPackage 缺失，或 LatestVersion 字段缺失/无效。";
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
        return buildIndex != null
               && remoteIndex != null
               && buildIndex.Version.Major != remoteIndex.LatestVersion.Major;
    }

    /// <summary>
    /// 建立当前完整包的 Hash 到 BundleName 索引。
    /// </summary>
    private static Dictionary<string, string> BuildReuseBundleMap(HotfixVersionInfo currentPackageInfo)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (currentPackageInfo?.Bundles == null)
            return map;

        for (int i = 0; i < currentPackageInfo.Bundles.Count; i++)
        {
            BundleDownloadItem bundle = currentPackageInfo.Bundles[i];
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
    /// 从当前完整包复制并校验同 Hash Bundle。
    /// </summary>
    private bool TryReuseCurrentBundle(
        BundleDownloadItem bundle,
        Dictionary<string, string> reuseBundleMap,
        string currentPackageRoot,
        string savePath)
    {
        if (string.IsNullOrEmpty(bundle.FileHash)
            || string.IsNullOrEmpty(currentPackageRoot)
            || !reuseBundleMap.TryGetValue(bundle.FileHash, out string currentBundleName))
        {
            return false;
        }
        if (!HotfixPackageValidator.IsSafePathSegment(currentBundleName))
            return false;

        string currentBundlePath = FYAssetPathUtility.JoinFilePath(
            currentPackageRoot,
            FYAssetSettings.BUNDLES_DIRECTORY_NAME,
            currentBundleName);
        if (FYAssetPathUtility.AreSamePath(currentBundlePath, savePath)
            || !IsFileSizeValid(currentBundlePath, bundle))
            return false;

        string tempPath = savePath + ".tmp";
        try
        {
            FileHelper.TryDelete(tempPath);
            FileHelper.CopyFile(currentBundlePath, tempPath);
            if (!VerifyBundle(tempPath, bundle))
                return false;
            FileHelper.ReplaceFile(tempPath, savePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[HotfixManager] 当前完整包的 Bundle 复用失败：{currentBundleName}，错误={ex.Message}");
            return false;
        }
        finally
        {
            FileHelper.TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 按目标目录、当前完整包、网络的优先级准备 Bundle。
    /// </summary>
    private async Task<bool> DownloadBundlesAsync(
        HotfixContext ctx,
        IReadOnlyList<BundleDownloadItem> remoteBundles)
    {
        BeginStep("下载 Bundle");
        try
        {
            string targetBundleRoot = FYAssetPathUtility.JoinFilePath(
                ctx.TargetGUIDRoot,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME);
            FileHelper.EnsureDirectory(targetBundleRoot);
            CleanupStaleTempFiles(targetBundleRoot);

            var reuseBundleMap = BuildReuseBundleMap(ctx.CurrentPackageInspection?.VersionInfo);
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

                if (TryReuseCurrentBundle(bundle, reuseBundleMap, ctx.CurrentPackageRoot, savePath))
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
        int totalAttempts = Mathf.Max(0, HotfixMaxRetryCount) + 1;
        float retryBaseDelaySeconds = Mathf.Max(0f, HotfixRetryBaseDelaySeconds);
        string tempPath = savePath + ".tmp";
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            FileHelper.TryDelete(tempPath);
            bool downloaded = await NetworkDownloader.DownloadFileOnce(url, tempPath, HotfixBundleTimeoutSeconds);
            if (downloaded && VerifyBundle(tempPath, bundle))
            {
                FileHelper.ReplaceFile(tempPath, savePath);
                onDone?.Invoke();
                return true;
            }

            FileHelper.TryDelete(tempPath);
            if (attempt < totalAttempts && retryBaseDelaySeconds > 0f)
            {
                int delayMs = Mathf.RoundToInt(
                    retryBaseDelaySeconds * 1000f * Mathf.Pow(2f, attempt - 1));
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
    private static bool IsCurrentBuiltIn(HotfixContext ctx) => ctx != null && string.Equals(ctx.CurrentPackageRoot, ctx.BuiltInPackageRoot, StringComparison.Ordinal);

    private static string FormatError(HotfixStepResult result)
    {
        return result.Error != null ? result.Error.ToString() : "未知错误";
    }

    private static HotfixCheckResult BlockedCheck(string message)
    {
        return new HotfixCheckResult(
            HotfixStateAction.Block,
            string.Empty,
            default,
            message);
    }

    /// <summary>
    /// 把隔离 staging 换入正式 Build_* 路径：旧正式目录先 move 到 backup，再把 staging move 到正式路径。
    /// </summary>
    /// <returns>true 表示正式路径上是本次新内容；false 表示换入未完成，调用方必须先恢复运行根。</returns>
    /// <remarks>
    /// 调用前必须已关闭旧资源管理器；staging 只在这一步离开隔离位置，
    /// 因此同包修复不可能在正式或损坏包目录内原地写入。
    /// </remarks>
    private bool TryPromoteStagingToTargetRoot(HotfixContext ctx, string finalRoot, out string error)
    {
        error = string.Empty;
        string stagingRoot = ctx.TargetGUIDRoot;
        string backupRoot = RuntimePathManager.GetHotfixBackupRoot(ctx.TargetPackageName);
        if (!IsDirectPackageRoot(stagingRoot)
            || !IsDirectPackageRoot(finalRoot)
            || !IsDirectPackageRoot(backupRoot))
        {
            error = $"staging、正式包根或 backup 不在 HotfixRoot 直接子级：{stagingRoot} → {finalRoot}";
            return false;
        }
        if (!FileHelper.DirectoryExists(stagingRoot))
        {
            error = $"staging 目录不存在：{stagingRoot}";
            return false;
        }

        try
        {
            // 上次失败遗留的 backup 不再需要：本次 staging 已按目标 manifest 完整校验通过
            FileHelper.TryDeleteDirectory(backupRoot, true);
            if (FileHelper.DirectoryExists(finalRoot))
            {
                MoveDirectory(finalRoot, backupRoot);
                ctx.TargetRootBackedUp = true;
            }

            MoveDirectory(stagingRoot, finalRoot);
            ctx.TargetRootSwapped = true;
            Debug.Log($"[HotfixManager] 已把隔离目录换入正式包根：{Path.GetFileName(finalRoot)}。");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            RestoreTargetRoot(ctx);
            return false;
        }
    }

    /// <summary>
    /// 换入失败或激活失败时的补偿：换入产物退回 staging 保留诊断，换入前的正式目录放回正式路径。
    /// </summary>
    /// <remarks>
    /// 换入未发生时（两个换入标志都为 false）不做任何文件操作：
    /// 正式包根上此时可能是仍在使用的当前包，不能被本次失败流程触碰。
    /// </remarks>
    private void RestoreTargetRoot(HotfixContext ctx)
    {
        bool swapped = ctx.TargetRootSwapped;
        bool backedUp = ctx.TargetRootBackedUp;
        ctx.TargetRootSwapped = false;
        ctx.TargetRootBackedUp = false;
        if (!swapped && !backedUp)
            return;

        string stagingRoot = RuntimePathManager.GetHotfixStagingRoot(ctx.TargetPackageName);
        string finalRoot = RuntimePathManager.GetHotfixPackageRoot(ctx.TargetPackageName);
        string backupRoot = RuntimePathManager.GetHotfixBackupRoot(ctx.TargetPackageName);
        if (!IsDirectPackageRoot(stagingRoot) || !IsDirectPackageRoot(finalRoot) || !IsDirectPackageRoot(backupRoot))
            return;

        try
        {
            if (swapped && FileHelper.DirectoryExists(finalRoot))
                MoveDirectory(finalRoot, stagingRoot);
            if (backedUp && FileHelper.DirectoryExists(backupRoot))
                MoveDirectory(backupRoot, finalRoot);
        }
        catch (Exception ex)
        {
            ReportWarning($"[HotfixManager] 恢复换入前的包目录失败：{finalRoot}，原因={ex.Message}");
            return;
        }

        Debug.LogWarning($"[HotfixManager] 已恢复换入前的包目录：{Path.GetFileName(finalRoot)}。");
    }

    /// <summary>
    /// 删除本次换入留下的 backup 目录（即换入前的旧包目录）；目录不存在时无操作。
    /// </summary>
    /// <remarks>
    /// 只在换入、激活、初始化与指针写入都成功之后调用；删除失败只告警，
    /// 残留目录是 HotfixRoot 直接子级，会在下次成功启动的旧包回收里重试。
    /// </remarks>
    private void DiscardTargetBackup(HotfixContext ctx)
    {
        string backupRoot = RuntimePathManager.GetHotfixBackupRoot(ctx.TargetPackageName);
        if (!IsDirectPackageRoot(backupRoot) || !FileHelper.DirectoryExists(backupRoot))
            return;

        if (!FileHelper.TryDeleteDirectory(backupRoot, true))
        {
            ReportWarning($"[HotfixManager] 旧包目录删除失败，将在下次成功启动时重试：{backupRoot}");
            return;
        }

        Debug.Log($"[HotfixManager] 已删除旧包目录：{Path.GetFileName(backupRoot)}。");
    }

    /// <summary>
    /// 移动目录：目标已存在时先删除目标，同卷目录改名不会产生半成品目录树。
    /// </summary>
    private static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (!FileHelper.DirectoryExists(sourceDir))
            throw new DirectoryNotFoundException($"待移动目录不存在：{sourceDir}");

        FileHelper.EnsureDirectory(Path.GetDirectoryName(targetDir));
        if (FileHelper.DirectoryExists(targetDir))
            FileHelper.TryDeleteDirectory(targetDir, true);
        Directory.Move(sourceDir, targetDir);
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
    /// 删除 HotfixRoot 下除当前激活包外的直接子级 Build_* 目录；当前激活包为空时全部删除。
    /// </summary>
    /// <param name="activePackageRoot">当前激活的包根；为空表示使用内置包，跳过本次回收。</param>
    /// <param name="keepDiagnosticRoot">要保留为诊断物的 staging 根；为空表示没有待保留项。</param>
    /// <remarks>
    /// staging / backup 目录与被替换的旧包目录都是 Build_* 直接子级，因此一并在这里回收，
    /// 保证失败准备的残留不会无限累积；唯一例外是 keepDiagnosticRoot：
    /// 失败目标的隔离产物在失败当下与随后恢复当前内容时都保留，供定位问题。
    /// </remarks>
    private static void CleanupInactivePackages(string activePackageRoot, string keepDiagnosticRoot = null)
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
                if (!string.IsNullOrEmpty(keepDiagnosticRoot)
                    && FYAssetPathUtility.AreSamePath(packageDir, keepDiagnosticRoot))
                {
                    Debug.Log($"[HotfixManager] 保留失败准备的隔离目录用于诊断：{packageDir}");
                    continue;
                }

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
    /// 从 StreamingAssets 读取内置 BuildIndex；它是启动标记，不是包内容，因此不受单包根约束。
    /// </summary>
    private async Task<BuildIndexData> LoadBuildIndexAsync()
    {
        string path = FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.BUILD_INDEX_FILENAME);
        try
        {
            string json = await FileHelper.ReadAllTextAsync(path);
            if (!VersionNumber.JsonHasObjectField(json, nameof(BuildIndexData.Version)))
            {
                Debug.LogWarning("[HotfixManager] BuildIndex 缺少 Version 对象字段。");
                return null;
            }

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

using System;
using System.Threading.Tasks;

/// <summary>
/// AB 热更入口。
/// </summary>
public static class ABHotfixManager
{
    private static readonly ABHotfixFlow Flow = new();

    public static event Action<string> OnStepChanged
    {
        add => Flow.OnStepChanged += value;
        remove => Flow.OnStepChanged -= value;
    }

    public static event Action<float, string> OnProgress
    {
        add => Flow.OnProgress += value;
        remove => Flow.OnProgress -= value;
    }

    public static event Action<string> OnError
    {
        add => Flow.OnError += value;
        remove => Flow.OnError -= value;
    }

    public static event Action<string> OnWarning
    {
        add => Flow.OnWarning += value;
        remove => Flow.OnWarning -= value;
    }

    public static event Action<ClientUpdateRequiredInfo> OnClientUpdateRequired
    {
        add => Flow.OnClientUpdateRequired += value;
        remove => Flow.OnClientUpdateRequired -= value;
    }

    public static event Action OnFinished
    {
        add => Flow.OnFinished += value;
        remove => Flow.OnFinished -= value;
    }

    public static string CurrentStepName => Flow.CurrentStepName;
    public static float CurrentProgressValue => Flow.CurrentProgressValue;

    /// <summary>当前使用的内容归属：内置包或本地热更包。</summary>
    public static HotfixContentState CurrentContent => Flow.CurrentContent;

    /// <summary>已准备且校验通过、等待 Apply 的目标包名；没有待应用目标时为空字符串。</summary>
    public static string PreparedTargetName => Flow.PreparedTargetName;

    public static Task InitializeAsync() => Flow.InitializeAsync();

    /// <summary>运行中检查是否存在可接受更新；不下载内容、不切换包根。</summary>
    public static Task<HotfixCheckResult> CheckAsync() => Flow.CheckAsync();

    /// <summary>运行中准备目标包；当前包继续运行，只在隔离目录内写入。</summary>
    public static Task<HotfixStepResult> PrepareAsync() => Flow.PrepareAsync();

    /// <summary>运行中应用已准备的目标包；由业务在安全入口调用，句柄未释放时拒绝。</summary>
    public static Task<HotfixStepResult> ApplyAsync() => Flow.ApplyAsync();

    private sealed class ABHotfixFlow : HotfixFlowBase
    {
        protected override string HotfixUrl => FYAssetABSettings.Instance.HotfixUrl;
        protected override string BackendModeName => "AB";
        protected override int HotfixMaxRetryCount => FYAssetABSettings.Instance.HotfixMaxRetryCount;
        protected override float HotfixRetryBaseDelaySeconds => FYAssetABSettings.Instance.HotfixRetryBaseDelaySeconds;
        protected override int HotfixMetadataTimeoutSeconds => FYAssetABSettings.Instance.HotfixMetadataTimeoutSeconds;
        protected override int HotfixBundleTimeoutSeconds => FYAssetABSettings.Instance.HotfixBundleTimeoutSeconds;

        protected override IHotfixPipeline CreatePipeline()
        {
            return new ABHotfixBackend();
        }

        protected override int GetActiveHandleCount()
        {
            return HandleRegistry.ActiveCount;
        }

        protected override RuntimeMessage ShutdownPackageManager()
        {
            return ABPackageManager.Instance.Shutdown();
        }

        protected override Task<bool> FinishHotfix()
        {
            return ABPackageManager.Instance.InitializePackageAsync();
        }

    }
}

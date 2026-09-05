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

    public static Task InitializeAsync() => Flow.InitializeAsync();

    private sealed class ABHotfixFlow : HotfixFlowBase
    {
        protected override string HotfixUrl => FYAssetABSettings.Instance.HotfixUrl;
        protected override string BackendModeName => BackendModeNames.AB;
        protected override int HotfixMaxRetryCount => FYAssetABSettings.Instance.HotfixMaxRetryCount;
        protected override float HotfixRetryBaseDelaySeconds => FYAssetABSettings.Instance.HotfixRetryBaseDelaySeconds;
        protected override int HotfixMetadataTimeoutSeconds => FYAssetABSettings.Instance.HotfixMetadataTimeoutSeconds;
        protected override int HotfixBundleTimeoutSeconds => FYAssetABSettings.Instance.HotfixBundleTimeoutSeconds;

        protected override IHotfixPipeline CreatePipeline()
        {
            return new ABHotfixBackend();
        }

        protected override bool IsStandaloneMode() =>
            FYAssetSettings.Instance.StandaloneBuild;

        protected override Task<bool> FinishHotfix()
        {
            return ABPackageManager.Instance.InitializePackageAsync();
        }

    }
}

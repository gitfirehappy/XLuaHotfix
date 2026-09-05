using System;
using System.Threading.Tasks;

/// <summary>
/// AA 热更入口。
/// </summary>
public static class AAHotfixManager
{
    private static readonly AAHotfixFlow Flow = new();

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

    private sealed class AAHotfixFlow : HotfixFlowBase
    {
        protected override string HotfixUrl => FYAssetAASettings.Instance.HotfixUrl;
        protected override string BackendModeName => "AA";
        protected override int HotfixMaxRetryCount => FYAssetAASettings.Instance.HotfixMaxRetryCount;
        protected override float HotfixRetryBaseDelaySeconds => FYAssetAASettings.Instance.HotfixRetryBaseDelaySeconds;
        protected override int HotfixMetadataTimeoutSeconds => FYAssetAASettings.Instance.HotfixMetadataTimeoutSeconds;
        protected override int HotfixBundleTimeoutSeconds => FYAssetAASettings.Instance.HotfixBundleTimeoutSeconds;

        protected override IHotfixPipeline CreatePipeline()
        {
            return new AAHotfixBackend();
        }

        protected override bool IsStandaloneMode() =>
            FYAssetSettings.Instance.StandaloneBuild;

        protected override Task<bool> FinishHotfix()
        {
            return AAPackageManager.Instance.InitializePackageAsync();
        }

    }
}

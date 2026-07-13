using System;
using System.Threading.Tasks;

/// <summary>
/// 旧启动调用方的兼容门面。
/// 新增后端特定代码应直接调用 AAHotfixManager 或 ABHotfixManager。
/// </summary>
public static class HotfixManager
{
    public static event Action<string> OnStepChanged
    {
        add
        {
            AAHotfixManager.OnStepChanged += value;
            ABHotfixManager.OnStepChanged += value;
        }
        remove
        {
            AAHotfixManager.OnStepChanged -= value;
            ABHotfixManager.OnStepChanged -= value;
        }
    }

    public static event Action<float, string> OnProgress
    {
        add
        {
            AAHotfixManager.OnProgress += value;
            ABHotfixManager.OnProgress += value;
        }
        remove
        {
            AAHotfixManager.OnProgress -= value;
            ABHotfixManager.OnProgress -= value;
        }
    }

    public static event Action<string> OnError
    {
        add
        {
            AAHotfixManager.OnError += value;
            ABHotfixManager.OnError += value;
        }
        remove
        {
            AAHotfixManager.OnError -= value;
            ABHotfixManager.OnError -= value;
        }
    }

    public static event Action<string> OnWarning
    {
        add
        {
            AAHotfixManager.OnWarning += value;
            ABHotfixManager.OnWarning += value;
        }
        remove
        {
            AAHotfixManager.OnWarning -= value;
            ABHotfixManager.OnWarning -= value;
        }
    }

    public static event Action<ClientUpdateRequiredInfo> OnClientUpdateRequired
    {
        add
        {
            AAHotfixManager.OnClientUpdateRequired += value;
            ABHotfixManager.OnClientUpdateRequired += value;
        }
        remove
        {
            AAHotfixManager.OnClientUpdateRequired -= value;
            ABHotfixManager.OnClientUpdateRequired -= value;
        }
    }

    public static event Action OnFinished
    {
        add
        {
            AAHotfixManager.OnFinished += value;
            ABHotfixManager.OnFinished += value;
        }
        remove
        {
            AAHotfixManager.OnFinished -= value;
            ABHotfixManager.OnFinished -= value;
        }
    }

    public static string CurrentStepName => FYAssetSettings.Instance.UseABBackend
        ? ABHotfixManager.CurrentStepName
        : AAHotfixManager.CurrentStepName;

    public static float CurrentProgressValue => FYAssetSettings.Instance.UseABBackend
        ? ABHotfixManager.CurrentProgressValue
        : AAHotfixManager.CurrentProgressValue;

    public static Task InitializeAsync()
    {
        return FYAssetSettings.Instance.UseABBackend
            ? ABHotfixManager.InitializeAsync()
            : AAHotfixManager.InitializeAsync();
    }
}

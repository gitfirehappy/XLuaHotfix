using System;
using System.Threading.Tasks;

/// <summary>
/// Compatibility facade for old startup callers.
/// New backend-specific code should call AAHotfixManager or ABHotfixManager directly.
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

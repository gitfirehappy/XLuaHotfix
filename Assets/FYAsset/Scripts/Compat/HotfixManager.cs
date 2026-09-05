using System;
using System.Threading.Tasks;

/// <summary>
/// 旧启动调用方的兼容门面。
/// 新增后端特定代码应直接调用 AAHotfixManager 或 ABHotfixManager。
/// </summary>
public static class HotfixManager
{
    private static BackendMode? _selectedMode;

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

    public static string CurrentStepName => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.CurrentStepName
        : AAHotfixManager.CurrentStepName;

    public static float CurrentProgressValue => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.CurrentProgressValue
        : AAHotfixManager.CurrentProgressValue;

    public static Task InitializeAsync(BackendMode mode)
    {
        if (mode != BackendMode.AA && mode != BackendMode.ABManifest)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported backend mode.");

        if (_selectedMode.HasValue && _selectedMode.Value != mode)
            throw new InvalidOperationException(
                $"HotfixManager 已选择 {_selectedMode.Value}，不能切换为 {mode}。");

        _selectedMode = mode;
        return mode == BackendMode.ABManifest
            ? ABHotfixManager.InitializeAsync()
            : AAHotfixManager.InitializeAsync();
    }
}

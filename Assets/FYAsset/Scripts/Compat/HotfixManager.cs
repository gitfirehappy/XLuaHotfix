using System;
using System.Collections.Generic;
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

    private static bool _wired;
    private static readonly List<Action> FinishedSubscribers = new();

    /// <summary>
    /// facade 完成事件：先完成 facade Bind（与热更指针持久化同点完成语义），再向外部订阅者广播，
    /// 避免消费方拿到 ready 信号但 facade 还未绑定的反序。
    /// </summary>
    public static event Action OnFinished
    {
        add
        {
            FinishedSubscribers.Add(value);
            EnsureWired();
        }
        remove => FinishedSubscribers.Remove(value);
    }

    private static void EnsureWired()
    {
        if (_wired)
            return;
        _wired = true;
        AAHotfixManager.OnFinished += HandleFinished;
        ABHotfixManager.OnFinished += HandleFinished;
    }

    private static void HandleFinished()
    {
        if (_selectedMode.HasValue)
        {
            RuntimeMessage bindError = AssetPackageManager.Instance.Bind(_selectedMode.Value);
            if (bindError != null)
                throw new InvalidOperationException("[HotfixManager] facade Bind 失败: " + bindError);
        }

        for (int i = 0; i < FinishedSubscribers.Count; i++)
            FinishedSubscribers[i]();
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
        // 即使无人订阅也要接线：facade Bind 本身依赖完成回调。
        EnsureWired();
        return mode == BackendMode.ABManifest
            ? ABHotfixManager.InitializeAsync()
            : AAHotfixManager.InitializeAsync();
    }
}

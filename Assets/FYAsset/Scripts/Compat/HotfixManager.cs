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

    /// <summary>当前使用的内容归属：内置包或本地热更包。</summary>
    public static HotfixContentState CurrentContent => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.CurrentContent
        : AAHotfixManager.CurrentContent;

    /// <summary>已准备且校验通过、等待 Apply 的目标包名；没有待应用目标时为空字符串。</summary>
    public static string PreparedTargetName => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.PreparedTargetName
        : AAHotfixManager.PreparedTargetName;

    /// <summary>运行中检查是否存在可接受更新；不下载内容、不切换包根。</summary>
    public static Task<HotfixCheckResult> CheckAsync() => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.CheckAsync()
        : AAHotfixManager.CheckAsync();

    /// <summary>运行中准备目标包；当前包继续运行，只在隔离目录内写入。</summary>
    public static Task<HotfixStepResult> PrepareAsync() => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.PrepareAsync()
        : AAHotfixManager.PrepareAsync();

    /// <summary>运行中应用已准备的目标包；由业务在安全入口调用，句柄未释放时拒绝。</summary>
    public static Task<HotfixStepResult> ApplyAsync() => _selectedMode == BackendMode.ABManifest
        ? ABHotfixManager.ApplyAsync()
        : AAHotfixManager.ApplyAsync();

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

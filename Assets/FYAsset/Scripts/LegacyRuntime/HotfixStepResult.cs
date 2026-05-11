/// <summary>
/// 热更步骤的结构化执行结果 —— 替代裸 bool 返回值。
/// Success 为 true 时 Error 为 null；为 false 时 Error 携带 RuntimeMessage 诊断信息。
/// </summary>
public readonly struct HotfixStepResult
{
    public bool Success { get; }
    public RuntimeMessage Error { get; }

    private HotfixStepResult(bool success, RuntimeMessage error)
    {
        Success = success;
        Error = error;
    }

    public static HotfixStepResult Ok => new HotfixStepResult(true, null);

    public static HotfixStepResult Fail(RuntimeMessage error)
        => new HotfixStepResult(false, error);

    public static implicit operator bool(HotfixStepResult r) => r.Success;
}

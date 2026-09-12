/// <summary>
/// 热更步骤的执行结果，分别记录 Success 和可选诊断。
/// default 为失败且 Error 为空，调用方不能假定失败必有诊断。
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

    public static HotfixStepResult Ok => new(true, null);

    public static HotfixStepResult Fail(RuntimeMessage error)
        => new HotfixStepResult(false, error);

    public static implicit operator bool(HotfixStepResult r) => r.Success;
}

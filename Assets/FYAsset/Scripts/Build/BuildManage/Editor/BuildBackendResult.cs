#if UNITY_EDITOR
/// <summary>
/// 构建后端的结构化执行结果 —— 替代裸 bool + BuildSummary。
/// Success 为 true 时 Error 为 null。
/// </summary>
public class BuildBackendResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }

    private BuildBackendResult(bool success, BuildMessage error)
    {
        Success = success;
        Error = error;
    }

    public static BuildBackendResult Ok()
        => new BuildBackendResult(true, null);

    public static BuildBackendResult Fail(BuildMessage error)
        => new BuildBackendResult(false, error);
}
#endif

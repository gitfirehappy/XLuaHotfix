/// <summary>
/// BuildContext 键名常量 —— Task 代码中不出现裸字符串，全部引用此静态类。
/// 供 Editor Task 和 Runtime 消费方共同使用，放 Runtime 程序集。
/// </summary>
/// <remarks>
/// 构建只通过 Context 传递构建事实（配置、请求、模式、输出路径、校验结果与摘要）；
/// 交付清单与历史差异不进入 Context，由交付事务与发布事务各自持有。
/// </remarks>
public static class BuildContextKeys
{
    public const string BuildConfig = "BuildConfig";
    public const string BuildRequest = "BuildRequest";
    public const string BuildType = "BuildType";
    public const string OutputPath = "OutputPath";
    public const string DeferPackagePublication = "DeferPackagePublication";
    public const string BuildVerificationResult = "BuildVerificationResult";

    /// <summary>本次构建的 CompleteBuildSummary，由后端 Export 阶段写入，供构建结果面板读取</summary>
    public const string BuildSummary = "BuildSummary";

    /// <summary>本次构建开始的 UTC 时间，由运行环境写入，供 Export 阶段计算耗时</summary>
    public const string BuildStartedAtUtc = "BuildStartedAtUtc";
}

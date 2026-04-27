using System.Collections.Generic;
using System.Linq;

/// <summary>
/// CollectionScanner 的返回类型 —— 包含采集到的资源列表和扫描消息。
/// </summary>
public class ScanResult
{
    /// <summary>采集到的资源列表（所有 Package 合并）</summary>
    public List<CollectedAssetInfo> Assets = new();

    /// <summary>扫描过程中的错误和警告</summary>
    public List<ScanMessage> Messages = new();

    /// <summary>是否存在 Error 级别的消息</summary>
    public bool HasErrors => Messages.Any(m => m.Severity == ScanSeverity.Error);
}

/// <summary>
/// 扫描过程中的一条诊断消息。
/// </summary>
public class ScanMessage
{
    /// <summary>消息严重级别</summary>
    public ScanSeverity Severity;

    /// <summary>消息代码，如 CROSS_PACKAGE_OVERLAP / SAME_PATH_CONFLICT 等</summary>
    public string Code;

    /// <summary>人类可读的描述信息</summary>
    public string Message;

    /// <summary>触发该消息的 Collector 路径</summary>
    public string CollectorPath;
}

/// <summary>
/// 扫描消息严重级别。
/// </summary>
public enum ScanSeverity
{
    /// <summary>警告 —— 不阻止扫描继续</summary>
    Warning = 0,

    /// <summary>错误 —— 阻止扫描继续</summary>
    Error = 1
}

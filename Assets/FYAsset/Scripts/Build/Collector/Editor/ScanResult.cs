using System.Collections.Generic;
using System.Linq;

/// <summary>
/// CollectionScanner 的返回类型 —— 包含采集到的资源列表和构建消息。
/// 使用统一的 BuildMessage / BuildSeverity 替代旧的 ScanMessage / ScanSeverity。
/// </summary>
public class ScanResult
{
    /// <summary>采集到的资源列表（所有 Package 合并）</summary>
    public List<CollectedAssetInfo> Assets = new();

    /// <summary>扫描过程中的错误和警告</summary>
    public List<BuildMessage> Messages = new();

    /// <summary>是否存在 Error 级别的消息</summary>
    public bool HasErrors => Messages.Any(m => m.Severity == BuildSeverity.Error);
}

using System.Collections.Generic;

/// <summary>
/// CollectionScanner 的返回类型，包含显式采集资产、固定 Unity 系统来源和扫描消息。
/// </summary>
public class ScanResult
{
    private static readonly IReadOnlyList<CollectionSystemSource> FixedSystemSources =
        new List<CollectionSystemSource> { CollectionSystemSource.Resources }.AsReadOnly();

    /// <summary>显式 Collector 采集到的 AB 资源。</summary>
    public List<CollectedAssetInfo> Assets = new();

    /// <summary>固定 Unity 系统来源；不进入 Assets、Snapshot 或后续 AB 构建。</summary>
    public IReadOnlyList<CollectionSystemSource> SystemSources => FixedSystemSources;

    /// <summary>扫描过程中的错误和警告。</summary>
    public List<BuildMessage> Messages = new();
}

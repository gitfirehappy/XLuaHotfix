using System.Collections.Generic;

/// <summary>
/// 统一产物扫描接口。不同 backend 负责把自己的输入域转换为 ArtifactDigest 列表。
/// </summary>
public interface IArtifactScanner
{
    /// <summary>扫描当前输入域并返回可 Diff 的产物指纹。</summary>
    List<ArtifactDigest> Scan();
}

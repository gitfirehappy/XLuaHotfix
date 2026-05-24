using System.Collections.Generic;

/// <summary>
/// ArtifactDiffer 的三段式输出：新增、修改、删除。
/// </summary>
public class ArtifactDelta
{
    /// <summary>目标侧存在、基准侧不存在的产物。</summary>
    public List<ArtifactDigest> Added = new();

    /// <summary>两侧 Name 相同但 Hash 不同的产物。</summary>
    public List<ArtifactDigest> Modified = new();

    /// <summary>基准侧存在、目标侧不存在的产物，只需要保留 Name。</summary>
    public List<string> Removed = new();

    /// <summary>没有任何新增、修改或删除。</summary>
    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0;
}

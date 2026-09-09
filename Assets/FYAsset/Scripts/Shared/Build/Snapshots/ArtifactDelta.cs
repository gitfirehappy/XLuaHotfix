using System;
using System.Collections.Generic;

/// <summary>
/// ArtifactDiffer 的结果：目标新增/修改条目，以及基线中已移除的 Name。
/// </summary>
[Serializable]
public class ArtifactDelta
{
    /// <summary>仅目标侧存在的条目。</summary>
    public List<BuildDiffEntry> Added = new();

    /// <summary>Name 相同但 Hash 不同的目标条目。</summary>
    public List<BuildDiffEntry> Modified = new();

    /// <summary>仅基线侧存在的 Name。</summary>
    public List<string> Removed = new();

    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0;
}

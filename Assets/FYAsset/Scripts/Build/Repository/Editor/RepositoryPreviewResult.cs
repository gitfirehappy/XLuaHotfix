#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// AB preview 的组合结果。
/// HeadDelta 表示 current-vs-HEAD；DeliveryBundles 表示 current-vs-Full-baseline 的 Hotfix 交付列表。
/// </summary>
[Serializable]
public sealed class ABRepositoryPreviewResult
{
    public ArtifactDelta HeadDelta = new ArtifactDelta();
    public List<ManifestBundleEntry> DeliveryBundles = new();
    public long DeliverySizeBytes;
}
#endif

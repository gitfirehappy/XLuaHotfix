using System;
using System.Collections.Generic;

/// <summary>
/// Package 级共享提取策略。Runtime 程序集，挂载于 AssetCollectionPackage 以供 SO 序列化。
/// 决策逻辑在 Editor 程序集的 DependencyAnalyzer 中执行。
///
/// 未收集的隐式依赖一律按自身（payload + 精确类型）独立成桶。
/// MinReferenceCount / MinAssetSizeBytes / NoSharePatterns 仅为序列化兼容保留，不再被分析器消费。
/// 同一资甂同时匹配 ForceShare 与 NoShare 判为配置错误（SHAREPOLICY_CONFLICT）。
/// </summary>
[Serializable]
public class SharePolicyConfig
{
    /// <summary>仅序列化兼容保留，不参与决策。</summary>
    public int MinReferenceCount = 2;

    /// <summary>仅序列化兼容保留，不参与决策。</summary>
    public long MinAssetSizeBytes = 0;

    /// <summary>匹配列表，仅保留与 ForceSharePatterns 的冲突校验语义。</summary>
    public List<string> NoSharePatterns = new();

    /// <summary>匹配列表，仅保留与 NoSharePatterns 的冲突校验语义。</summary>
    public List<string> ForceSharePatterns = new();
}

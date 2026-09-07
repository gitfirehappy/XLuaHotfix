using System;
using System.Collections.Generic;

/// <summary>
/// Package 级共享提取策略。Runtime 程序集，挂载于 AssetCollectionPackage 以供 SO 序列化。
/// 决策逻辑在 Editor 程序集的 DependencyAnalyzer 中执行。
///
/// B05 修正版（2026-09-06）：未收集隐式依赖一律按自身（payload + 精确类型）独立成桶，
/// 旧 “复制进引用方 Bundle” 分支已废止；MinReferenceCount / MinAssetSizeBytes / NoSharePatterns
/// 三个字段仅作为序列化兼容数据保留，当前不再被分析器消费；保留语义冲突校验
/// （同时匹配 ForceShare 与 NoShare 仍视为配置错误，SHAREPOLICY_CONFLICT）。
/// </summary>
[Serializable]
public class SharePolicyConfig
{
    /// <summary>已退役：旧低引用嵌入策略的残留字段，不再参与决策。</summary>
    public int MinReferenceCount = 2;

    /// <summary>已退役：旧低引用嵌入策略的残留字段，不再参与决策。</summary>
    public long MinAssetSizeBytes = 0;

    /// <summary>匹配列表，仅保留与 ForceSharePatterns 的冲突校验语义。</summary>
    public List<string> NoSharePatterns = new();

    /// <summary>匹配列表，仅保留与 NoSharePatterns 的冲突校验语义。</summary>
    public List<string> ForceSharePatterns = new();
}

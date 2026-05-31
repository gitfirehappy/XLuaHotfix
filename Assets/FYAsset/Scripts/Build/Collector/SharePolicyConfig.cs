using System;
using System.Collections.Generic;

/// <summary>
/// Per-Package 共享提取策略。Runtime 程序集，挂载于 AssetCollectionPackage 以供 SO 序列化。
/// 决策逻辑在 Editor 程序集的 DependencyAnalyzer 中执行。
///
/// 规则冲突处理：若同一资产同时匹配 ForceSharePatterns 和 NoSharePatterns
/// 视为配置错误，抛出 SHAREPOLICY_CONFLICT。
/// </summary>
[Serializable]
public class SharePolicyConfig
{
    /// <summary>触发共享提取的最小引用 Bundle 数量，默认 2</summary>
    public int MinReferenceCount = 2;

    /// <summary>小于此值（字节）的资产不参与共享提取</summary>
    public long MinAssetSizeBytes = 0;

    /// <summary>匹配此 glob 模式的资产永不共享（强制复制）</summary>
    public List<string> NoSharePatterns = new();

    /// <summary>匹配此 glob 模式的资产强制共享（无视 MinReferenceCount/MinAssetSizeBytes）</summary>
    public List<string> ForceSharePatterns = new();
}

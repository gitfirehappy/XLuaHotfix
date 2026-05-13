using System;

/// <summary>
/// Classifier 的输出契约，由 PackRule和依赖分析消费。
/// 两个正交维度：资产的语义角色（Role）和存储方式（PayloadKind）。
/// </summary>
[Serializable]
public struct AssetClassification : IEquatable<AssetClassification>
{
    /// <summary>资产的语义角色，由 ECollectorType 映射 + 依赖分析共同确定</summary>
    public EAssetRole Role;

    /// <summary>资产的载荷类型，决定构建管线的处理路径</summary>
    public EPayloadKind PayloadKind;

    public bool Equals(AssetClassification other) =>
        Role == other.Role && PayloadKind == other.PayloadKind;

    public override bool Equals(object obj) =>
        obj is AssetClassification other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine((int)Role, (int)PayloadKind);

    public override string ToString() =>
        $"{Role}/{PayloadKind}";
}

using System;
using System.Collections.Generic;

/// <summary>
/// 序列化的 AB 资源元数据，含所属 Bundle 下标。
/// </summary>
[Serializable]
[BinarySerializable]
public class ManifestAssetEntry
{

    /// <summary>
    /// Unity GUID，用作缓存和句柄身份。
    /// </summary>
    [BinaryField(0)]
    public string EntryId;

    /// <summary>
    /// 逻辑查询键；允许重复。
    /// </summary>
    [BinaryField(1)]
    public string Address;

    /// <summary>
    /// 运行时索引使用的主类型名。
    /// </summary>
    [BinaryField(2)]
    public string PrimaryType;

    /// <summary>
    /// 运行时按大小写不敏感匹配的 Labels。
    /// </summary>
    [BinaryField(3)]
    public List<string> Labels = new();

    /// <summary>
    /// 传给 AssetBundle.LoadAsset 或 AssetDatabase.LoadAssetAtPath 的工程路径。
    /// </summary>
    [BinaryField(4)]
    public string SourcePath;

    /// <summary>
    /// 构建 Group 元数据；不是运行时查询过滤条件。
    /// </summary>
    [BinaryField(5)]
    public string Group;

    /// <summary>
    /// 是否由构建配置生成 Address。
    /// </summary>
    [BinaryField(6)]
    public bool AutoAddress = true;

    /// <summary>
    /// Index into ABManifest.BundleEntries.
    /// </summary>
    [BinaryField(7)]
    public int BundleIndex;

    /// <summary>
    /// Selects UnityEngine.Object or RawFile loading.
    /// </summary>
    [BinaryField(8)]
    public EPayloadKind PayloadKind = EPayloadKind.Serialized;

    /// <summary>
    /// 复制查询元数据与 Labels；BundleIndex 仍留在 Manifest。
    /// </summary>
    public RuntimeAssetEntry ToRuntimeEntry()
    {
        var entry = new RuntimeAssetEntry
        {
            EntryId = EntryId,
            Address = Address,
            PrimaryType = PrimaryType,
            SourcePath = SourcePath,
            Group = Group,
            AutoAddress = AutoAddress,
            PayloadKind = PayloadKind
        };
        entry.SetLabels(Labels);
        return entry;
    }
}

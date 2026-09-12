using System;
using System.Collections.Generic;

/// <summary>
/// 序列化的 AB 资源元数据，含所属内容下标。
/// </summary>
/// <remarks>
/// 公共资源（IsPublic=true）由显式 Collector 采集，带 Address 并进入公共查询索引；
/// 隐式依赖条目只服务内容构建与校验，Address 为空，不参与地址查询。
/// </remarks>
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
    /// 公共查询键；只对公共条目有意义，隐式依赖条目为空。
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
    /// 是否为公共资源：显式采集为 true，依赖分析自动发现为 false。
    /// </summary>
    [BinaryField(5)]
    public bool IsPublic;

    /// <summary>
    /// 内容类型：选择 UnityEngine.Object 加载或物理文件读取。
    /// </summary>
    [BinaryField(6)]
    public AssetContentType ContentType = AssetContentType.SerializedObject;

    /// <summary>
    /// Index into ABManifest.ContentEntries；多个条目可以指向同一个内容。
    /// </summary>
    [BinaryField(7)]
    public int ContentIndex;

    /// <summary>
    /// 复制查询元数据与 Labels；ContentIndex 仍留在 Manifest。
    /// </summary>
    public RuntimeAssetEntry ToRuntimeEntry()
    {
        var entry = new RuntimeAssetEntry
        {
            EntryId = EntryId,
            Address = Address,
            PrimaryType = PrimaryType,
            SourcePath = SourcePath,
            IsPublic = IsPublic,
            ContentType = ContentType
        };
        entry.SetLabels(Labels);
        return entry;
    }
}

using System;
using System.Collections.Generic;

/// <summary>
/// AA 包体 Manifest — 记录 AA 构建产物的版本、Bundle 信息和资源索引。
///
/// 与 ABManifest 的关系：ABManifest 是 AB 管线的运行时索引，AAManifest 是 AA 管线的包体描述。
/// 两者互斥使用（由 FYAssetSettings.UseABBackend 控制），共享相同的序列化基础设施。
/// 支持 JSON 和 Binary 两种序列化格式，Binary 由 BinarySerializable 属性驱动。
/// </summary>
[Serializable]
[BinarySerializable(Magic = 0x41414D46, SchemaVersion = 1)]
public class AAManifest
{
    /// <summary>包体版本号</summary>
    [BinaryField(0)]
    public VersionNumber Version;

    /// <summary>包体文件 Hash，用于完整性校验</summary>
    [BinaryField(1)]
    public string FileHash;

    /// <summary>所有 Bundle 的总字节数</summary>
    [BinaryField(2)]
    public long TotalSize;

    /// <summary>Bundle 列表（名称、Hash、CRC、大小）</summary>
    [BinaryField(3)]
    public List<BundleInfo> Bundles = new();

    /// <summary>AA 资源条目列表（key、Type、Labels）</summary>
    [BinaryField(4)]
    public List<PackageEntry> AssetEntries = new();

    /// <summary>Type -> Keys 索引，支持按类型查询</summary>
    [BinaryField(5)]
    public List<TypeToKeys> KeysByType = new();

    /// <summary>Label -> Keys 索引，支持按标签查询</summary>
    [BinaryField(6)]
    public List<LabelToKeys> KeysByLabel = new();
}

/// <summary>
/// Bundle 元信息 — 对应包体目录中的单个 .bundle 文件。
/// </summary>
[Serializable]
[BinarySerializable]
public class BundleInfo
{
    /// <summary>Bundle 文件名（如 "hotfixgroup_assets_xxx.bundle"）</summary>
    [BinaryField(0)]
    public string BundleName;

    /// <summary>Bundle 文件 SHA256 Hash</summary>
    [BinaryField(1)]
    public string FileHash;

    /// <summary>Bundle 文件 CRC32 校验值</summary>
    [BinaryField(2)]
    public uint FileCRC;

    /// <summary>Bundle 文件字节数</summary>
    [BinaryField(3)]
    public long FileSize;
}

/// <summary>
/// 类型 -> Keys 映射，用于 GetKeysByType 查询。
/// </summary>
[Serializable]
[BinarySerializable]
public class TypeToKeys
{
    /// <summary>类型名（取第一个 Label，无 Label 时为 "Untyped"）</summary>
    [BinaryField(0)]
    public string Type;

    /// <summary>该类型下的所有 Addressable Key</summary>
    [BinaryField(1)]
    public List<string> Keys = new();
}

/// <summary>
/// 标签 -> Keys 映射，用于 GetKeysByLabel 查询。
/// </summary>
[Serializable]
[BinarySerializable]
public class LabelToKeys
{
    /// <summary>标签名</summary>
    [BinaryField(0)]
    public string Label;

    /// <summary>该标签下的所有 Addressable Key</summary>
    [BinaryField(1)]
    public List<string> Keys = new();
}

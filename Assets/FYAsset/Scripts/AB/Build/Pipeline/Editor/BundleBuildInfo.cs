using System.Collections.Generic;

/// <summary>
/// BuildABContentTask 的输出 —— 描述单个构建产物的元数据。
/// 后续由 ABManifest 生成 Task 消费，映射为 ManifestContentEntry。
/// </summary>
public class BundleBuildInfo
{
    /// <summary>逻辑内容名，不含 hash 后缀；同一名称的成员物理上属于同一内容</summary>
    public string BundleName;

    /// <summary>实际输出文件名：BundleNameBuilder 生成的物理名（含 hash 与可读段）</summary>
    public string OutputFileName;

    /// <summary>Unity BuildPipeline 产出的内容哈希</summary>
    public string Hash;

    /// <summary>产物 CRC32；与 Hash/Size 一起构成复用时的制品校验事实</summary>
    public uint CRC;

    /// <summary>文件大小（字节）</summary>
    public long Size;

    /// <summary>
    /// 构建输入指纹；写入 Summary 后作为后续构建判定复用的依据。
    /// 成员无法采集变化检测输入的内容为空，其制品不参与复用。
    /// </summary>
    public string InputFingerprint;

    /// <summary>此 Bundle 包含的所有资产路径</summary>
    public List<string> AssetPaths = new();

    /// <summary>主导内容类型（SerializedObject / Scene / RawFile）</summary>
    public AssetContentType ContentType;

    /// <summary>
    /// 内容级直接依赖的输出文件名集合；事实来源是 Unity AssetBundleManifest，复用内容取缓存回放。
    /// 依赖预期图只作预期诊断，不作为依赖下标的事实来源。
    /// </summary>
    public List<string> DependencyFileNames = new();
}

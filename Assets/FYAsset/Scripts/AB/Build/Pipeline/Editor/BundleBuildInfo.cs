using System.Collections.Generic;

/// <summary>
/// TaskBuildBundles 的输出 —— 描述单个构建产物的元数据。
/// 后续由 ABManifest 生成 Task 消费，映射为 ManifestBundleEntry。
/// </summary>
public class BundleBuildInfo
{
    /// <summary>逻辑 Bundle 名（如 "hotfix_ui_abc123"），不含 hash/后缀</summary>
    public string BundleName;

    /// <summary>实际输出文件名（如 "hotfix_ui_abc123_md5hash.bundle"）</summary>
    public string OutputFileName;

    /// <summary>Unity BuildPipeline 产出的内容哈希</summary>
    public string Hash;

    /// <summary>文件大小（字节）</summary>
    public long Size;

    /// <summary>此 Bundle 包含的所有资产路径</summary>
    public List<string> AssetPaths = new();

    /// <summary>主导载荷类型（Serialized / Scene / RawFile）</summary>
    public EPayloadKind PayloadKind;
}

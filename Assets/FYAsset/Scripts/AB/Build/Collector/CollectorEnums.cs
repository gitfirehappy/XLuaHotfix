/// <summary>
/// 采集路径类型 —— Collector 可绑定目录或单个文件。
/// </summary>
public enum ECollectPathType
{
    /// <summary>按目录递归采集资源</summary>
    Folder = 0,

    /// <summary>仅采集指定的单个文件</summary>
    File = 1
}

/// <summary>
/// 资产内容类型 —— 由分类器按分类顺序推断，决定构建与运行时加载路线。
/// </summary>
/// <remarks>
/// 分类顺序：RawFile 白名单 → Scene → Unity 可有效识别的序列化资产 → 其余一律 RawFile。
/// SerializedObject 与 Scene 进入 Unity AssetBundle 构建，RawFile 只做物理文件拷贝。
/// </remarks>
public enum AssetContentType
{
    /// <summary>标准序列化资产（Prefab / Texture / Material 等），打入 AssetBundle</summary>
    SerializedObject = 0,

    /// <summary>场景文件，Unity 要求独立打包为 Scene Bundle</summary>
    Scene = 1,

    /// <summary>原始文件，直接拷贝，不打入 AssetBundle</summary>
    RawFile = 2
}

/// <summary>
/// 资产进入构建集合的来源 —— 只服务构建诊断，不进入运行时 Manifest。
/// </summary>
/// <remarks>
/// 显式 Collector 采集（含框架内置补入）即公共资源；依赖分析自动发现的资源全部内部化，
/// 只作为引用方或共享内容的组成部分存在。
/// </remarks>
public enum AssetDependencyOrigin
{
    /// <summary>显式采集的资产，进入公共 Address 索引</summary>
    Explicit = 0,

    /// <summary>依赖分析自动发现的资产，只参与构建，不进入公共索引</summary>
    Implicit = 1
}

/// <summary>
/// Group 上配置的 Addressables 风格 Bundle 打包模式。
/// </summary>
public enum BundlePackingMode
{
    /// <summary>Group 内所有资产打入同一个 Bundle</summary>
    PackTogether = 0,

    /// <summary>每个资产单独打入一个 Bundle</summary>
    PackSeparately = 1,

    /// <summary>按资产最终 Label 集合分组打包</summary>
    PackTogetherByLabel = 2
}

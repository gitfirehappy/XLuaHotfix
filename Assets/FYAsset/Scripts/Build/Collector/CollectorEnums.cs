/// <summary>
/// 采集器类型 —— 用户对资产参与构建方式的意图配置。
/// </summary>
public enum ECollectorType
{
    /// <summary>可寻址入口资产，运行时通过 Address 加载</summary>
    Main = 0,
    
    /// <summary>内部打包资产，不对外暴露 Address</summary>
    Static = 1,

    /// <summary>仅作为依赖项打包，不直接加载</summary>
    Depend = 2,

    /// <summary>由依赖分析自动发现的隐式依赖资产，无用户声明</summary>
    Implicit = 3
}

/// <summary>
/// 资产载荷类型 —— 由 Classifier 自动推断，决定构建管线的处理路径。
/// </summary>
public enum EPayloadKind
{
    /// <summary>标准序列化资产（Prefab / Texture / Material 等），打入 AssetBundle</summary>
    Serialized = 0,

    /// <summary>原始文件，直接拷贝，不打入 AssetBundle</summary>
    RawFile = 1,

    /// <summary>场景文件，Unity 要求独立打包为 Scene Bundle</summary>
    Scene = 2
}

/// <summary>
/// 资产角色 —— 由 ECollectorType 映射 + 依赖分析（E4）共同确定的最终语义角色。
/// </summary>
public enum EAssetRole
{
    /// <summary>来自 Main 采集器的可寻址入口资产</summary>
    Main = 0,

    /// <summary>来自 Static 采集器的内部打包资产</summary>
    Static = 1,

    /// <summary>来自 Depend 采集器的显式声明依赖资产</summary>
    Depend = 2,

    /// <summary>由依赖分析发现的隐式依赖资产</summary>
    ImplicitDependency = 3
}

/// <summary>
/// 用户强制指定的载荷类型 —— 覆盖 Classifier 的自动推断结果。
/// Auto 表示完全由 Classifier 决定。
/// </summary>
public enum EForcePayloadKind
{
    /// <summary>由 Classifier 自动推断</summary>
    Auto = 0,

    /// <summary>强制视为序列化资产</summary>
    Serialized = 1,

    /// <summary>强制视为原始文件</summary>
    RawFile = 2,
    
    /// <summary>强制视为场景文件</summary>
    Scene = 3
}

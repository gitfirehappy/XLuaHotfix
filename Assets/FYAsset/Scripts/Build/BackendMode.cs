/// <summary>
/// 构建管线后端模式 —— 决定 AssetBundle 构建的数据源和 manifest 格式。
/// 由 BuildPipelineConfig.DefaultBackendMode 配置，命令行 --backend 可覆盖。
/// DAG W-W 冲突检测保证单一 Task 独占写入此 Key。
/// </summary>
public enum BackendMode
{
    /// <summary>基于 AddressableLabelsConfig / VersionState 的旧版后端</summary>
    LegacyAddressable = 0,

    /// <summary>基于 ABManifest 的新版后端，后续 Task 默认使用此模式</summary>
    ABManifest = 1
}

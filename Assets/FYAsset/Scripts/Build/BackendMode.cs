/// <summary>
/// 构建管线后端模式 —— 决定 AssetBundle 构建的数据源和 manifest 格式。
/// 实际来源为 FYAssetSettings.Instance.UseABBackend，CLI --backend 可局部覆盖。
/// DAG W-W 冲突检测保证单一 Task 独占写入此 Key。
/// </summary>
public enum BackendMode
{
    /// <summary>基于 AAManifest 的 AA 后端</summary>
    AA = 0,

    /// <summary>基于 ABManifest 的新版后端，后续 Task 默认使用此模式</summary>
    ABManifest = 1
}

using UnityEngine;

/// <summary>
/// 宿主运行时资源后端选择。仅保存选择数据，不负责初始化或路由。
/// </summary>
[CreateAssetMenu(menuName = "FYAsset/Backend Settings", fileName = "FYAssetBackendSettings")]
public sealed class FYAssetBackendSettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetBackendSettings.asset";

    [Header("Backend")]
    public BackendMode Backend = BackendMode.Unspecified;
}

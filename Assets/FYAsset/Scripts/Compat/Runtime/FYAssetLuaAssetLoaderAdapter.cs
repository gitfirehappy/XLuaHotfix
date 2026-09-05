using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// FYAsset 资产管理 facade（AssetPackageManager）到 XLuaFramework 资源服务口的适配器。
/// 错误形态从 RuntimeMessage 降级为 string（消费侧约定：只用 ToString/非空判断）。
/// 由宿主启动壳（GameLauncher）显式注入 LuaAssetRuntime。
/// </summary>
public sealed class FYAssetLuaAssetLoaderAdapter : ILuaAssetLoader
{
    public async Task<(T asset, string error)> LoadAssetAsync<T>(string address) where T : Object
    {
        var (asset, error) = await AssetPackageManager.Instance.LoadAssetAsync<T>(address);
        return (asset, error?.ToString());
    }

    public (T asset, string error) LoadAssetSync<T>(string address) where T : Object
    {
        var (asset, error) = AssetPackageManager.Instance.LoadAssetSync<T>(address);
        return (asset, error?.ToString());
    }

    public void UnloadAsset<T>(string address) where T : Object
    {
        AssetPackageManager.Instance.UnloadAsset<T>(address);
    }
}

using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 应用层资源加载 facade：启动时绑定一次 backend，之后统一转发 AA/AB。
/// </summary>
public class AssetPackageManager : Singleton<AssetPackageManager>
{
    private BackendMode? _boundMode;

    public RuntimeMessage Bind(BackendMode mode)
    {
        if (mode != BackendMode.AA && mode != BackendMode.ABManifest)
            return RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, $"无效 backend mode: {mode}");

        if (!_boundMode.HasValue)
        {
            _boundMode = mode;
            Debug.Log($"[AssetPackageManager] 已绑定 backend: {BackendModeNames.FromBackendMode(mode)}");
            return null;
        }

        if (_boundMode.Value == mode)
            return null;

        return RuntimeMessage.Error(
            RuntimeErrorCodes.InvalidArgument,
            $"AssetPackageManager 已绑定 {_boundMode.Value}，不能切换为 {mode}");
    }

    public Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
            return Task.FromResult<(T asset, RuntimeMessage error)>((
                null,
                RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend")));

        return _boundMode.Value == BackendMode.AA
            ? AAPackageManager.Instance.LoadAssetAsync<T>(address)
            : ABPackageManager.Instance.LoadAssetAsync<T>(address);
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
            return (null, RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend"));

        return _boundMode.Value == BackendMode.AA
            ? AAPackageManager.Instance.LoadAssetSync<T>(address)
            : ABPackageManager.Instance.LoadAssetSync<T>(address);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        if (!_boundMode.HasValue)
        {
            Debug.LogError(RuntimeMessage.LoadFailed(address, "AssetPackageManager 尚未绑定 backend").ToString());
            return;
        }

        if (_boundMode.Value == BackendMode.AA)
            AAPackageManager.Instance.UnloadAsset<T>(address);
        else
            ABPackageManager.Instance.UnloadAsset<T>(address);
    }
}

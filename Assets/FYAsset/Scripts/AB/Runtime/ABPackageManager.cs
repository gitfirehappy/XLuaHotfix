using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB runtime package loading entrypoint.
/// </summary>
public sealed class ABPackageManager : PackageManagerBase
{
    private static readonly object LockObject = new();
    private static ABPackageManager _instance;

    public static ABPackageManager Instance
    {
        get
        {
            lock (LockObject)
            {
                return _instance ??= new ABPackageManager();
            }
        }
    }

    protected override async Task<bool> InitializeBackendAsync()
    {
        var manifest = await ABManifestLoader.LoadAsync();
        if (manifest == null)
        {
            Debug.LogWarning(
                "[ABPackageManager] ABManifest 加载失败。请检查 AB 资源是否已正确构建并部署到热更目录或 StreamingAssets。");
            return false;
        }

        _index = new ABAssetIndex(manifest);

        var bundleLoader = new ABBundleLoader(manifest);
        _backend = new ABPackageBackend(manifest, bundleLoader);

        BuildQueryCaches(_index.GetAllEntries());

        Debug.Log(
            $"[ABPackageManager] AB 全链路初始化完成。" +
            $"Assets: {manifest.AssetCount}, Bundles: {manifest.BundleCount}, " +
            "Index: ABAssetIndex, Backend: ABPackageBackend");
        return true;
    }
}

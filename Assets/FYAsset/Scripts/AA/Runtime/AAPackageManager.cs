using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AA runtime package loading entrypoint.
/// </summary>
public sealed class AAPackageManager : PackageManagerBase
{
    private static readonly object LockObject = new();
    private static AAPackageManager _instance;

    public static AAPackageManager Instance
    {
        get
        {
            lock (LockObject)
            {
                return _instance ??= new AAPackageManager();
            }
        }
    }

    protected override async Task<bool> InitializeBackendAsync()
    {
        _backend = new AddressablesBackend();

        var manifest = await AAManifestLoader.LoadAsync();
        bool success = TryInitializeAAIndexFromAAManifest(manifest);
        if (!success)
            Debug.LogError("[AAPackageManager] AA AAManifest 索引初始化失败。");

        return success;
    }
}

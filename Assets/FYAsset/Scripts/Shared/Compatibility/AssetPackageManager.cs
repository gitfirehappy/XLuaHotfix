using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Compatibility facade for old runtime callers.
/// New backend-specific code should call AAPackageManager or ABPackageManager directly.
/// </summary>
public class AssetPackageManager : Singleton<AssetPackageManager>
{
    private PackageManagerBase _activeManager;

    private PackageManagerBase CurrentManager =>
        _activeManager ?? (FYAssetSettings.Instance.UseABBackend
            ? ABPackageManager.Instance
            : AAPackageManager.Instance);

    public async Task Initialize()
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            bool abReady = await ABPackageManager.Instance.InitializePackageAsync();
            if (abReady)
            {
                _activeManager = ABPackageManager.Instance;
                return;
            }

            UnityEngine.Debug.LogWarning(
                "[AssetPackageManager] ABManifest 加载失败，回退到 AA 路径。请检查 AB 资源是否已正确构建并部署。");
        }

        await AAPackageManager.Instance.InitializePackageAsync();
        _activeManager = AAPackageManager.Instance;
    }

    public IReadOnlyList<string> GetKeysByType(string type) => CurrentManager.GetKeysByType(type);

    public IReadOnlyList<string> GetKeysByLabel(string label) => CurrentManager.GetKeysByLabel(label);

    public List<string> GetKeysByLabels(string[] labels) => CurrentManager.GetKeysByLabels(labels);

    public List<string> GetKeysByTypeAndLabel(string type, string label) =>
        CurrentManager.GetKeysByTypeAndLabel(type, label);

    public bool ContainsKey(string key) => CurrentManager.ContainsKey(key);

    public Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object =>
        CurrentManager.LoadAssetAsync<T>(key);

    public Task<List<T>> LoadAssetByLabelAsync<T>(string label) where T : UnityEngine.Object =>
        CurrentManager.LoadAssetByLabelAsync<T>(label);

    public Task<List<T>> LoadAssetByLabelsAsync<T>(string[] labels) where T : UnityEngine.Object =>
        CurrentManager.LoadAssetByLabelsAsync<T>(labels);

    public void UnloadAsset(string key) => CurrentManager.UnloadAsset(key);

    public void UnloadAssetByLabel(string label) => CurrentManager.UnloadAssetByLabel(label);

    public void UnloadAssetsByLabels(string[] labels) => CurrentManager.UnloadAssetsByLabels(labels);

    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object =>
        CurrentManager.LoadAssetSync<T>(key);

    public Task<byte[]> LoadRawBytesAsync(string address, IReadOnlyList<string> labels = null) =>
        CurrentManager.LoadRawBytesAsync(address, labels);

    public byte[] LoadRawBytesSync(string address, IReadOnlyList<string> labels = null) =>
        CurrentManager.LoadRawBytesSync(address, labels);

    public Task<string> LoadRawTextAsync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null) =>
        CurrentManager.LoadRawTextAsync(address, labels, encoding);

    public string LoadRawTextSync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null) =>
        CurrentManager.LoadRawTextSync(address, labels, encoding);

    public Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object =>
        CurrentManager.LoadByAddress<T>(address);

    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object =>
        CurrentManager.LoadByAddressSync<T>(address);

    public Task<AssetHandle<T>> LoadByTypeKey<T>(
        string key,
        IReadOnlyList<string> labels = null) where T : UnityEngine.Object =>
        CurrentManager.LoadByTypeKey<T>(key, labels);

    public AssetHandle<T> LoadByTypeKeySync<T>(
        string key,
        IReadOnlyList<string> labels = null) where T : UnityEngine.Object =>
        CurrentManager.LoadByTypeKeySync<T>(key, labels);
}

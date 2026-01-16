using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets.ResourceLocators;

/// <summary>
/// 合并更新下载的Catalog
/// </summary>
public static class CatalogUpdater
{
    private static bool _transformInstalled = false;

    /// <summary>
    /// 加载 HotfixRoot下的外部 Catalog
    /// </summary>
    public static async Task<bool> LoadExternalCatalog(string catalogFullPath)
    {
        if (!File.Exists(catalogFullPath))
        {
            Debug.LogError($"[CatalogUpdater] Catalog 文件不存在：{catalogFullPath}");
            return false;
        }

        InstallInternalIdRedirect();

        Debug.Log($"[CatalogUpdater] 正在加载外部 Catalog: {catalogFullPath}");

        AsyncOperationHandle<IResourceLocator> handle =
            Addressables.LoadContentCatalogAsync(catalogFullPath);

        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[CatalogUpdater] Catalog 加载失败：{handle.OperationException}");
            return false;
        }

        IResourceLocator locator = handle.Result;

        Debug.Log($"[CatalogUpdater] Catalog 加载成功: {locator.LocatorId}, Keys 数量: {locator.Keys.Count()}");

        // 注意：不能 Addressables.Release(handle)
        // 否则 catalog 会被卸载，热更失效
        return true;
    }
    
    /// <summary>
    /// InternalId 路径重定向，热更后的资源
    /// </summary>
    private static void InstallInternalIdRedirect()
    {
        if (_transformInstalled) return;

        Addressables.ResourceManager.InternalIdTransformFunc = (location) =>
        {
            string id = location.InternalId;

            // 如果 internalId 是 HTTP(S)，说明来自 remote catalog
            if (id.StartsWith("http"))
            {
                string fileName = Path.GetFileName(id);
                // 所有有效资源位于LocalBundleRoot
                string localPath = Path.Combine(PathManager.LocalBundleRoot, fileName);

                // 如果本地已有下载的包，则强制使用本地路径
                if (File.Exists(localPath))
                {
                    return localPath;
                }
            }
            return id;
        };

        _transformInstalled = true;
        Debug.Log("[CatalogUpdater] 已安装 InternalId 路径重定向函数");
    }
}

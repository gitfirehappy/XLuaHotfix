using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets.ResourceLocators;

/// <summary>
/// 用外部 catalog 替换 Addressables locator，并重定向 Bundle 路径。
/// </summary>
public static class CatalogUpdater
{
    private static bool _transformInstalled = false;

    /// <summary>
    /// 从给定路径加载 catalog，并移除其余 locator。
    /// </summary>
    /// <remarks>
    /// 此方法不释放 catalog operation handle，也不卸载已加载的资源对象。
    /// 文件缺失或 operation 状态失败时返回 false；await 或 Addressables 抛出的异常由调用方处理。
    /// </remarks>
    public static async Task<bool> LoadExternalCatalog(string catalogFullPath)
    {
        if (!FileHelper.Exists(catalogFullPath))
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

        IResourceLocator newLocator = handle.Result;

        Debug.Log($"[CatalogUpdater] Catalog 加载成功: {newLocator.LocatorId}, Keys 数量: {newLocator.Keys.Count()}");

        // 只保留新 catalog 的定位器，避免相同 Key 仍解析到旧配置。
        var locators = Addressables.ResourceLocators.ToList();
        foreach (var loc in locators)
        {
            if (loc == newLocator) continue;

            Debug.Log($"[CatalogUpdater] 移除旧定位器: {loc.LocatorId}，确保热更生效。");
            Addressables.RemoveResourceLocator(loc);
        }

        return true;
    }
    
    /// <summary>
    /// 安装 InternalId 重定向：remote URL 映射到当前激活包根 bundles 目录下的内容文件。
    /// </summary>
    /// <remarks>
    /// 只映射一个包根 RuntimePathManager.ActivePackageRoot；激活包根下不存在该文件时保留原 InternalId，
    /// 由 Addressables 自己报错，不做逐文件回退到其他目录。
    /// </remarks>
    public static void InstallInternalIdRedirect()
    {
        if (_transformInstalled) return;

        Addressables.ResourceManager.InternalIdTransformFunc = (location) =>
        {
            string id = location.InternalId;

            if (FYAssetPathUtility.IsHttpUrl(id))
            {
                string fileName = Path.GetFileName(id);
                string localPath = FYAssetPathUtility.JoinFilePath(
                    RuntimePathManager.ActivePackageRoot,
                    FYAssetSettings.BUNDLES_DIRECTORY_NAME,
                    fileName);

                if (FileHelper.Exists(localPath))
                {
                    // 使用 file URI，避免 Windows 盘符被 Provider 当作 URI 端口解析。
                    return new System.Uri(localPath).AbsoluteUri;
                }
            }
            return id;
        };

        _transformInstalled = true;
        Debug.Log("[CatalogUpdater] 已安装 InternalId 路径重定向函数");
    }
}

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

        // 移除旧的定位器
        // Addressables 默认会优先查询第一个加载的 Locator。
        // 如果不移除内置的 Locator，系统会一直使用包体内的旧资源配置。
        var locators = Addressables.ResourceLocators.ToList();
        foreach (var loc in locators)
        {
            // 跳过刚刚加载的这个新 Locator
            if (loc == newLocator) continue;

            // 移除默认的 "AddressablesMainContent" 或其他旧的 Locator
            // 这样 Addressables 在解析 Key 时，只能查阅新的 Locator
            Debug.Log($"[CatalogUpdater] 移除旧定位器: {loc.LocatorId}，确保热更生效。");
            Addressables.RemoveResourceLocator(loc);
        }

        // 注意：不能 Addressables.Release(handle)，否则新 catalog 会被卸载
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
                // 所有有效资源位于 CurrentGUIDRoot/bundles 下
                string localPath = Path.Combine(RuntimePathManager.CurrentGUIDRoot, "bundles", fileName);

                // 如果本地已有下载的包，则强制使用本地路径
                if (FileHelper.Exists(localPath))
                {
                    // 必须转换为 URI 格式 (file://)，否则 Windows 平台下 AssetBundleProvider 创建 Uri 时会报 "Invalid port specified"
                    return new System.Uri(localPath).AbsoluteUri;
                }
            }
            return id;
        };

        _transformInstalled = true;
        Debug.Log("[CatalogUpdater] 已安装 InternalId 路径重定向函数");
    }
}

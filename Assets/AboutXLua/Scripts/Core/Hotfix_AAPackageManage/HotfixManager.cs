using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 热更管理器，控制总流程
/// </summary>
public static class HotfixManager
{
    private static readonly string _hotfixUrl = Constants.HOTFIX_URL;
    
    // 固定下载 manifest 动态获取路径
    private static readonly string _manifestUrl = $"{_hotfixUrl}/manifest.json";
    
    private static string _remoteUrlRoot;
    
    public async static Task InitializeAsync()
    {
        // 1. 初始化 Addressable 本地包
        // 初始化 Addressables 系统，加载本地 Catalog
        var initHandle = Addressables.InitializeAsync();
        await initHandle.Task;
        
        if (initHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[HotfixManager] Addressables 初始化失败: {initHandle.OperationException}");
            return;
        }
        Debug.Log("[HotfixManager] Addressables 本地包初始化成功");
        
        // 2. 加载 BuildIndex，并初始化路径 (从 Local AA 包中)
        var indexHandle = Addressables.LoadAssetAsync<BuildIndex>(Constants.BUILD_INDEX);
        BuildIndex buildIndex = await indexHandle.Task;

        if (indexHandle.Status != AsyncOperationStatus.Succeeded || buildIndex == null)
        {
            Debug.LogError("[HotfixManager] 致命错误：无法加载 BuildIndex！无法确定版本路径。");
            return;
        }
        
        PathManager.Initialize(buildIndex);
        PathManager.EnsureDirectories();
        
        // 3. 获取 manifest.json，确定下载路径
        string manifestJson = await NetworkDownloader.Instance.DownloadText(_manifestUrl);
        if (string.IsNullOrEmpty(manifestJson))
        {
            Debug.LogError("[HotfixManager] 无法获取manifest.json，使用本地资源运行。");
            await FinishHotfix();
            return;
        }
        
        Manifest manifest = JsonUtility.FromJson<Manifest>(manifestJson);
        if (string.IsNullOrEmpty(manifest.latestPackage))
        {
            Debug.LogError("[HotfixManager] manifest.json 无效，使用本地资源运行。");
            await FinishHotfix();
            return;
        }
        
        string packagePath = manifest.latestPackage;
        _remoteUrlRoot = $"{_hotfixUrl}/Packages/{packagePath}";
    
        Debug.Log($"[HotfixManager] 获取最新包体: {packagePath}，URL已更新: {_remoteUrlRoot}");
        
        // 4. 加载本地 version_state.json
        string localVersionStatePath = Path.Combine(PathManager.LocalRoot, "version_state.json");
        VersionState localVersionState = null;
        
        if (File.Exists(localVersionStatePath))
        {
            ParseJson<VersionState>(File.ReadAllText(localVersionStatePath),out var localstate);
            localVersionState = localstate;
            Debug.Log($"[HotfixManager] 本地版本: {localVersionState?.version}, Hash: {localVersionState?.hash}");
        }
        
        // 5. 下载远端 version_state.json
        string remoteVersionUrl = $"{_remoteUrlRoot}/version_state.json";
        string remoteVersionJson = await NetworkDownloader.Instance.DownloadText(remoteVersionUrl);
        
        if (string.IsNullOrEmpty(remoteVersionJson))
        {
            Debug.LogError("[HotfixManager] 无法获取远端版本信息，将使用本地资源运行。");
            await FinishHotfix();
            return;
        }
        
        ParseJson<VersionState>(remoteVersionJson,out var remoteVersionState);
        Debug.Log($"[HotfixManager] 远端版本: {remoteVersionState?.version}");
        
        // 如果是大版本更新，则强制清理所有热更目录
        // 检查本地整包是否是大版本包，拿BuildIndex查（整包唯一）
        if (localVersionState.version.Major != remoteVersionState.version.Major)
        {
            if (buildIndex.Version == remoteVersionState.version)
            {
                Debug.Log($"[HotfixManager] 检测到大版本更新，执行全量清理。版本：{buildIndex.Version}");
                // 强制清理所有热更目录 (Local, Remote)
                PackageCleaner.ClearAllHotfix();   
            }
            else
            {
                Debug.LogError($"[HotfixManager] 检测到整包版本不一致，请下载最新整包");
            }
        }
        
        Debug.Log($"[HotfixManager] 此次需下载Bundle数: {remoteVersionState.bundles.Count}, 总大小: {remoteVersionState.totalSize}");
        
        // 6. 下载所有的远端 bundle 到 RemoteRoot （暂存远端文件）
        string remoteBundleRoot = PathManager.TempBundleRoot;
        if (!Directory.Exists(remoteBundleRoot)) Directory.CreateDirectory(remoteBundleRoot);
        
        var task = new List<Task<bool>>();
        foreach (var bundleInfo in remoteVersionState.bundles)
        {
            string bundleUrl = $"{_remoteUrlRoot}/bundles/{bundleInfo.bundleName}";
            string savePath = Path.Combine(remoteBundleRoot, bundleInfo.bundleName);
            
            // 简单并行下载
            task.Add(NetworkDownloader.Instance.DownloadFile(bundleUrl, savePath));
        }
        await Task.WhenAll(task);
        if (task.Any(t => !t.Result))
        {
            Debug.LogError("[HotfixManager] 存在下载失败的 bundle，请检查网络！");
            return; // 直接终止
        }
        
        // 7. 下载 catalog.json
        string catalogUrl = $"{_remoteUrlRoot}/catalog.json";
        await NetworkDownloader.Instance.DownloadFile(catalogUrl, Path.Combine(PathManager.TempRoot, "catalog.json"));
        
        // 保存新的 version_state.json 到 TempRoot
        File.WriteAllText(Path.Combine(PathManager.TempRoot, "version_state.json"), remoteVersionJson);
        
        Debug.Log("[HotfixManager] 热更资源下载完成，开始应用热更...");
        
        // 8. 拿version_state中的删除名单比对，删除并更新文件
        PackageCleaner.ApplyUpdate(remoteVersionState.deleteList, PathManager.TempRoot, PathManager.LocalRoot);
        
        // 9. 加载新的 catalog，此时LocalRoot中的catalog.json已经是最新
        Debug.Log("[HotfixManager] 加载新的远端 Catalog...");
        string localCatalogPath = Path.Combine(PathManager.LocalRoot, "catalog.json");
        
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);

        if (catalogLoaded) Debug.Log("[HotfixManager] 热更流程成功完成！");
        
        await FinishHotfix();
    }

    private static async Task FinishHotfix()
    {
        // 加载 AddressableLabelsConfig（依赖更新后的HelperBuildData）
        await AAPackageManager.Instance.Initialize();
    }
    
    /// <summary>
    /// 将Json 解析为需要对象
    /// </summary>
    private static bool ParseJson<T>(string json, out T result)
    {
        result = default;
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            result = JsonUtility.FromJson<T>(json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotfixManager] JSON 解析失败: {e.Message}");
            return false;
        }
    }
}

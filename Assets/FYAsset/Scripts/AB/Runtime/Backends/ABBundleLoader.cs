using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// AB Bundle 加载器 — 负责 AssetBundle 文件的加载、卸载与依赖管理。
///
/// 设计说明：
/// - 替代 Addressables 内部的 BundleProvider，将 Bundle 加载逻辑从黑盒变为白盒
/// - 通过 ABManifest 查询 Bundle 依赖关系，递归加载所有依赖 Bundle
/// - 维护 Bundle 级缓存与引用计数，RefCount=0 时调用 AssetBundle.Unload(true)
/// - 路径策略与 ABManifestLoader 一致：热更目录优先，StreamingAssets 回退
/// - 同步/异步保留各自 I/O 路径，共享 BundleName 级物理加载 single-flight
/// - 不依赖 Addressables — 零 Addressables 引用
///
/// 使用方式：
/// 1. 由 ABPackageBackend 创建并持有
/// 2. ABPackageBackend 通过 ABManifest 查出 BundleName 后调用 LoadBundle/LoadBundleAsync
/// 3. 资源释放时调用 UnloadBundle，引用计数归零后自动卸载 Bundle 及其依赖
/// </summary>
public class ABBundleLoader
{
    #region 内部数据结构

    /// <summary>
    /// Bundle 缓存条目 — 记录已加载的 AssetBundle 及其引用状态。
    /// </summary>
    private class BundleCacheEntry
    {
        /// <summary>已加载的 AssetBundle 实例</summary>
        public AssetBundle Bundle;

        /// <summary>
        /// 引用计数。每次 LoadBundle 时 +1，每次 UnloadBundle 时 -1。
        /// 降至 0 时执行 AssetBundle.Unload(true) 并移除缓存。
        /// </summary>
        public int RefCount;

        /// <summary>
        /// 该 Bundle 的直接依赖 Bundle 名称列表。
        /// 卸载时需要递归减少依赖 Bundle 的引用计数。
        /// </summary>
        public string[] DependencyBundleNames;
    }

    /// <summary>
    /// 正在进行的物理 Bundle 加载。依赖已由 leader 获取；followers 只共享物理请求和最终结果。
    /// </summary>
    private sealed class BundleLoadOperation
    {
        public int PendingAcquireCount;
        public string[] DependencyBundleNames;
        public string BundlePath;
        public AssetBundleCreateRequest LocalRequest;
        public Action<AsyncOperation> CompletionHandler;
        public TaskCompletionSource<(AssetBundle bundle, RuntimeMessage error)> Completion;
        public bool IsFinalized;
        public AssetBundle Bundle;
        public RuntimeMessage Error;
    }

    #endregion

    #region 字段

    /// <summary>Bundle 缓存：BundleName → CacheEntry</summary>
    /// 缓存策略决策：当前使用精确引用计数 + 归零即卸载（Unload(true)）。
    /// 该策略下 RefCount=0 的 Bundle 会被立即释放，不会累积。
    /// 主要内存风险来自引用泄漏（Load 后未 Release），由 B8 AssetHandle pool 的 Handle-first 设计解决。
    /// LRU/LFU 缓存优化需要 profiling 数据驱动参数（容量、超时阈值），不做过早优化。
    private readonly Dictionary<string, BundleCacheEntry> _bundleCache = new();

    /// <summary>正在进行的物理加载：BundleName → leader operation。</summary>
    private readonly Dictionary<string, BundleLoadOperation> _bundleInflightLoads = new();

    /// <summary>ABManifest 引用，用于查询 Bundle 依赖关系</summary>
    private readonly ABManifest _manifest;

    #endregion

    #region 构造

    /// <summary>
    /// 创建 ABBundleLoader 实例。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest（由 ABManifestLoader 加载）</param>
    public ABBundleLoader(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    #endregion

    #region 同步 API

    /// <summary>
    /// 同步加载 Bundle（含依赖）。
    /// 如果已缓存则直接增加引用计数并返回。
    /// </summary>
    /// <param name="bundleName">ManifestBundleEntry.BundleName</param>
    /// <returns>成功返回 (bundle, null)，失败返回 (null, error)</returns>
    public (AssetBundle bundle, RuntimeMessage error) LoadBundle(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName ?? ""));
        }

        // 缓存命中：引用计数 +1，直接返回
        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        // 查询 Manifest 获取 BundleEntry
        if (!_manifest.TryGetBundleByName(bundleName, out var bundleEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        // 递归加载依赖 Bundle（同步）
        // 注：visited HashSet 对无依赖的叶子 Bundle 也会分配，代价小于提前查询依赖数量带来的双重 GetDirectDependencies 调用
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            bundleName
        };
        var (depNames, depError) = LoadDependenciesSync(bundleEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return LoadPhysicalBundleSync(bundleName, bundlePath, depNames);
    }

    /// <summary>
    /// 同步卸载 Bundle。引用计数 -1，降至 0 时执行 AssetBundle.Unload(true) 并递归卸载依赖。
    /// </summary>
    /// <param name="bundleName">ManifestBundleEntry.BundleName</param>
    public void UnloadBundle(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName)) return;
        if (!_bundleCache.TryGetValue(bundleName, out var entry)) return;

        entry.RefCount--;
        if (entry.RefCount <= 0)
        {
            // 卸载 AssetBundle 及其所有已加载的资源
            if (entry.Bundle != null)
            {
                entry.Bundle.Unload(true);
            }

            // 移除缓存
            _bundleCache.Remove(bundleName);

            // 递归卸载依赖 Bundle
            UnloadDependencies(entry.DependencyBundleNames);
        }
    }

    #endregion

    #region 异步 API

    /// <summary>
    /// 异步加载 Bundle（含依赖）。
    /// 如果已缓存则直接增加引用计数并返回。
    /// </summary>
    /// <param name="bundleName">ManifestBundleEntry.BundleName</param>
    /// <returns>成功返回 (bundle, null)，失败返回 (null, error)</returns>
    public async Task<(AssetBundle bundle, RuntimeMessage error)> LoadBundleAsync(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName ?? ""));
        }

        // 缓存命中：引用计数 +1，直接返回
        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        // 查询 Manifest 获取 BundleEntry
        if (!_manifest.TryGetBundleByName(bundleName, out var bundleEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        // 递归加载依赖 Bundle（异步）
        // 注：visited HashSet 对无依赖的叶子 Bundle 也会分配，代价小于提前查询依赖数量带来的双重 GetDirectDependencies 调用
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            bundleName
        };
        var (depNames, depError) = await LoadDependenciesAsync(bundleEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return await LoadPhysicalBundleAsync(bundleName, bundlePath, depNames);
    }

    #endregion

    #region 生命周期

    /// <summary>
    /// 卸载所有已缓存的 Bundle。用于资源管理器销毁时的清理。
    /// </summary>
    public void UnloadAllBundles()
    {
        var names = new List<string>(_bundleCache.Keys);
        for (int i = 0; i < names.Count; i++)
        {
            if (_bundleCache.TryGetValue(names[i], out var entry) && entry.Bundle != null)
            {
                entry.Bundle.Unload(true);
            }
        }
        _bundleCache.Clear();
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 解析 Bundle 文件的物理路径。
    /// 策略：热更目录优先 → StreamingAssets 回退。
    /// 跨平台：通过 FileHelper.Exists 统一处理 Android jar: URI 等非文件系统路径。
    /// </summary>
    /// <param name="bundleName">Bundle 文件名</param>
    /// <returns>存在的文件路径，找不到返回 null</returns>
    private string ResolveBundlePath(string bundleName)
    {
        // Primary: 当前热更包的 bundles 目录
        string primaryPath = FYAssetPathUtility.JoinFilePath(RuntimePathManager.CurrentGUIDRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName);
        if (FileHelper.Exists(primaryPath))
            return primaryPath;

        // Fallback: 包内初始 bundles 目录（standalone 模式使用隔离子目录）
        string fallbackPath = FYAssetPathUtility.JoinFilePath(GetStreamingAssetsBundlesDir(), bundleName);
        if (FileHelper.Exists(fallbackPath))
            return fallbackPath;

        return null;
    }

    /// <summary>
    /// 从 StreamingAssets 异步加载 AssetBundle（跨平台）。
    /// 非 Android / Editor → 直接走 LoadFromFileAsync。
    /// Android 运行时 → UnityWebRequestAssetBundle（jar: URI 不是真实文件系统）。
    /// </summary>
    private static async Task<AssetBundle> LoadBundleFromStreamingAssetsAsync(string bundleName)
    {
        string path = FYAssetPathUtility.JoinFilePath(GetStreamingAssetsBundlesDir(), bundleName);

#if UNITY_ANDROID && !UNITY_EDITOR
        using var request = UnityWebRequestAssetBundle.GetAssetBundle(path);
        await request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[ABBundleLoader] StreamingAssets Bundle 加载失败: {path}, 错误: {request.error}");
            return null;
        }
        return DownloadHandlerAssetBundle.GetContent(request);
#else
        if (!FileHelper.Exists(path))
        {
            Debug.LogError($"[ABBundleLoader] StreamingAssets 中未找到 Bundle: {path}");
            return null;
        }
        var fileRequest = AssetBundle.LoadFromFileAsync(path);
        var tcs = new TaskCompletionSource<AssetBundle>();
        if (fileRequest.isDone)
        {
            tcs.SetResult(fileRequest.assetBundle);
        }
        else
        {
            fileRequest.completed += _ => tcs.SetResult(fileRequest.assetBundle);
        }
        return await tcs.Task;
#endif
    }

    /// <summary>
    /// 返回 StreamingAssets 下 bundles 目录的路径。
    /// standalone 模式使用 StreamingAssets/Standalone/bundles/，与在线基线隔离。
    /// </summary>
    private static string GetStreamingAssetsBundlesDir() =>
        FYAssetSettings.Instance.StandaloneBuild
            ? FYAssetPathUtility.JoinFilePath(
                Application.streamingAssetsPath,
                FYAssetSettings.STANDALONE_DIRECTORY_NAME,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME)
            : FYAssetPathUtility.JoinFilePath(
                Application.streamingAssetsPath,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME);

    /// <summary>
    /// 同步递归加载 BundleEntry 的所有依赖 Bundle。
    /// 使用 HashSet 防环和防重复加载。
    /// </summary>
    /// <returns>成功返回 (depNames, null)，失败返回 (null, error)</returns>
    private (string[] depNames, RuntimeMessage error) LoadDependenciesSync(
        ManifestBundleEntry bundleEntry, HashSet<string> visited)
    {
        var directDeps = _manifest.GetDirectDependencies(bundleEntry);
        if (directDeps.Count == 0)
            return (Array.Empty<string>(), null);

        var loadedDepNames = new List<string>(directDeps.Count);

        for (int i = 0; i < directDeps.Count; i++)
        {
            var dep = directDeps[i];
            if (string.IsNullOrEmpty(dep.BundleName)) continue;

            // 环依赖直接判错，避免坏 manifest 在运行时递归爆栈
            if (!visited.Add(dep.BundleName))
            {
                UnloadDependencies(loadedDepNames);
                return (null, RuntimeMessage.DependencyFailed(bundleEntry.BundleName, dep.BundleName));
            }

            try
            {
                // 递归加载依赖的依赖，visited 仅表示当前递归路径。
                var (depBundle, depError) = LoadBundleInternal(dep.BundleName, visited);
                if (depError != null)
                {
                    // 回滚已加载的依赖
                    UnloadDependencies(loadedDepNames);
                    return (null, RuntimeMessage.DependencyFailed(bundleEntry.BundleName, dep.BundleName));
                }

                loadedDepNames.Add(dep.BundleName);
            }
            finally
            {
                visited.Remove(dep.BundleName);
            }
        }

        return (loadedDepNames.ToArray(), null);
    }

    /// <summary>
    /// 异步递归加载 BundleEntry 的所有依赖 Bundle。

    /// 使用 HashSet 防环和防重复加载。
    /// </summary>
    /// <returns>成功返回 (depNames, null)，失败返回 (null, error)</returns>
    private async Task<(string[] depNames, RuntimeMessage error)> LoadDependenciesAsync(
        ManifestBundleEntry bundleEntry, HashSet<string> visited)
    {
        var directDeps = _manifest.GetDirectDependencies(bundleEntry);
        if (directDeps.Count == 0)
            return (Array.Empty<string>(), null);

        var loadedDepNames = new List<string>(directDeps.Count);

        for (int i = 0; i < directDeps.Count; i++)
        {
            var dep = directDeps[i];
            if (string.IsNullOrEmpty(dep.BundleName)) continue;

            // 环依赖直接判错，避免坏 manifest 在运行时递归爆栈
            if (!visited.Add(dep.BundleName))
            {
                UnloadDependencies(loadedDepNames);
                return (null, RuntimeMessage.DependencyFailed(bundleEntry.BundleName, dep.BundleName));
            }

            try
            {
                // 递归加载依赖的依赖，visited 仅表示当前递归路径。
                var (depBundle, depError) = await LoadBundleInternalAsync(dep.BundleName, visited);
                if (depError != null)
                {
                    UnloadDependencies(loadedDepNames);
                    return (null, RuntimeMessage.DependencyFailed(bundleEntry.BundleName, dep.BundleName));
                }

                loadedDepNames.Add(dep.BundleName);
            }
            finally
            {
                visited.Remove(dep.BundleName);
            }
        }

        return (loadedDepNames.ToArray(), null);
    }

    /// <summary>
    /// 批量卸载依赖 Bundle（用于加载失败时的回滚和正常卸载时的递归释放）。
    /// </summary>
    private void UnloadDependencies(IList<string> depNames)
    {
        if (depNames == null) return;
        for (int i = 0; i < depNames.Count; i++)
        {
            UnloadBundle(depNames[i]);
        }
    }

    /// <summary>
    /// 同步完成物理 Bundle 获取。依赖遍历已经完成，因此这里必须先做第二次缓存检查。
    /// </summary>
    private (AssetBundle bundle, RuntimeMessage error) LoadPhysicalBundleSync(
        string bundleName,
        string bundlePath,
        string[] dependencyBundleNames)
    {
        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            UnloadDependencies(dependencyBundleNames);
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        if (_bundleInflightLoads.TryGetValue(bundleName, out var operation))
        {
            UnloadDependencies(dependencyBundleNames);

            if (operation.LocalRequest == null)
            {
                return (null, RuntimeMessage.UnsupportedOperation(
                    nameof(LoadBundle),
                    "无法同步等待正在进行的 StreamingAssets/UWR Bundle 加载"));
            }

            operation.PendingAcquireCount++;

            try
            {
                if (operation.CompletionHandler != null)
                {
                    operation.LocalRequest.completed -= operation.CompletionHandler;
                }

                AssetBundle bundle = operation.LocalRequest.assetBundle;
                RuntimeMessage error = bundle == null
                    ? RuntimeMessage.BundleLoadFailed(bundleName, operation.BundlePath)
                    : null;
                return FinalizeBundleLoad(bundleName, operation, bundle, error);
            }
            catch (Exception)
            {
                return FinalizeBundleLoad(
                    bundleName,
                    operation,
                    null,
                    RuntimeMessage.BundleLoadFailed(bundleName, operation.BundlePath));
            }
        }

        if (bundlePath == null)
        {
            UnloadDependencies(dependencyBundleNames);
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        AssetBundle loadedBundle = AssetBundle.LoadFromFile(bundlePath);
        if (loadedBundle == null)
        {
            UnloadDependencies(dependencyBundleNames);
            return (null, RuntimeMessage.BundleLoadFailed(bundleName, bundlePath));
        }

        _bundleCache[bundleName] = new BundleCacheEntry
        {
            Bundle = loadedBundle,
            RefCount = 1,
            DependencyBundleNames = dependencyBundleNames
        };

        return (loadedBundle, null);
    }

    /// <summary>
    /// 异步完成物理 Bundle 获取。同一 BundleName 的 followers 共享 leader 的本地或 UWR 请求。
    /// </summary>
    private async Task<(AssetBundle bundle, RuntimeMessage error)> LoadPhysicalBundleAsync(
        string bundleName,
        string bundlePath,
        string[] dependencyBundleNames)
    {
        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            UnloadDependencies(dependencyBundleNames);
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        if (_bundleInflightLoads.TryGetValue(bundleName, out var followerOperation))
        {
            UnloadDependencies(dependencyBundleNames);
            followerOperation.PendingAcquireCount++;
            return await followerOperation.Completion.Task;
        }

        var leaderOperation = new BundleLoadOperation
        {
            PendingAcquireCount = 1,
            DependencyBundleNames = dependencyBundleNames,
            BundlePath = bundlePath,
            Completion = new TaskCompletionSource<(AssetBundle bundle, RuntimeMessage error)>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        _bundleInflightLoads[bundleName] = leaderOperation;

        if (bundlePath == null)
        {
            try
            {
                AssetBundle streamedBundle = await LoadBundleFromStreamingAssetsAsync(bundleName);
                RuntimeMessage streamError = streamedBundle == null
                    ? RuntimeMessage.BundleLoadFailed(bundleName, "streamingAssets")
                    : null;
                return FinalizeBundleLoad(bundleName, leaderOperation, streamedBundle, streamError);
            }
            catch (Exception)
            {
                return FinalizeBundleLoad(
                    bundleName,
                    leaderOperation,
                    null,
                    RuntimeMessage.BundleLoadFailed(bundleName, "streamingAssets"));
            }
        }

        try
        {
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
            if (request == null)
            {
                return FinalizeBundleLoad(
                    bundleName,
                    leaderOperation,
                    null,
                    RuntimeMessage.BundleLoadFailed(bundleName, bundlePath));
            }

            leaderOperation.LocalRequest = request;
            leaderOperation.CompletionHandler = _ =>
            {
                try
                {
                    AssetBundle loadedBundle = request.assetBundle;
                    RuntimeMessage error = loadedBundle == null
                        ? RuntimeMessage.BundleLoadFailed(bundleName, bundlePath)
                        : null;
                    FinalizeBundleLoad(bundleName, leaderOperation, loadedBundle, error);
                }
                catch (Exception)
                {
                    FinalizeBundleLoad(
                        bundleName,
                        leaderOperation,
                        null,
                        RuntimeMessage.BundleLoadFailed(bundleName, bundlePath));
                }
            };

            if (request.isDone)
            {
                AssetBundle loadedBundle = request.assetBundle;
                RuntimeMessage error = loadedBundle == null
                    ? RuntimeMessage.BundleLoadFailed(bundleName, bundlePath)
                    : null;
                return FinalizeBundleLoad(bundleName, leaderOperation, loadedBundle, error);
            }

            request.completed += leaderOperation.CompletionHandler;
            return await leaderOperation.Completion.Task;
        }
        catch (Exception)
        {
            return FinalizeBundleLoad(
                bundleName,
                leaderOperation,
                null,
                RuntimeMessage.BundleLoadFailed(bundleName, bundlePath));
        }
    }

    /// <summary>
    /// 幂等完成一个物理加载。先发布缓存或回滚 leader 依赖，再移除 inflight，最后唤醒等待者。
    /// </summary>
    private (AssetBundle bundle, RuntimeMessage error) FinalizeBundleLoad(
        string bundleName,
        BundleLoadOperation operation,
        AssetBundle bundle,
        RuntimeMessage error)
    {
        if (operation.IsFinalized)
        {
            return (operation.Bundle, operation.Error);
        }

        operation.IsFinalized = true;
        operation.Bundle = bundle;
        operation.Error = error;

        if (operation.LocalRequest != null && operation.CompletionHandler != null)
        {
            operation.LocalRequest.completed -= operation.CompletionHandler;
        }

        if (bundle != null && error == null)
        {
            _bundleCache[bundleName] = new BundleCacheEntry
            {
                Bundle = bundle,
                RefCount = operation.PendingAcquireCount,
                DependencyBundleNames = operation.DependencyBundleNames
            };
        }
        else
        {
            UnloadDependencies(operation.DependencyBundleNames);
        }

        if (_bundleInflightLoads.TryGetValue(bundleName, out var currentOperation) &&
            ReferenceEquals(currentOperation, operation))
        {
            _bundleInflightLoads.Remove(bundleName);
        }

        var result = (operation.Bundle, operation.Error);
        operation.Completion.TrySetResult(result);
        return result;
    }

    /// <summary>
    /// 内部同步加载实现，沿用调用链传入的 visited 集合。
    /// </summary>
    private (AssetBundle bundle, RuntimeMessage error) LoadBundleInternal(
        string bundleName, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(bundleName))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName ?? ""));
        }

        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        if (!_manifest.TryGetBundleByName(bundleName, out var bundleEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        var (depNames, depError) = LoadDependenciesSync(bundleEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return LoadPhysicalBundleSync(bundleName, bundlePath, depNames);
    }

    /// <summary>
    /// 内部异步加载实现，沿用调用链传入的 visited 集合。
    /// </summary>
    private async Task<(AssetBundle bundle, RuntimeMessage error)> LoadBundleInternalAsync(
        string bundleName, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(bundleName))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName ?? ""));
        }

        if (_bundleCache.TryGetValue(bundleName, out var cached))
        {
            cached.RefCount++;
            return (cached.Bundle, null);
        }

        if (!_manifest.TryGetBundleByName(bundleName, out var bundleEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        var (depNames, depError) = await LoadDependenciesAsync(bundleEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return await LoadPhysicalBundleAsync(bundleName, bundlePath, depNames);
    }

    #endregion
}

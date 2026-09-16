using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AssetBundle 加载、依赖管理和引用计数。
/// </summary>
/// <remarks>
/// 物理路径相对 RuntimePathManager.ActivePackageRoot；同名 Bundle 的并发加载共享一次请求，引用归零时卸载。
/// </remarks>
public class ABBundleLoader
{
    /// <summary>已加载 Bundle 的实例、依赖和引用计数。</summary>
    private class BundleCacheEntry
    {
        /// <summary>已加载的 AssetBundle 实例</summary>
        public AssetBundle Bundle;

        /// <summary>引用归零时卸载 Bundle，并递归释放依赖。</summary>
        public int RefCount;

        /// <summary>该 Bundle 的直接依赖，卸载时递归释放。</summary>
        public string[] DependencyBundleNames;
    }

    /// <summary>同名 Bundle 的一次物理加载；followers 共享 leader 的结果。</summary>
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

    /// <summary>Bundle 缓存：BundleName → CacheEntry。策略为精确引用计数 + 归零即卸载。</summary>
    private readonly Dictionary<string, BundleCacheEntry> _bundleCache = new();

    /// <summary>正在进行的物理加载：BundleName → leader operation。</summary>
    private readonly Dictionary<string, BundleLoadOperation> _bundleInflightLoads = new();

    /// <summary>ABManifest 引用，用于查询 Bundle 依赖关系</summary>
    private readonly ABManifest _manifest;

    #region Public

    /// <summary>
    /// 创建 ABBundleLoader 实例。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest</param>
    public ABBundleLoader(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <summary>
    /// 同步加载 Bundle（含依赖）。
    /// 如果已缓存则直接增加引用计数并返回。
    /// </summary>
    /// <param name="bundleName">ManifestContentEntry.FileName</param>
    /// <returns>成功返回 (bundle, null)，失败返回 (null, error)</returns>
    public (AssetBundle bundle, RuntimeMessage error) LoadBundle(string bundleName)
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

        if (!_manifest.TryGetContentByFileName(bundleName, out var contentEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        // 注：visited HashSet 对无依赖的叶子 Bundle 也会分配，代价小于提前查询依赖数量带来的双重 GetDirectDependencies 调用
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            bundleName
        };
        var (depNames, depError) = LoadDependenciesSync(contentEntry, visited);
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
    /// <param name="bundleName">ManifestContentEntry.FileName</param>
    public void UnloadBundle(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName)) return;
        if (!_bundleCache.TryGetValue(bundleName, out var entry)) return;

        entry.RefCount--;
        if (entry.RefCount <= 0)
        {
            if (entry.Bundle != null)
            {
                entry.Bundle.Unload(true);
            }

            _bundleCache.Remove(bundleName);

            UnloadDependencies(entry.DependencyBundleNames);
        }
    }

    /// <summary>
    /// 异步加载 Bundle（含依赖）。
    /// 如果已缓存则直接增加引用计数并返回。
    /// </summary>
    /// <param name="bundleName">ManifestContentEntry.FileName</param>
    /// <returns>成功返回 (bundle, null)，失败返回 (null, error)</returns>
    public async Task<(AssetBundle bundle, RuntimeMessage error)> LoadBundleAsync(string bundleName)
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

        if (!_manifest.TryGetContentByFileName(bundleName, out var contentEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        // 注：visited HashSet 对无依赖的叶子 Bundle 也会分配，代价小于提前查询依赖数量带来的双重 GetDirectDependencies 调用
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            bundleName
        };
        var (depNames, depError) = await LoadDependenciesAsync(contentEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return await LoadPhysicalBundleAsync(bundleName, bundlePath, depNames);
    }

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

    /// <summary>
    /// 解析 Bundle 文件的物理路径。
    /// 只在当前激活包根下查找；找不到返回 null，调用方按结构化错误处理，不再回退其他目录。
    /// 跨平台：通过 FileHelper.Exists 判定文件是否存在。
    /// </summary>
    /// <returns>存在的文件路径，找不到返回 null</returns>
    private static string ResolveBundlePath(string bundleName)
    {
        string root = RuntimePathManager.ActivePackageRoot;
        if (string.IsNullOrEmpty(root)) return null;

        string path = FYAssetPathUtility.JoinFilePath(
            root,
            FYAssetSettings.BUNDLES_DIRECTORY_NAME,
            bundleName);
        return FileHelper.Exists(path) ? path : null;
    }

    /// <summary>
    /// 同步递归加载 ContentEntry 的所有依赖内容。
    /// 使用 HashSet 防环和防重复加载。
    /// </summary>
    /// <returns>成功返回 (depNames, null)，失败返回 (null, error)</returns>
    private (string[] depNames, RuntimeMessage error) LoadDependenciesSync(
        ManifestContentEntry contentEntry, HashSet<string> visited)
    {
        var directDeps = _manifest.GetDirectDependencies(contentEntry);
        if (directDeps.Count == 0)
            return (Array.Empty<string>(), null);

        var loadedDepNames = new List<string>(directDeps.Count);

        for (int i = 0; i < directDeps.Count; i++)
        {
            var dep = directDeps[i];
            if (string.IsNullOrEmpty(dep.FileName)) continue;

            // 环依赖直接判错，避免坏 manifest 在运行时递归爆栈
            if (!visited.Add(dep.FileName))
            {
                UnloadDependencies(loadedDepNames);
                return (null, RuntimeMessage.DependencyFailed(contentEntry.FileName, dep.FileName));
            }

            try
            {
                // 递归加载依赖的依赖，visited 仅表示当前递归路径。
                var (depBundle, depError) = LoadBundleInternal(dep.FileName, visited);
                if (depError != null)
                {
                    UnloadDependencies(loadedDepNames);
                    return (null, RuntimeMessage.DependencyFailed(contentEntry.FileName, dep.FileName));
                }

                loadedDepNames.Add(dep.FileName);
            }
            finally
            {
                visited.Remove(dep.FileName);
            }
        }

        return (loadedDepNames.ToArray(), null);
    }

    /// <summary>
    /// 异步递归加载 ContentEntry 的所有依赖内容。
    /// 使用 HashSet 防环和防重复加载。
    /// </summary>
    /// <returns>成功返回 (depNames, null)，失败返回 (null, error)</returns>
    private async Task<(string[] depNames, RuntimeMessage error)> LoadDependenciesAsync(
        ManifestContentEntry contentEntry, HashSet<string> visited)
    {
        var directDeps = _manifest.GetDirectDependencies(contentEntry);
        if (directDeps.Count == 0)
            return (Array.Empty<string>(), null);

        var loadedDepNames = new List<string>(directDeps.Count);

        for (int i = 0; i < directDeps.Count; i++)
        {
            var dep = directDeps[i];
            if (string.IsNullOrEmpty(dep.FileName)) continue;

            // 环依赖直接判错，避免坏 manifest 在运行时递归爆栈
            if (!visited.Add(dep.FileName))
            {
                UnloadDependencies(loadedDepNames);
                return (null, RuntimeMessage.DependencyFailed(contentEntry.FileName, dep.FileName));
            }

            try
            {
                // 递归加载依赖的依赖，visited 仅表示当前递归路径。
                var (depBundle, depError) = await LoadBundleInternalAsync(dep.FileName, visited);
                if (depError != null)
                {
                    UnloadDependencies(loadedDepNames);
                    return (null, RuntimeMessage.DependencyFailed(contentEntry.FileName, dep.FileName));
                }

                loadedDepNames.Add(dep.FileName);
            }
            finally
            {
                visited.Remove(dep.FileName);
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
                // 物理请求尚未建立：同步调用无法安全加入，返回结构化错误而不是空引用。
                // 当前实现里 inflight 记录建立与 LoadFromFileAsync 调用之间没有 await，因此该分支实际不可达。
                return (null, RuntimeMessage.UnsupportedOperation(
                    nameof(LoadBundle),
                    "无法同步等待尚未开始的 Bundle 物理加载"));
            }

            operation.PendingAcquireCount++;

            try
            {
                if (operation.CompletionHandler != null)
                {
                    operation.LocalRequest.completed -= operation.CompletionHandler;
                }

                // 读取请求结果会同步完成尚未结束的本地加载，避免同步调用阻塞在队列上
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
            return FinalizeBundleLoad(
                bundleName,
                leaderOperation,
                null,
                RuntimeMessage.BundleNotFound(bundleName));
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

        if (!_manifest.TryGetContentByFileName(bundleName, out var contentEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        var (depNames, depError) = LoadDependenciesSync(contentEntry, visited);
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

        if (!_manifest.TryGetContentByFileName(bundleName, out var contentEntry))
        {
            return (null, RuntimeMessage.BundleNotFound(bundleName));
        }

        var (depNames, depError) = await LoadDependenciesAsync(contentEntry, visited);
        if (depError != null)
        {
            return (null, depError);
        }

        string bundlePath = ResolveBundlePath(bundleName);
        return await LoadPhysicalBundleAsync(bundleName, bundlePath, depNames);
    }
}

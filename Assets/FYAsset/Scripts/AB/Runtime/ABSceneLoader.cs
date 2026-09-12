using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene 加载与卸载控制器 —— 按 Unity 2022.3 的场景 Bundle 约束实现。
/// </summary>
/// <remarks>
/// 官方约束与对应实现：
/// 1. 场景 Bundle 只能通过 <see cref="AssetBundle.GetAllScenePaths"/> 取得完整场景路径，
///    再交给 <see cref="SceneManager.LoadSceneAsync(string, LoadSceneMode)"/>；本类用条目 SourcePath 的文件名比对校验。
/// 2. <see cref="SceneManager.UnloadSceneAsync(Scene)"/> 不释放 AssetBundle 引用，因此内容引用由本类持有，
///    只在场景确认卸载后调用 ABBundleLoader.UnloadBundle。
/// 3. Additive 场景由调用方 UnloadAsync 卸载；Single 模式在新场景激活并确认旧场景卸载后结算旧 SceneHandle。
/// 4. allowSceneActivation=false 不允许无限等待：预载窗口有帧数上限，窗口结束即激活，
///    避免把 AsyncOperation 长期停在队列里，也避免返回调用方无法激活的悬空句柄。
/// 5. Resources.UnloadUnusedAssets 只在场景切换这类安全入口显式调用，不放在每次 Handle 释放里。
/// </remarks>
internal sealed class ABSceneLoader : IABSceneUnloadSink
{

    /// <summary>
    /// 已加载场景的记账。一个场景路径对应一条记录，持有内容引用与创建它的 token。
    /// </summary>
    private sealed class SceneRecord
    {
        /// <summary>AssetBundle.GetAllScenePaths() 给出的完整场景路径（字典键）</summary>
        public string ScenePath;

        /// <summary>承载该场景的内容文件名（ManifestContentEntry.FileName）</summary>
        public string ContentFileName;

        /// <summary>场景资源 EntryId：同一条目下所有 token 都必须随强制卸载一起结算</summary>
        public string EntryId;

        /// <summary>已加载的场景</summary>
        public Scene Scene;
    }

    /// <summary>activateOnLoad=false 时允许等待场景预载的最大帧数。</summary>
    private const int PreloadWaitBudgetFrames = 120;

    /// <summary>Single 模式确认旧场景卸载的最大帧数。</summary>
    private const int UnloadConfirmBudgetFrames = 60;

    private readonly ABManifest _manifest;
    private readonly ABBundleLoader _bundleLoader;
    private readonly bool _directScenePathMode;

    /// <summary>场景路径 → 记账。路径比较大小写不敏感，与 Unity 的路径写法保持一致。</summary>
    private readonly Dictionary<string, SceneRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建场景加载器。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest</param>
    /// <param name="bundleLoader">内容 Bundle 加载器；Editor PlayMode 传 null，表示直接按工程路径加载场景</param>
    public ABSceneLoader(ABManifest manifest, ABBundleLoader bundleLoader)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _bundleLoader = bundleLoader;
        _directScenePathMode = bundleLoader == null;
    }

    /// <summary>
    /// 加载场景并分配 SceneHandle。
    /// </summary>
    /// <param name="entry">已解析的公共 Scene 条目</param>
    /// <param name="mode">Single 会替换当前场景；Additive 由调用方另行卸载</param>
    /// <param name="activateOnLoad">true 直接激活；false 先做有界预载再激活（见类型注释第 4 条）</param>
    public async Task<SceneHandle> LoadSceneAsync(
        RuntimeAssetEntry entry,
        LoadSceneMode mode,
        bool activateOnLoad)
    {
        if (!_manifest.TryGetAssetByEntryId(entry.EntryId, out ManifestAssetEntry assetEntry))
            return Failed(entry, RuntimeMessage.NotFound(string.Concat("EntryId=", entry.EntryId)));

        if (assetEntry.ContentType != AssetContentType.Scene)
        {
            return Failed(entry, RuntimeMessage.InvalidPayloadKind(
                assetEntry.EntryId,
                AssetContentType.Scene.ToString(),
                assetEntry.ContentType.ToString()));
        }

        string contentFileName = null;
        string scenePath;
        if (_directScenePathMode)
        {
            // Editor PlayMode 经 AssetDatabase 加载：场景按工程路径直接加载，没有内容 Bundle 引用可持有
            scenePath = assetEntry.SourcePath;
        }
        else
        {
            ManifestContentEntry contentEntry = _manifest.GetContentForAsset(assetEntry);
            if (contentEntry == null)
            {
                return Failed(entry, RuntimeMessage.BundleNotFound(string.Concat(
                    "(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
            }

            if (contentEntry.ContentType != AssetContentType.Scene)
            {
                return Failed(entry, RuntimeMessage.InvalidPayloadKind(
                    contentEntry.FileName,
                    AssetContentType.Scene.ToString(),
                    contentEntry.ContentType.ToString()));
            }

            contentFileName = contentEntry.FileName;
            var (bundle, bundleError) = await _bundleLoader.LoadBundleAsync(contentFileName);
            if (bundleError != null)
                return Failed(entry, bundleError);

            scenePath = ResolveScenePath(bundle, assetEntry, contentFileName, out RuntimeMessage pathError);
            if (scenePath == null)
            {
                // 场景内容没有被任何 token 引用，校验失败必须立即回滚这次内容获取。
                _bundleLoader.UnloadBundle(contentFileName);
                return Failed(entry, pathError);
            }
        }

        // Single 模式会替换当前场景：先记下要被替换的场景，等新场景激活后再结算它们。
        SceneRecord[] replaced = mode == LoadSceneMode.Single
            ? CollectLoadedRecords()
            : Array.Empty<SceneRecord>();

        AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath, mode);
        if (operation == null)
        {
            ReleaseContentReference(contentFileName);
            return Failed(entry, RuntimeMessage.SceneLoadFailed(
                assetEntry.EntryId,
                string.Concat("SceneManager.LoadSceneAsync 返回 null, ScenePath=", scenePath)));
        }

        if (!activateOnLoad)
            await PreloadAsync(operation);

        operation.allowSceneActivation = true;
        RuntimeMessage activationError = await WaitForOperationAsync(operation);
        if (activationError != null)
        {
            ReleaseContentReference(contentFileName);
            return Failed(entry, RuntimeMessage.SceneLoadFailed(assetEntry.EntryId, activationError.Message));
        }

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        // Scene 槽位不携带 Registry 释放回调：内容引用由本控制器在场景卸载后释放。
        // 每个场景一个 token：调用方释放它时 Registry 才把 EntryId 计数降到零；
        // Single 模式外的结算由 SceneHandle 自己负责，因此记录本身不再保存 token 快照。
        var (handleId, generation) = HandleRegistry.Alloc(
            assetEntry.EntryId,
            HandleKind.Scene,
            contentFileName,
            null,
            null);

        _records[scenePath] = new SceneRecord
        {
            ScenePath = scenePath,
            ContentFileName = contentFileName,
            EntryId = assetEntry.EntryId,
            Scene = scene
        };

        if (replaced.Length > 0)
            await SettleReplacedScenesAsync(replaced);

        return new SceneHandle(handleId, generation, entry.Address, scenePath, scene, this);
    }

    /// <summary>
    /// 卸载场景并释放内容引用。返回 null 表示成功。
    /// </summary>
    public async Task<RuntimeMessage> UnloadSceneAsync(Scene scene, string scenePath)
    {
        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                return RuntimeMessage.SceneLoadFailed(
                    scenePath,
                    "SceneManager.UnloadSceneAsync 返回 null");
            }

            RuntimeMessage waitError = await WaitForOperationAsync(operation);
            if (waitError != null)
                return RuntimeMessage.SceneLoadFailed(scenePath, waitError.Message);
        }

        ReleaseContent(scenePath);
        return null;
    }

    /// <summary>
    /// 丢弃全部场景记账并清空 Bundle 缓存引用（资源管理器 Shutdown 时调用）。
    /// </summary>
    /// <remarks>
    /// 场景本身不在这里卸载：调用方必须在 Handle 归零前完成 UnloadAsync。
    /// 本方法只负责让控制器不再持有内容引用，避免重新初始化后残留旧包的文件句柄。
    /// </remarks>
    public void Clear()
    {
        if (_records.Count > 0)
        {
            Debug.LogWarning(string.Concat(
                "[ABSceneLoader] Shutdown 时仍有 ", _records.Count.ToString(),
                " 个场景未通过 UnloadAsync 卸载，内容引用将被强制清空。"));
        }

        _records.Clear();
    }

    /// <summary>
    /// 按条目 SourcePath 的文件名校验并返回 Bundle 内的完整场景路径。
    /// 校验失败时输出结构化错误，调用方负责回滚内容引用。
    /// </summary>
    private static string ResolveScenePath(
        AssetBundle bundle,
        ManifestAssetEntry assetEntry,
        string contentFileName,
        out RuntimeMessage error)
    {
        error = null;
        string[] scenePaths = bundle.GetAllScenePaths();
        if (scenePaths == null || scenePaths.Length == 0)
        {
            error = RuntimeMessage.SceneLoadFailed(
                assetEntry.EntryId,
                string.Concat("内容 ", contentFileName, " 内没有任何 Scene"));
            return null;
        }

        string expectedFileName = Path.GetFileNameWithoutExtension(assetEntry.SourcePath);
        string matched = null;
        for (int i = 0; i < scenePaths.Length; i++)
        {
            string candidate = scenePaths[i];
            if (string.IsNullOrEmpty(candidate)) continue;

            if (string.Equals(candidate, assetEntry.SourcePath, StringComparison.OrdinalIgnoreCase))
                return candidate;

            if (matched == null &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                matched = candidate;
            }
        }

        if (matched != null)
            return matched;

        error = RuntimeMessage.SceneLoadFailed(
            assetEntry.EntryId,
            string.Concat(
                "Bundle 内 ScenePath 与条目 SourcePath 不匹配。SourcePath='", assetEntry.SourcePath,
                "', Content=", contentFileName,
                ", ScenePaths=[", JoinPaths(scenePaths), "]"));
        return null;
    }

    /// <summary>
    /// activateOnLoad=false 的有界预载：等待场景数据加载到「已加载未激活」（progress 达到 0.9）。
    /// 等待帧数用尽即返回，随后由加载流程统一激活，保证不会无限等待。
    /// </summary>
    private static async Task PreloadAsync(AsyncOperation operation)
    {
        operation.allowSceneActivation = false;
        for (int frame = 0; frame < PreloadWaitBudgetFrames; frame++)
        {
            if (operation.isDone || operation.progress >= 0.9f)
                return;

            await Task.Yield();
        }
    }

    /// <summary>
    /// 等待一个 Unity AsyncOperation 完成。返回 null 表示成功。
    /// </summary>
    private static Task<RuntimeMessage> WaitForOperationAsync(AsyncOperation operation)
    {
        if (operation.isDone) return Task.FromResult<RuntimeMessage>(null);

        var completion = new TaskCompletionSource<RuntimeMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        operation.completed += _ => completion.TrySetResult(null);
        return completion.Task;
    }

    /// <summary>
    /// Single 模式结算：确认旧场景已经卸载后再释放旧 SceneHandle 与旧内容引用。
    /// </summary>
    private async Task SettleReplacedScenesAsync(SceneRecord[] replaced)
    {
        for (int i = 0; i < replaced.Length; i++)
        {
            SceneRecord record = replaced[i];

            if (record.Scene.IsValid() && record.Scene.isLoaded)
            {
                await WaitForSceneUnloadAsync(record.Scene);
                if (record.Scene.isLoaded)
                {
                    Debug.LogWarning(string.Concat(
                        "[ABSceneLoader] Single 模式替换后旧场景仍报告已加载，按已切换结算: ",
                        record.ScenePath));
                }
            }

            // 同一路径重新加载时新记录已经占用该键，只移除确实属于本次替换的旧记录
            if (_records.TryGetValue(record.ScenePath, out SceneRecord current) &&
                ReferenceEquals(current, record))
            {
                _records.Remove(record.ScenePath);
            }

            // 强制卸载已经确认：结算该场景条目下全部存活 token（含调用方 Retain 出的副本），
            // 旧句柄此后 IsValid=false，重复 Release 为 no-op。刻意不逐个 Release：
            // 逐个结算不会把同一条目的其余所有者一起作废。
            HandleRegistry.ReleaseAllForEntry(record.EntryId);
            // 场景确认卸载之后才释放内容引用：AssetBundle.Unload(true) 不能早于场景卸载。
            ReleaseContentReference(record.ContentFileName);
        }

        // 场景切换是显式全局清理的安全入口；每次 Handle 释放都调用会带来无谓的全量扫描。
        Resources.UnloadUnusedAssets();
    }

    /// <summary>
    /// 有界等待旧场景卸载完成，避免因为异常状态无限挂起。
    /// </summary>
    private static async Task WaitForSceneUnloadAsync(Scene scene)
    {
        for (int frame = 0; frame < UnloadConfirmBudgetFrames; frame++)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            await Task.Yield();
        }
    }

    /// <summary>释放指定场景路径的内容引用并移除记账。</summary>
    private void ReleaseContent(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return;
        if (!_records.TryGetValue(scenePath, out SceneRecord record)) return;

        _records.Remove(record.ScenePath);
        ReleaseContentReference(record.ContentFileName);
    }

    /// <summary>释放内容引用。Editor PlayMode 直接按工程路径加载，没有内容引用可释放。</summary>
    private void ReleaseContentReference(string contentFileName)
    {
        if (_bundleLoader == null || string.IsNullOrEmpty(contentFileName)) return;
        _bundleLoader.UnloadBundle(contentFileName);
    }

    private SceneRecord[] CollectLoadedRecords()
    {
        var records = new SceneRecord[_records.Count];
        _records.Values.CopyTo(records, 0);
        return records;
    }

    private static SceneHandle Failed(RuntimeAssetEntry entry, RuntimeMessage error)
    {
        return new SceneHandle(error, entry?.Address, null);
    }

    private static string JoinPaths(string[] paths)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < paths.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(paths[i] ?? "");
        }

        return builder.ToString();
    }
}

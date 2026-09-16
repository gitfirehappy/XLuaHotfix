using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>已解析 Scene 条目的加载与 SceneManager 生命周期管理。</summary>
internal sealed class ABSceneLoader : IABSceneUnloadSink
{
    private sealed class SceneRecord
    {
        public string Address;
        public string ScenePath;
        public string ContentFileName;
        public Scene Scene;
    }

    private readonly ABManifest _manifest;
    private readonly ABBundleLoader _bundleLoader;
    private readonly bool _directScenePathMode;
    private readonly Dictionary<string, SceneRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    public ABSceneLoader(ABManifest manifest, ABBundleLoader bundleLoader)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _bundleLoader = bundleLoader;
        _directScenePathMode = bundleLoader == null;
    }

    public bool HasLoadedScenes => _records.Count > 0;

    public async Task<SceneHandle> LoadSceneAsync(
        ManifestAssetEntry entry,
        ManifestContentEntry content,
        LoadSceneMode mode)
    {
        if (entry == null || content == null)
            return Failed(entry, RuntimeMessage.NotFound("Scene ManifestEntry"));
        if (content.ContentType != AssetContentType.Scene)
            return Failed(entry, RuntimeMessage.InvalidPayloadKind(
                entry.Address, AssetContentType.Scene.ToString(), content.ContentType.ToString()));

        string contentFileName = null;
        string scenePath;
        if (_directScenePathMode)
        {
            scenePath = entry.AssetPath;
        }
        else
        {
            contentFileName = content.FileName;
            var (bundle, bundleError) = await _bundleLoader.LoadBundleAsync(contentFileName);
            if (bundleError != null)
                return Failed(entry, bundleError);
            scenePath = ResolveScenePath(bundle, entry, contentFileName, out RuntimeMessage pathError);
            if (scenePath == null)
            {
                _bundleLoader.UnloadBundle(contentFileName);
                return Failed(entry, pathError);
            }
        }

        if (string.IsNullOrEmpty(scenePath))
        {
            ReleaseContentReference(contentFileName);
            return Failed(entry, RuntimeMessage.SceneLoadFailed(entry.Address, "ScenePath 为空"));
        }

        SceneRecord[] replaced = mode == LoadSceneMode.Single ? CollectLoadedRecords() : Array.Empty<SceneRecord>();
        AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath, mode);
        if (operation == null)
        {
            ReleaseContentReference(contentFileName);
            return Failed(entry, RuntimeMessage.SceneLoadFailed(entry.Address, "SceneManager.LoadSceneAsync 返回 null"));
        }

        RuntimeMessage waitError = await WaitForOperationAsync(operation);
        if (waitError != null)
        {
            ReleaseContentReference(contentFileName);
            return Failed(entry, waitError);
        }

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        var (handleId, generation) = HandleRegistry.Alloc(entry.Address, HandleKind.Scene, null, null);
        _records[scenePath] = new SceneRecord
        {
            Address = entry.Address,
            ScenePath = scenePath,
            ContentFileName = contentFileName,
            Scene = scene
        };

        for (int i = 0; i < replaced.Length; i++)
            await SettleReplacedSceneAsync(replaced[i]);

        return new SceneHandle(handleId, generation, entry.Address, scenePath, scene, this);
    }

    public async Task<RuntimeMessage> UnloadSceneAsync(Scene scene, string scenePath)
    {
        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
                return RuntimeMessage.SceneLoadFailed(scenePath, "SceneManager.UnloadSceneAsync 返回 null");
            RuntimeMessage waitError = await WaitForOperationAsync(operation);
            if (waitError != null)
                return waitError;
        }

        if (_records.TryGetValue(scenePath, out SceneRecord record))
        {
            _records.Remove(scenePath);
            ReleaseContentReference(record.ContentFileName);
        }
        return null;
    }

    private static string ResolveScenePath(AssetBundle bundle, ManifestAssetEntry entry, string contentFileName, out RuntimeMessage error)
    {
        error = null;
        string[] scenePaths = bundle?.GetAllScenePaths();
        if (scenePaths == null || scenePaths.Length == 0)
        {
            error = RuntimeMessage.SceneLoadFailed(entry.Address, $"内容 {contentFileName} 内没有 Scene");
            return null;
        }

        string expectedName = Path.GetFileNameWithoutExtension(entry.AssetPath);
        for (int i = 0; i < scenePaths.Length; i++)
        {
            string candidate = scenePaths[i];
            if (string.Equals(candidate, entry.AssetPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(candidate), expectedName, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        error = RuntimeMessage.SceneLoadFailed(entry.Address,
            $"Bundle 内 ScenePath 与 AssetPath 不匹配: {entry.AssetPath}");
        return null;
    }

    private async Task SettleReplacedSceneAsync(SceneRecord record)
    {
        if (record.Scene.IsValid() && record.Scene.isLoaded)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(record.Scene);
            if (operation != null)
                await WaitForOperationAsync(operation);
        }

        if (_records.TryGetValue(record.ScenePath, out SceneRecord current)
            && ReferenceEquals(current, record))
            _records.Remove(record.ScenePath);

        HandleRegistry.ReleaseAllForEntry(record.Address);
        ReleaseContentReference(record.ContentFileName);
    }

    private static Task<RuntimeMessage> WaitForOperationAsync(AsyncOperation operation)
    {
        if (operation == null)
            return Task.FromResult<RuntimeMessage>(RuntimeMessage.SceneLoadFailed("Scene", "异步操作为空"));
        if (operation.isDone)
            return Task.FromResult<RuntimeMessage>(null);
        var completion = new TaskCompletionSource<RuntimeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.completed += _ => completion.TrySetResult(null);
        return completion.Task;
    }

    private void ReleaseContentReference(string fileName)
    {
        if (_bundleLoader != null && !string.IsNullOrEmpty(fileName))
            _bundleLoader.UnloadBundle(fileName);
    }

    private SceneRecord[] CollectLoadedRecords()
    {
        var records = new SceneRecord[_records.Count];
        _records.Values.CopyTo(records, 0);
        return records;
    }

    private static SceneHandle Failed(ManifestAssetEntry entry, RuntimeMessage error)
        => new SceneHandle(error, entry?.Address, null);
}

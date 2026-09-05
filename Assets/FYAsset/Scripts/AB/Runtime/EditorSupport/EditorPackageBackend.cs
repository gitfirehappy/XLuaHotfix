#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor PlayMode 下用 AssetDatabase 替代 AssetBundle 加载。
/// API 形状对齐 ABPackageBackend，供 ABPackageManager 复用 Resolve / Handle 路径。
/// </summary>
internal sealed class EditorPackageBackend : IABLoadBackend
{
    private readonly ABManifest _manifest;
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    public EditorPackageBackend(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key, string entryId)
        where T : UnityEngine.Object
    {
        await Task.Yield();
        return LoadAssetSync<T>(key, entryId);
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string key, string entryId)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadAssetSync: key 为空"));

        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound($"key={key}, entryId={entryId ?? ""}"));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached) && cached is T typedCached)
            return (typedCached, null);

        if (string.IsNullOrEmpty(assetEntry.SourcePath))
            return (null, RuntimeMessage.LoadFailed(entryId, "Editor 条目缺少 SourcePath"));

        var asset = AssetDatabase.LoadAssetAtPath<T>(assetEntry.SourcePath);
        if (asset == null)
            return (null, RuntimeMessage.LoadFailed(entryId, $"AssetDatabase 未找到: {assetEntry.SourcePath}"));

        _assetCache[assetEntry.EntryId] = asset;
        return (asset, null);
    }

    public async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string key, string entryId)
    {
        await Task.Yield();
        return LoadRawBytesSync(key, entryId);
    }

    public (byte[] data, RuntimeMessage error) LoadRawBytesSync(string key, string entryId)
    {
        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound($"key={key}, entryId={entryId ?? ""}"));
        if (string.IsNullOrEmpty(assetEntry.SourcePath) || !File.Exists(assetEntry.SourcePath))
            return (null, RuntimeMessage.LoadFailed(entryId, $"Raw 文件不存在: {assetEntry.SourcePath}"));

        try
        {
            return (File.ReadAllBytes(assetEntry.SourcePath), null);
        }
        catch (Exception ex)
        {
            return (null, RuntimeMessage.LoadFailed(entryId, ex.Message));
        }
    }

    public async Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        await Task.Yield();
        return LoadAssetTupleSync<T>(key, entryId);
    }

    public (T asset, string bundleName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        var (asset, error) = LoadAssetSync<T>(key, entryId);
        return (asset, "editor", error);
    }

    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        _assetCache.Remove(entryId);
    }

    private ManifestAssetEntry ResolveAssetEntry(string address, string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return null;
        if (!_manifest.TryGetAssetByEntryId(entryId, out var assetEntry)) return null;
        return string.Equals(assetEntry.Address, address, StringComparison.Ordinal) ? assetEntry : null;
    }
}
#endif

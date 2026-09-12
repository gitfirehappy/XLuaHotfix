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
    private const string EditorContentName = "editor";

    private readonly ABManifest _manifest;
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    public EditorPackageBackend(ABManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public async Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string address, string entryId) where T : UnityEngine.Object
    {
        await Task.Yield();
        return LoadAssetTupleSync<T>(address, entryId);
    }

    public (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string address, string entryId) where T : UnityEngine.Object
    {
        ManifestAssetEntry assetEntry = ResolveAssetEntry(address, entryId, out RuntimeMessage resolveError);
        if (assetEntry == null) return (null, null, resolveError);

        if (assetEntry.ContentType != AssetContentType.SerializedObject)
        {
            return (null, null, RuntimeMessage.InvalidPayloadKind(
                assetEntry.EntryId,
                AssetContentType.SerializedObject.ToString(),
                assetEntry.ContentType.ToString()));
        }

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached) && cached is T typedCached)
            return (typedCached, EditorContentName, null);

        if (string.IsNullOrEmpty(assetEntry.SourcePath))
        {
            return (null, EditorContentName,
                RuntimeMessage.LoadFailed(entryId, "Editor 条目缺少 SourcePath"));
        }

        var asset = AssetDatabase.LoadAssetAtPath<T>(assetEntry.SourcePath);
        if (asset == null)
        {
            return (null, EditorContentName,
                RuntimeMessage.LoadFailed(entryId, $"AssetDatabase 未找到: {assetEntry.SourcePath}"));
        }

        _assetCache[assetEntry.EntryId] = asset;
        return (asset, EditorContentName, null);
    }

    public async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string address, string entryId)
    {
        await Task.Yield();

        ManifestAssetEntry assetEntry = ResolveAssetEntry(address, entryId, out RuntimeMessage resolveError);
        if (assetEntry == null) return (null, resolveError);

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

    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        _assetCache.Remove(entryId);
    }

    public void UnloadAllContent()
    {
        _assetCache.Clear();
    }

    /// <summary>按 EntryId 精确定位资源条目；Address 只参与错误文本。</summary>
    private ManifestAssetEntry ResolveAssetEntry(
        string address,
        string entryId,
        out RuntimeMessage error)
    {
        error = null;
        if (_manifest.TryGetAssetByEntryId(entryId, out ManifestAssetEntry assetEntry))
            return assetEntry;

        error = RuntimeMessage.NotFound(string.Concat("address=", address ?? "", ", entryId=", entryId ?? ""));
        return null;
    }
}
#endif

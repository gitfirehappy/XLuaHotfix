#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Editor PlayMode 的直读加载器；只处理已解析 Manifest 条目。</summary>
internal sealed class EditorAssetLoader : IABAssetLoader
{
    private const string EditorContentName = "editor";
    private readonly Dictionary<string, UnityEngine.Object> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object
    {
        await Task.Yield();
        return LoadAssetTupleSync<T>(entry, content);
    }

    public (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object
    {
        if (entry == null || content == null)
            return (null, EditorContentName, RuntimeMessage.NotFound("ManifestAssetEntry"));
        if (content.ContentType != AssetContentType.SerializedObject)
            return (null, EditorContentName, RuntimeMessage.InvalidPayloadKind(
                entry.Address, AssetContentType.SerializedObject.ToString(), content.ContentType.ToString()));
        if (_assetCache.TryGetValue(entry.Address, out UnityEngine.Object cached) && cached is T typed)
            return (typed, EditorContentName, null);
        if (string.IsNullOrEmpty(entry.AssetPath))
            return (null, EditorContentName, RuntimeMessage.LoadFailed(entry.Address, "Editor 条目缺少 AssetPath"));

        T asset = AssetDatabase.LoadAssetAtPath<T>(entry.AssetPath);
        if (asset == null)
            return (null, EditorContentName, RuntimeMessage.LoadFailed(entry.Address,
                $"AssetDatabase 未找到: {entry.AssetPath}"));
        _assetCache[entry.Address] = asset;
        return (asset, EditorContentName, null);
    }

    public void UnloadByAddress(string address)
    {
        if (!string.IsNullOrEmpty(address))
            _assetCache.Remove(address);
    }

    public void UnloadAllContent() => _assetCache.Clear();
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 根据管线已经解析完成的容器 Address 生成 Lua 脚本索引。
/// 不读取 Addressables 或 AssetCollectionSetting，地址所有权由调用方负责。
/// </summary>
public static class LuaScriptsIndexBuilder
{
    public static int Rebuild(IReadOnlyDictionary<string, string> containerAddresses)
    {
        if (containerAddresses == null || containerAddresses.Count == 0)
            throw new LuaScriptsIndexBuildException("没有可写入 LuaScriptsIndex 的 LuaScriptContainer。");

        var paths = new List<string>(containerAddresses.Keys);
        paths.Sort(StringComparer.Ordinal.Compare);

        var usedAddresses = new HashSet<string>(StringComparer.Ordinal);
        var moduleOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = new List<LuaScriptsIndex.ContainerEntry>(paths.Count);

        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            string address = containerAddresses[path];
            if (string.IsNullOrWhiteSpace(address))
                throw new LuaScriptsIndexBuildException($"LuaScriptContainer Address 为空: {path}");
            if (!usedAddresses.Add(address))
                throw new LuaScriptsIndexBuildException($"LuaScriptContainer Address 重复: {address}");

            var container = AssetDatabase.LoadAssetAtPath<LuaScriptContainer>(path);
            if (container == null)
                throw new LuaScriptsIndexBuildException($"无法加载 LuaScriptContainer: {path}");

            var scriptNames = new List<string>();
            if (container.luaAssets != null)
            {
                for (int a = 0; a < container.luaAssets.Count; a++)
                {
                    var asset = container.luaAssets[a];
                    if (asset == null)
                        continue;

                    string moduleName = XLuaLoader.NormalizeModuleKey(asset.name);
                    if (string.IsNullOrEmpty(moduleName))
                        throw new LuaScriptsIndexBuildException($"Lua 模块名为空: Container={path}, Asset={asset.name}");
                    if (moduleOwners.TryGetValue(moduleName, out string existingAddress))
                    {
                        throw new LuaScriptsIndexBuildException(
                            $"Lua 模块名重复: {moduleName}, Containers={existingAddress}/{address}");
                    }

                    moduleOwners[moduleName] = address;
                    scriptNames.Add(moduleName);
                }
            }

            scriptNames.Sort(StringComparer.Ordinal.Compare);
            data.Add(new LuaScriptsIndex.ContainerEntry
            {
                containerAddress = address,
                scriptNames = scriptNames
            });
        }

        if (moduleOwners.Count == 0)
            throw new LuaScriptsIndexBuildException("LuaScriptsIndex 不包含任何 Lua 模块。");

        var index = AssetDatabase.LoadAssetAtPath<LuaScriptsIndex>(LuaScriptsIndex.EditorAssetPath);
        if (index == null)
        {
            index = UnityEngine.ScriptableObject.CreateInstance<LuaScriptsIndex>();
            AssetDatabase.CreateAsset(index, LuaScriptsIndex.EditorAssetPath);
        }

        index.data = data;
        EditorUtility.SetDirty(index);
        AssetDatabase.SaveAssetIfDirty(index);
        return data.Count;
    }

    public static void ValidatePublishedAssets(IEnumerable<LuaScriptsIndexPublishedAsset> publishedAssets)
    {
        var typesByAddress = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int indexCount = 0;
        foreach (LuaScriptsIndexPublishedAsset asset in publishedAssets ?? Array.Empty<LuaScriptsIndexPublishedAsset>())
        {
            if (!typesByAddress.TryGetValue(asset.Address ?? string.Empty, out var types))
            {
                types = new HashSet<string>(StringComparer.Ordinal);
                typesByAddress[asset.Address ?? string.Empty] = types;
            }
            types.Add(asset.PrimaryType ?? string.Empty);

            if (!string.Equals(asset.Address, LuaScriptsIndex.AssetAddress, StringComparison.Ordinal))
                continue;

            indexCount++;
            if (!string.Equals(asset.PrimaryType, nameof(LuaScriptsIndex), StringComparison.Ordinal))
                throw new LuaScriptsIndexBuildException(
                    $"启动资源类型错误: Address={asset.Address}, Type={asset.PrimaryType}");
            if (!string.IsNullOrEmpty(asset.AssetPath) &&
                !string.Equals(asset.AssetPath, LuaScriptsIndex.EditorAssetPath, StringComparison.Ordinal))
            {
                throw new LuaScriptsIndexBuildException(
                    $"启动资源路径错误: Address={asset.Address}, Path={asset.AssetPath}");
            }
        }

        if (indexCount != 1)
            throw new LuaScriptsIndexBuildException(
                $"Manifest 中启动资源 Address 必须唯一: {LuaScriptsIndex.AssetAddress}, Count={indexCount}");

        var index = AssetDatabase.LoadAssetAtPath<LuaScriptsIndex>(LuaScriptsIndex.EditorAssetPath);
        if (index == null || index.data == null || index.data.Count == 0)
            throw new LuaScriptsIndexBuildException($"LuaScriptsIndex 无效: {LuaScriptsIndex.EditorAssetPath}");

        for (int i = 0; i < index.data.Count; i++)
        {
            var entry = index.data[i];
            if (entry == null || string.IsNullOrEmpty(entry.containerAddress))
                throw new LuaScriptsIndexBuildException($"LuaScriptsIndex ContainerEntry[{i}] Address 为空。");
            if (!typesByAddress.TryGetValue(entry.containerAddress, out var types) ||
                !types.Contains(nameof(LuaScriptContainer)))
            {
                throw new LuaScriptsIndexBuildException(
                    $"Manifest 缺少 LuaScriptContainer: Address={entry.containerAddress}");
            }
        }
    }
}

public readonly struct LuaScriptsIndexPublishedAsset
{
    public readonly string Address;
    public readonly string PrimaryType;
    public readonly string AssetPath;

    public LuaScriptsIndexPublishedAsset(string address, string primaryType, string assetPath = null)
    {
        Address = address;
        PrimaryType = primaryType;
        AssetPath = assetPath;
    }
}

public sealed class LuaScriptsIndexBuildException : Exception
{
    public LuaScriptsIndexBuildException(string message) : base(message)
    {
    }
}
#endif

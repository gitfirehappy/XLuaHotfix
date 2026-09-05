using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

public static class XLuaLoader
{
    public enum Mode
    {
        EditorOnly = 0, // 只读磁盘（Editor）
        PackageOnly = 1, // 只读当前绑定的资源包后端
        Hybrid = 2 // 先 Editor，再资源包
    }

    public sealed class Options
    {
        public Mode mode = Mode.Hybrid;

        public List<string> editorRoots = new(); // 编辑器根目录,默认Assets/ + 根目录
        public List<string> extensions = new() { ".lua", ".lua.txt", ".bytes" }; // 扩展名
    }

    /// <summary>
    /// 内容缓存: Lua模块名 -> 文件二进制数据
    /// </summary> 
    private static readonly Dictionary<string, byte[]> _contentCache = new();

    /// <summary>
    /// 索引缓存引用
    /// </summary>
    private static LuaScriptsIndex _luaIndexAsset;
    
    private static bool _isIndexBuilt = false;

    #region 对外API

    /// <summary>
    /// 初始化并注册到指定 LuaEnv 的 AddLoader
    /// </summary>
    public static async Task SetupAndRegister(LuaEnv env, Options options = null)
    {
        if (env == null) throw new ArgumentNullException(nameof(env));
        var opt = options ?? new Options();

        // 构建索引缓存
        if (!_isIndexBuilt && opt.mode != Mode.EditorOnly)
        {
            await LoadPreBuildIndex();
            _isIndexBuilt = true;
        }

        env.AddLoader((ref string filepath) =>
        {
            string key = NormalizeModuleKey(filepath);
            byte[] bytes = null;

            // 尝试内容缓存
            if (_contentCache.TryGetValue(key, out bytes))
            {
                Debug.Log($"[LuaLoader] 缓存命中: {key}");
                return bytes;
            }

            // 尝试编辑器路径
            if (opt.mode != Mode.PackageOnly)
            {
                bytes = TryReadFromEditor(opt, key);
                if (bytes != null) return bytes;
            }

            // 尝试通过索引缓存查询加载（懒加载 + 写入内容缓存）
            if (_luaIndexAsset != null && _luaIndexAsset.ScriptToContainer.TryGetValue(key, out string containerAddress))
            {
                bytes = LoadFromPackageSync(containerAddress, key);

                if (bytes != null)
                {
                    _contentCache[key] = bytes;
                    return bytes;
                }
            }

            Debug.LogWarning($"[LuaLoader] 没有找到Lua文件: {key}");
            return null;
        });

        Debug.Log($"[LuaLoader] 注册AddLoader成功 Mode={opt.mode}");
    }

    /// <summary>
    /// 【按容器释放】
    /// 释放指定资源包 Address (Container) 包含的所有 Lua 脚本缓存。
    /// 场景：明确知道要卸载哪个 Container 时调用。
    /// </summary>
    /// <param name="containerAddress">LuaScriptContainer 的资源地址</param>
    public static void ReleaseScriptCacheByContainer(string containerAddress)
    {
        if (string.IsNullOrEmpty(containerAddress)) return;
        if(_luaIndexAsset == null) return;

        if (_luaIndexAsset.ContainerToScripts.TryGetValue(containerAddress, out List<string> scriptNames))
        {
            int removeCount = 0;
            foreach (var scriptKey in scriptNames)
            {
                if (_contentCache.Remove(scriptKey))
                {
                    removeCount++;
                }
            }
            if (removeCount > 0)
            {
                Debug.Log($"[LuaLoader] 已释放容器 [{containerAddress}] 下的 {removeCount} 个脚本缓存。");
            }
        }
    }

    /// <summary>
    /// 清空所有脚本内容缓存
    /// 过场景、低内存警告时调用
    /// 注意：XLua 虚拟机内部的 package.loaded 依然存在，这里只是清理 loader 的缓存
    /// </summary>
    public static void ClearAllContentCache()
    {
        int count = _contentCache.Count;
        _contentCache.Clear();
        Debug.Log($"[LuaLoader] 清空所有内容缓存，清除{count} 个脚本缓存");
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 读磁盘（Editor）
    /// </summary>
    private static byte[] TryReadFromEditor(Options opt, string key)
    {
        foreach (var root in opt.editorRoots)
        {
            foreach (var ext in opt.extensions)
            {
                string path = Path.Combine(Application.dataPath, root, key + ext);
                if (File.Exists(path))
                {
                    try
                    {
                        return File.ReadAllBytes(path);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[LuaLoader] File read error: {path}\n{e.Message}");
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 加载预构建的 Lua 脚本索引
    /// </summary>
    private static async Task LoadPreBuildIndex()
    {
        try
        {
            var (indexSO, error) =
                await LuaAssetRuntime.Loader.LoadAssetAsync<LuaScriptsIndex>(LuaScriptsIndex.AssetAddress);

            if (error != null || indexSO == null)
                throw new InvalidOperationException(
                    error?.ToString() ?? $"无法加载启动资源: {LuaScriptsIndex.AssetAddress}");

            indexSO.BuildRuntimeDics();
            _luaIndexAsset = indexSO;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("LuaScriptsIndex 初始化失败。", e);
        }
    }

    /// <summary>
    /// 从当前绑定的资源包后端同步加载 Lua 内容
    /// </summary>
    private static byte[] LoadFromPackageSync(string containerAddress, string scriptName)
    {
        byte[] result = null;

        // 同步加载容器
        var (container, error) = LuaAssetRuntime.Loader.LoadAssetSync<LuaScriptContainer>(containerAddress);
        if (error != null)
        {
            Debug.LogError(error.ToString());
            return null;
        }

        if (container != null)
        {
            var asset = container.luaAssets.FirstOrDefault(a => NormalizeModuleKey(a.name) == scriptName);

            if (asset != null)
            {
                // 复制一份 byte[]，因为 TextAsset 马上要跟随 Bundle 卸载
                result = asset.bytes;
            }

            // 立即卸载容器，只保留bytes
            LuaAssetRuntime.Loader.UnloadAsset<LuaScriptContainer>(containerAddress);
        }

        return result;
    }

    #region 小工具

    /// <summary>
    /// 标准化模块名
    /// </summary>
    public static string NormalizeModuleKey(string filepath)
    {
        if (string.IsNullOrEmpty(filepath)) return string.Empty;

        // 统一路径格式
        string key = filepath.Replace('\\', '/');

        // 移除扩展名
        foreach (var ext in new[] { ".lua", ".lua.txt", ".bytes" })
        {
            if (key.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(0, key.Length - ext.Length);
                break;
            }
        }

        // 转换点路径为目录路径
        return key.Replace('.', '/');
    }

    #endregion

    #endregion
}

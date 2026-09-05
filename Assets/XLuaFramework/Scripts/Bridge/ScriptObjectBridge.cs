using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

public class ScriptObjectBridge : MonoBehaviour,IBridge
{
    [Tooltip("SO配置的 Package Address")]
    public string configKey;
    
    private ScriptObjectBridgeConfig _config; 
    private Dictionary<string, ScriptableObject> _soCache = new();
    
    public ScriptableObject GetSO(string luaKey)
    {
        if (_soCache.TryGetValue(luaKey, out var so))
            return so;
    
        Debug.LogError($"[ScriptObjectBridge] 未找到 SO: {luaKey} | 已加载: {string.Join(", ", _soCache.Keys)}");
        return null;
    }

    public async Task InitializeAsync(LuaTable luaInstance)
    {
        if (string.IsNullOrEmpty(configKey))
        {
            Debug.LogWarning($"[ScriptObjectBridge] {gameObject.name} 未配置 Config Key");
            return;
        }

        var (config, configError) =
            await LuaAssetRuntime.Loader.LoadAssetAsync<ScriptObjectBridgeConfig>(configKey);
        if (configError != null || config == null)
            throw new InvalidOperationException(
                configError?.ToString() ?? $"[ScriptObjectBridge] 加载配置失败: {configKey}");
        _config = config;
        
        foreach (var entry in _config.entries)
        {
            if (string.IsNullOrEmpty(entry.assetKey)) continue;
        
            try
            {
                var (so, loadError) =
                    await LuaAssetRuntime.Loader.LoadAssetAsync<ScriptableObject>(entry.assetKey);
                if (so != null)
                    _soCache[entry.luaKey] = so;
                else
                    Debug.LogWarning(
                        loadError?.ToString() ??
                        $"[ScriptObjectBridge] 加载失败: {entry.luaKey} ({entry.assetKey})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScriptObjectBridge] 加载异常 {entry.luaKey}: {e.Message}");
            }
        }
    }
    
    // 可根据需要，根据生命周期进行释放
}


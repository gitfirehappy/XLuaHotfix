using UnityEngine;

/// <summary>
/// 定义单个 Lua 脚本的配置
/// </summary>
[System.Serializable]
public class LuaScriptConfig
{
    [Tooltip("require('{luaScriptName}') 加载的脚本名称")]
    public string luaScriptName;
    
    [Tooltip("Class 模式: 脚本返回一个 Class，需要调用 New() 来实例化。\nModule 模式: 脚本本身 (require 的返回值) 就是实例。")]
    public LuaBehaviourBridge.LuaScriptMode luaScriptMode = LuaBehaviourBridge.LuaScriptMode.Class;

    /// <summary>
    /// 获取用于 'require' 的有效脚本名称
    /// </summary>
    public string GetScriptLoadName()
    {
        // 优先使用 luaScriptName，其次使用 TextAsset 的名称
        if (!string.IsNullOrEmpty(luaScriptName))
        {
            return luaScriptName;
        }
        return null;
    }
}
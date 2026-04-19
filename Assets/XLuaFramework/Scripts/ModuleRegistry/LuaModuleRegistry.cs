using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

public static class LuaModuleRegistry
{
    /// <summary>
    /// require常用的Lua模块
    /// </summary>
    public static void Initialize()
    {
        LuaEnv luaEnv = LuaEnvManager.Get();
        
        luaEnv.DoString(@"
            -- 加载模块并设置为全局变量
            ModuleRegistry = require 'ModuleRegistry'
            -- 调用初始化
            ModuleRegistry.Init()
        ");
        Debug.Log("Initialized regular Lua modules");
    }
}

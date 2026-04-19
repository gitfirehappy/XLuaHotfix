using System;
using UnityEngine;
using XLua;

/// <summary>
/// C#的Lua协程调度器（不直接控制协程）
/// </summary>
public static class LuaCoroutineScheduler
{
    private static int _idCounter;
    
    /// <summary>
    /// 生成唯一Lua协程ID
    /// </summary>
    public static int GenerateLuaCoID() => ++_idCounter;
    
    /// <summary>
    /// 通知Lua协程完成
    /// </summary>
    public static void NotifyLuaComplete(int id)
    {
        // 直接转发到CoroutineBridge处理
        CoroutineBridge.NotifyLuaComplete(id);
    }

    /// <summary>
    /// 恢复Lua协程
    /// </summary>
    /// <param name="luaCoId"></param>
    public static void Resume(int luaCoId,LuaEnv luaEnv)
    {
        if (luaEnv == null)
        {
            Debug.LogWarning($"Skip resume L#{luaCoId} - LuaEnv invalid");
            return;
        }
    
        try 
        {
            luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "LuaResume");
        }
        catch (Exception e)
        {
            Debug.LogError($"Resume failed: {e.Message}");
        }
    }
}
using System;
using System.Collections;
using UnityEngine;
using XLua;

public class CoroutineTester : MonoBehaviour
{
    private LuaEnv _luaEnv;
    private bool _isInitialized;
    public string[] customLuaPaths = { "Assets/AboutXLua/LuaScripts/Core/CoroutineScheduler" }; // 自定义路径
    
    [ContextMenu("1. 初始化环境")]
    public void Init()
    {
        if (_isInitialized) return;
        
        CSharpCoroutineScheduler.Init(this);
        
        _luaEnv = new LuaEnv();
        LuaEnvManager.Set(_luaEnv);
        
        // 注册自定义加载器
        _luaEnv.AddLoader((ref string path) => {
            foreach (var basePath in customLuaPaths) {
                string fullPath = $"{basePath}/{path.Replace('.', '/')}.lua";
                if (System.IO.File.Exists(fullPath)) {
                    return System.IO.File.ReadAllBytes(fullPath);
                }
            }
            return null;
        });
        
        // 加载常用的Lua模块
        LuaModuleRegistry.Initialize();
        
        // 正确加载Lua模块
        _luaEnv.DoString("coroutineBridge = require 'coroutineBridge'");
        
        _isInitialized = true;
        Debug.Log("✅ 环境初始化完成");
    }

    [ContextMenu("2. 测试C#协程")]
    public void TestCSharpCoroutine()
    {
        CheckInit();
        int id = CSharpCoroutineScheduler.StartCoroutine(SimpleCSharpCoroutine());
        Debug.Log($"🚀 C#协程启动 ID:{id}");
    }

    private IEnumerator SimpleCSharpCoroutine()
    {
        int id = CSharpCoroutineScheduler.GetCurrentCoroutineId();
        Debug.Log($"⚡ C#{id} 开始");
        yield return new WaitForSeconds(0.5f);
        Debug.Log($"⚡ C#{id} 运行中");
        yield return new WaitForSeconds(0.5f);
        Debug.Log($"✅ C#{id} 完成");
    }

    [ContextMenu("3. 测试Lua协程")]
    public void TestLuaCoroutine()
    {
        CheckInit();
        object[] result = _luaEnv.DoString(@"
        local id = coroutineBridge.create(function()
            print('🌌🌌 Lua#'..coroutineBridge.get_current_id()..' 开始')
            coroutine.yield()
            print('🌌🌌 Lua#'..coroutineBridge.get_current_id()..' 运行中')
            coroutine.yield()
            print('✅ Lua#'..coroutineBridge.get_current_id()..' 完成')
        end)
        print('🚀🚀 Lua协程启动 ID:'..id)
        
        -- 仅返回ID，不立即恢复
        return id
    ", "LuaTest");
    
        // 安全类型转换
        int luaCoId = Convert.ToInt32(result[0]);
        Debug.Log($"🚀🚀 获取Lua协程ID: {luaCoId}");

        // 第一次恢复（在主线程执行）
        _luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "InitialResume");
    
        // 后续恢复仍通过协程处理
        StartCoroutine(ResumeLuaCoroutine(luaCoId));
    }

    private IEnumerator ResumeLuaCoroutine(int luaCoId)
    {
        yield return new WaitForSeconds(0.5f);
    
        // 第二次恢复（执行到第二个yield）
        _luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "ResumeLua1");
        Debug.Log("🔁 第二次恢复Lua协程");
    
        yield return new WaitForSeconds(0.5f);
    
        // 第三次恢复（执行完成）
        _luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "ResumeLua2");
        Debug.Log("🔁 第三次恢复Lua协程");
    }

    [ContextMenu("4. 测试Lua等待C#")]
    public void TestLuaWaitCSharp()
    {
        CheckInit();
    
        // 简化Lua代码，使用XLua的协程生成器
        object[] result = _luaEnv.DoString(@"
        local csId = coroutineBridge.run_csharp_coroutine(function()
            -- 直接使用XLua的协程机制
            coroutine.yield(CS.UnityEngine.WaitForSeconds(0.5))
            print(""C#协程完成"")
        end)
        
        local id = coroutineBridge.create(function()
            print('⏳⏳⏳ Lua开始等待C#'..csId)
            coroutineBridge.wait_for_csharp(csId)
            print('✅ Lua结束等待')
        end)
        
        coroutineBridge.resume(id)
        return id
    ", "LuaWaitTest");
    
        Debug.Log($"🔄🔄 Lua等待C#启动 ID:{result[0]}");
    }

    [ContextMenu("5. 测试C#等待Lua")]
    public void TestCSharpWaitLua()
    {
        CheckInit();
        int id = CSharpCoroutineScheduler.StartCoroutine(WaitForLuaRoutine());
        Debug.Log($"🔄 C#等待Lua启动 ID:{id}");
    }

    private IEnumerator WaitForLuaRoutine()
    {
        Debug.Log("⏳ C#开始等待Lua");
    
        // 创建Lua协程（包含两个yield）
        object[] result = _luaEnv.DoString(@"
        return coroutineBridge.create(function()
            print('🌌 被等待的Lua协程开始')
            coroutine.yield()
            print('🌌 被等待的Lua协程继续')
            coroutine.yield()
            print('✅ 被等待的Lua协程完成')
        end)
    ", "LuaTask");
    
        int luaId = Convert.ToInt32(result[0]);
        Debug.Log($"📡 创建Lua协程 ID:{luaId}");
    
        // 第一次恢复（执行到第一个yield）
        _luaEnv.DoString($"coroutineBridge.resume({luaId})", "ResumeLua1");
    
        // 启动一个协程，定时恢复Lua协程的后续步骤（关键修复）
        StartCoroutine(ResumeLuaCoroutine5(luaId));
    
        // C#等待Lua协程完成
        yield return CoroutineBridge.WaitForLuaCoroutine(luaId);
    
        Debug.Log("✅ C#结束等待");
    }

// 复用测试3中的恢复逻辑，依次恢复Lua协程的yield点
    private IEnumerator ResumeLuaCoroutine5(int luaCoId)
    {
        yield return new WaitForSeconds(0.5f);
        _luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "ResumeLua2"); // 第二次恢复（到第二个yield）
    
        yield return new WaitForSeconds(0.5f);
        _luaEnv.DoString($"coroutineBridge.resume({luaCoId})", "ResumeLua3"); // 第三次恢复（完成）
    }

    [ContextMenu("6. 清理环境")]
    public void Cleanup()
    {
        if (!_isInitialized) return;
        
        LuaEnvManager.Dispose();
        _luaEnv = null;
        _isInitialized = false;
        
        Debug.Log("🧹 环境已清理");
    }

    private void CheckInit()
    {
        if (!_isInitialized) 
            throw new System.Exception("❌ 请先初始化环境!");
    }

    private void OnDestroy() => Cleanup();
}
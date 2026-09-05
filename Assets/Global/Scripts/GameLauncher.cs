using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 项目启动器，管理项目启动流程
/// </summary>
public class GameLauncher : MonoBehaviour
{
    public static bool IsReady { get; private set; }
    
    [Header("Lua Loader Configuration")]
    [Tooltip("Lua加载模式")]
    public XLuaLoader.Mode loaderMode = XLuaLoader.Mode.Hybrid;
    
    [Tooltip("编辑器模式下的Lua脚本根目录")]
    public List<string> editorRoots = new() { "LuaScripts" };

    [SerializeField]
    private FYAssetBackendSettings backendSettings;
    
    async void Awake()
    {
        try
        {
            await BootPhase();
            await InitPhase();
            await StartPhase();
        
            LaunchSignal.NotifyLaunched();
            IsReady = true;
            Debug.Log("[GameLauncher] 所有系统启动完毕");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameLauncher] failed: {e}");
        }
    }

    private async Task BootPhase()
    {
        Debug.Log("[GameLauncher] === Boot Phase ===");

        // XLuaFramework 资源服务注入（显式启动管理点）：必须先于任何 lua 桥链加载。
        LuaAssetRuntime.SetLoader(new FYAssetLuaAssetLoaderAdapter());
        
        if (backendSettings == null)
            throw new InvalidOperationException("GameLauncher 未引用 FYAssetBackendSettings。");
        if (!BackendModeNames.IsValid(backendSettings.Backend))
            throw new InvalidOperationException("FYAssetBackendSettings 未选择有效 BackendMode。");

        BackendMode backendMode = backendSettings.Backend;
        await HotfixManager.InitializeAsync(backendMode);
        
        // 创建Lua环境
        LuaEnvManager.CreateNewEnv();
        
        // Lua加载器加载lua脚本
        var loaderOptions = new XLuaLoader.Options
        {
            mode = loaderMode,
            editorRoots = editorRoots
        };
        await XLuaLoader.SetupAndRegister(LuaEnvManager.Get(), loaderOptions);

        await Task.CompletedTask;
    }
    
    private async Task InitPhase()
    {
        Debug.Log("[GameLauncher] === Init Phase ===");
        
        // 常用模块初始化
        LuaModuleRegistry.Initialize();

        await Task.CompletedTask;
    }
    
    private async Task StartPhase()
    {
        Debug.Log("[GameLauncher] === Start Phase ===");
        
        // UI初始化
        await GameUIManager.Instance.Initialize();
        
        await Task.CompletedTask;
    }
}

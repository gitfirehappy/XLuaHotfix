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
    
    [Header("XLua Configuration")]
    [Tooltip("Addressables标签名，用于加载XLua类型配置")]
    public string xluaConfigLabel = Constants.DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL;
    
    [Header("Lua Loader Configuration")]
    [Tooltip("Lua加载模式")]
    public XLuaLoader.Mode loaderMode = XLuaLoader.Mode.Hybrid;
    
    [Tooltip("编辑器模式下的Lua脚本根目录")]
    public List<string> editorRoots = new() { "LuaScripts" };
    
    // TODO: 替换为SO资源，AAPackageManager获取
    [Tooltip("Addressables标签，用于加载Lua脚本")]
    public List<string> aaLabels = new() { "LuaScriptContainer" }; // 初始第一批必备加载，后续可按需加载
    
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
        
        // HotfixManager 初始化
        
        // xlua标签管理初始化
        await XluaTypeConfigLoader.InitAsync(xluaConfigLabel);
        
        // 创建Lua环境
        LuaEnvManager.CreateNewEnv();
        
        // Lua加载器加载lua脚本
        var loaderOptions = new XLuaLoader.Options
        {
            mode = loaderMode,
            editorRoots = editorRoots,
            //aaLabels = aaLabels,
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
        
        // 对话功能注册
        DialogueFuncRegistry.ScanAndRegister();
        
        // UI初始化
        await GameUIManager.Instance.Initialize();
        
        await Task.CompletedTask;
    }
}

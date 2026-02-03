using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

public class GameUIManager : SingletonMono<GameUIManager>
{
    [Tooltip("UI配置资源的Addressable Key")]
    public string uiConfigKey = "UIResourceConfig";
    
    private UIResourceConfigSO _uiResourceConfig; 

    // TODO: 临时
    [SerializeField] private string _firstDialogueFileName;
    
    private LuaEnv _luaEnv;
    
    protected override async void Init()
    {
        await LaunchSignal.WaitForLaunch();
        
        _luaEnv = LuaEnvManager.Get();

        if (_uiResourceConfig == null) await Initialize();
        
        UIManager.Instance.ShowUIForm<DialoguePanel>();
        
        // 获取函数模块
        _luaEnv.DoString(@"
        local DialogueFuncRegistry = require('DialogueFuncRegistry')
        local DemoDialogueFunctions = require('DemoDialogueFunctions')
        
        -- 扫描并注册函数模块
        DialogueFuncRegistry.ScanModule(DemoDialogueFunctions, 'DemoDialogueFunctions')
    ");
        
        // 开启第一段对话
         _luaEnv.DoString($@"
             local DialogueController = require('DialogueController')
             DialogueController.Start('{_firstDialogueFileName}')
         ");
        
        Debug.Log("=== GameUIManager: Init ===");
    }

    public async Task Initialize()
    {
        if (string.IsNullOrEmpty(uiConfigKey))
        {
            Debug.LogError("[GameUIManager] uiConfigKey 为空，无法加载UI配置。");
            return;
        }

        _uiResourceConfig = await AAPackageManager.Instance.LoadAssetAsync<UIResourceConfigSO>(uiConfigKey);
    
        if (_uiResourceConfig != null)
        {
            UIManager.Instance.Initialize(_uiResourceConfig);
        }
        else
        {
            Debug.LogError($"[GameUIManager] 加载 UIResourceConfigSO 失败: {uiConfigKey}");
        }
    }
}

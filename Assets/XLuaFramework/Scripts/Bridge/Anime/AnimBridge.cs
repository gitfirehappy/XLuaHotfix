using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using XLua;

public class AnimBridge : MonoBehaviour,IBridge
{
    [Header("动画配置 Package Address")]
    public string configKey;
    
    private StateAnimationConfigSO _animationConfig; 
    
    [Header("动画驱动模式")]
    public AnimatorDriver.ControlMode mode = AnimatorDriver.ControlMode.CodeDriven;
    
    private IAnimationDriver driver;
    private Dictionary<string, StateAnimationConfigSO.StateAnimationMapping> animationCache;
    
    private string currentStatePath;
    
    public async Task InitializeAsync(LuaTable luaInstance)
    {
        if (!string.IsNullOrEmpty(configKey))
        {
            var (config, error) =
                await LuaAssetRuntime.Loader.LoadAssetAsync<StateAnimationConfigSO>(configKey);
            _animationConfig = config;
            if (error != null)
                Debug.LogWarning($"[AnimBridge] {error}");
        }
        else
        {
            Debug.LogWarning($"[AnimBridge] {gameObject.name} 未配置 Animation Config Key");
        }
        
        driver = new AnimatorDriver(mode);
        driver.Initialize(gameObject);
        
        // 初始化动画缓存
        animationCache = new Dictionary<string, StateAnimationConfigSO.StateAnimationMapping>();
        if (_animationConfig != null)
        {
            foreach (var mapping in _animationConfig.stateAnimations)
            {
                if (!string.IsNullOrEmpty(mapping.statePath))
                {
                    animationCache[mapping.statePath] = mapping;
                }
            }
        }
        else
        {
            Debug.LogWarning("[AnimBridge] AnimationConfig is missing!");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 播放指定状态路径的动画
    /// </summary>
    public void PlayState(string statePath)
    {
        if (string.IsNullOrEmpty(statePath) || driver == null) return;
        
        currentStatePath = statePath;
        
        if (animationCache.TryGetValue(statePath, out var mapping))
        {
            if (mapping.animationClip != null)
            {
                if (mode == AnimatorDriver.ControlMode.CodeDriven)
                {
                    // CodeDriven 模式直接播放动画片段
                    driver.Play(mapping.animationClip.name);
                }
                else
                {
                    // GraphDriven 模式使用状态名
                    driver.Play(statePath);
                }
            }
        }
        else
        {
            // 使用默认动画或状态名
            driver.Play(statePath);
            Debug.LogWarning($"[AnimBridge] 未找到状态 {statePath} 的动画配置，使用状态名播放");
        }
    }
    
    /// <summary>
    /// 获取当前状态路径
    /// </summary>
    public string GetCurrentStatePath()
    {
        return currentStatePath;
    }
    
    /// <summary>
    /// 检查是否有指定状态路径的动画
    /// </summary>
    public bool HasStateAnimation(string statePath)
    {
        return animationCache.ContainsKey(statePath);
    }
    
    public void Stop() => driver?.Stop();
    public void SetFloat(string name, float value) => driver?.SetFloat(name, value);
    public void SetBool(string name, bool value) => driver?.SetBool(name, value);
    public void SetTrigger(string name) => driver?.SetTrigger(name);
    public void SetInt(string name, int value) => driver?.SetInt(name, value);
}

using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using XLua;

public class InputBridge : MonoBehaviour, IBridge
{
    private LuaTable luaInstance;
    private InputActionAsset inputAsset;
    private Dictionary<string, InputAction> actionCache = new();
    private Dictionary<InputAction, Dictionary<string, LuaFunction>> luaCallbacks = new();
    
    public async Task InitializeAsync(LuaTable luaTable)
    {
        luaInstance = luaTable;
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            SetInputSource(playerInput);
        }
        else
        {
            Debug.LogWarning($"[InputBridge] 没有在 {gameObject.name} 找到 PlayerInput 或 InputActionAsset.");
        }
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 设置输入来源（支持 InputActionAsset 或 PlayerInput）
    /// </summary>
    public void SetInputSource(Object inputSource)
    {
        switch (inputSource)
        {
            case InputActionAsset asset:
                inputAsset = asset;
                inputAsset.Enable();
                break;
            case PlayerInput playerInput:
                inputAsset = playerInput.actions;
                inputAsset.Enable();
                break;
            default:
                Debug.LogWarning($"[InputBridge] Unsupported input source: {inputSource}");
                break;
        }
    }

    #region 绑定与解绑输入事件
    
    /// <summary>
    /// 绑定输入事件到 Lua
    /// </summary>
    public void BindAction(string path, string phase, LuaFunction callback)
    {
        var action = GetAction(path);
        if (action == null) return;

        if (!luaCallbacks.ContainsKey(action))
            luaCallbacks[action] = new Dictionary<string, LuaFunction>();

        luaCallbacks[action][phase] = callback;

        phase = phase.ToLower();
        if (phase == "started")
        {
            action.started += ctx => callback?.Call(luaInstance, action.name, phase);
        }
        else if (phase == "performed")
        {
            action.performed += ctx => callback?.Call(luaInstance, action.name, phase);
        }
        else if (phase == "canceled")
        {
            action.canceled += ctx => callback?.Call(luaInstance, action.name, phase);
        }
    }
    
    public void UnbindAction(string path, string phase)
    {
        var action = GetAction(path);
        if (action == null) return;

        if (luaCallbacks.TryGetValue(action, out var dict) && dict.ContainsKey(phase))
        {
            dict[phase]?.Dispose();
            dict.Remove(phase);
        }
    }
    
    #endregion

    #region 读取输入值

    public Vector2 GetVector2(string path)
    {
        var action = GetAction(path);
        return action != null ? action.ReadValue<Vector2>() : Vector2.zero;
    }

    public float GetFloat(string path)
    {
        var action = GetAction(path);
        return action != null ? action.ReadValue<float>() : 0f;
    }

    public bool GetBool(string path)
    {
        var action = GetAction(path);
        if (action == null) return false;
        return action.ReadValue<float>() > 0.5f;
    }

    #endregion

    #region 工具方法

    private InputAction GetAction(string path)
    {
        if (actionCache.TryGetValue(path, out var cached))
            return cached;

        var split = path.Split('/');
        if (split.Length != 2)
        {
            Debug.LogWarning($"[InputBridge] Invalid path: {path} (should be 'Map/Action')");
            return null;
        }

        var map = inputAsset.FindActionMap(split[0]);
        var action = map?.FindAction(split[1]);

        if (action == null)
        {
            Debug.LogWarning($"[InputBridge] Action not found: {path}");
            return null;
        }

        actionCache[path] = action;
        return action;
    }

    #endregion
    
    private void OnDestroy()
    {
        foreach (var kvp in luaCallbacks)
        {
            foreach (var func in kvp.Value.Values)
                func?.Dispose();
        }
        luaCallbacks.Clear();
    }
}

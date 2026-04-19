using System;
using System.Collections.Generic;
using UnityEngine;
using XLua;

public class EventCentre : Singleton<EventCentre>
{
    /// <summary>
    ///不同的事件端口
    /// </summary>
    public enum EventPort
    {
        CsharpToCsharp,
        LuaToLua,
        LuaToCsharp,
        CsharpToLua
    }

    /// <summary> 字典存储不同端口的事件 </summary>
    private Dictionary<EventPort, Dictionary<string, Delegate>> eventDictionaries = new();

    /// <summary>
    /// 存储Lua事件注册时的委托实例（解决委托引用比较问题）
    /// 键：(端口, 事件名, LuaFunction) 唯一标识
    /// 值：注册时创建的委托实例
    /// </summary>
    private Dictionary<Tuple<EventPort, string, LuaFunction>, Delegate> luaDelegateMap = new();
    
    public event Action OnEventChanged;
    
    // 初始化事件字典
    public EventCentre()
    {
        foreach (EventPort port in Enum.GetValues(typeof(EventPort)))
        {
            eventDictionaries[port] = new Dictionary<string, Delegate>();
        }
    }

    #region 通用事件管理方法

    /// <summary>
    /// 添加事件监听
    /// </summary>
    public void AddEvent(EventPort port, string eventName, Delegate action)
    {
        var portEvents = eventDictionaries[port];

        if (portEvents.ContainsKey(eventName))
        {
            portEvents[eventName] = Delegate.Combine(portEvents[eventName], action);
        }
        else
        {
            portEvents.Add(eventName, action);
        }
        OnEventChanged?.Invoke();
    }

    /// <summary>
    /// 移除事件监听
    /// </summary>
    public void RemoveEvent(EventPort port, string eventName, Delegate action)
    {
        if (eventDictionaries[port].ContainsKey(eventName))
        {
            eventDictionaries[port][eventName] = Delegate.Remove(eventDictionaries[port][eventName], action);

            // 如果没有监听器了，移除事件
            if (eventDictionaries[port][eventName] == null)
            {
                eventDictionaries[port].Remove(eventName);
            }
            OnEventChanged?.Invoke();
        }
        else
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", "事件不存在");
        }
    }
    
    /// <summary>
    /// 触发事件（多参数兼容版）
    /// </summary>
    public void TriggerEvent(EventPort port, string eventName, params object[] args)
    {
        switch (args.Length)
        {
            case 0:
                TriggerEvent(port, eventName);
                break;
            case 1:
                TriggerEvent(port, eventName, args[0]);
                break;
            case 2:
                TriggerEvent(port, eventName, args[0], args[1]);
                break;
            default:
                // 仅对3个以上参数使用DynamicInvoke
                if (eventDictionaries[port].TryGetValue(eventName, out var handler))
                {
                    handler?.DynamicInvoke(args);
                }
                else
                {
                    LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 不存在");
                }

                break;
        }
    }

    #region 触发事件（多种参数类型）
    
    /// <summary>
    /// 触发事件（无参数）
    /// </summary>
    private void TriggerEvent(EventPort port, string eventName)
    {
        if (eventDictionaries[port].TryGetValue(eventName, out var handler))
        {
            if (handler is Action action)
            {
                action.Invoke();
                return;
            }

            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 委托类型不匹配（预期无参Action）");
        }
        else
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 不存在");
        }
    }

    /// <summary>
    /// 触发事件（1个参数）
    /// </summary>
    private void TriggerEvent<T>(EventPort port, string eventName, T arg)
    {
        if (eventDictionaries[port].TryGetValue(eventName, out var handler))
        {
            if (handler is Action<T> action)
            {
                action.Invoke(arg);
                return;
            }

            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 委托类型不匹配（预期Action<T>）");
        }
        else
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 不存在");
        }
    }

    /// <summary>
    /// 触发事件（2个参数）
    /// </summary>
    private void TriggerEvent<T1, T2>(EventPort port, string eventName, T1 arg1, T2 arg2)
    {
        if (eventDictionaries[port].TryGetValue(eventName, out var handler))
        {
            if (handler is Action<T1, T2> action)
            {
                action.Invoke(arg1, arg2);
                return;
            }

            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 委托类型不匹配（预期Action<T1,T2>）");
        }
        else
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"事件 {eventName} 不存在");
        }
    }

    #endregion
    
    #endregion

    #region 特定端口的事件方法

    #region C# 到 C# 事件

    public void AddCSharpEvent(string eventName, Action action)
    {
        AddEvent(EventPort.CsharpToCsharp, eventName, action);
    }

    public void AddCSharpEvent<T>(string eventName, Action<T> action)
    {
        AddEvent(EventPort.CsharpToCsharp, eventName, action);
    }

    public void AddCSharpEvent<T1, T2>(string eventName, Action<T1, T2> action)
    {
        AddEvent(EventPort.CsharpToCsharp, eventName, action);
    }

    public void TriggerCSharpEvent(string eventName)
    {
        TriggerEvent(EventPort.CsharpToCsharp, eventName);
    }

    public void TriggerCSharpEvent<T>(string eventName, T arg)
    {
        TriggerEvent(EventPort.CsharpToCsharp, eventName, arg);
    }

    public void TriggerCSharpEvent<T1, T2>(string eventName, T1 arg1, T2 arg2)
    {
        TriggerEvent(EventPort.CsharpToCsharp, eventName, arg1, arg2);
    }

    #endregion

    #region Lua 到 Lua 事件

    public void AddLuaToLuaEvent(string eventName, Action<LuaTable> action)
    {
        AddEvent(EventPort.LuaToLua, eventName, action);
    }

    public void TriggerLuaToLuaEvent(string eventName, LuaTable data = null)
    {
        TriggerEvent(EventPort.LuaToLua, eventName, data);
    }

    #endregion

    #region Lua 到 C# 事件

    public void AddLuaToCSharpEvent(string eventName, Action action)
    {
        AddEvent(EventPort.LuaToCsharp, eventName, action);
    }

    public void AddLuaToCSharpEvent<T>(string eventName, Action<T> action)
    {
        AddEvent(EventPort.LuaToCsharp, eventName, action);
    }

    public void TriggerLuaToCSharpEvent(string eventName)
    {
        TriggerEvent(EventPort.LuaToCsharp, eventName);
    }

    public void TriggerLuaToCSharpEvent<T>(string eventName, T arg)
    {
        TriggerEvent(EventPort.LuaToCsharp, eventName, arg);
    }

    #endregion

    #region C# 到 Lua 事件

    public void AddCSharpToLuaEvent(string eventName, Action<LuaTable> action)
    {
        AddEvent(EventPort.CsharpToLua, eventName, action);
    }

    public void TriggerCSharpToLuaEvent(string eventName, LuaTable data = null)
    {
        TriggerEvent(EventPort.CsharpToLua, eventName, data);
    }

    #endregion

    #endregion

    #region Lua 交互方法

    // 供Lua调用的通用方法
    
    /// <summary>
    /// 注册Lua事件（保存委托实例）
    /// </summary>
    public static void RegisterLuaEvent(EventPort port, string eventName, LuaFunction handler)
    {
        if (handler == null)
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", "注册Lua事件失败：handler为空");
            return;
        }

        var key = Tuple.Create(port, eventName, handler);
        switch (port)
        {
            case EventPort.LuaToLua:
                Action<LuaTable> luaToLuaAction = (data) => handler.Call(data);
                Instance.AddLuaToLuaEvent(eventName, luaToLuaAction);
                Instance.luaDelegateMap[key] = luaToLuaAction; // 保存委托
                break;
            case EventPort.LuaToCsharp:
                Action luaToCSharpAction = () => handler.Call();
                Instance.AddLuaToCSharpEvent(eventName, luaToCSharpAction);
                Instance.luaDelegateMap[key] = luaToCSharpAction; // 保存委托
                break;
            case EventPort.CsharpToLua:
                Action<LuaTable> csharpToLuaAction = (data) => handler.Call(data);
                Instance.AddCSharpToLuaEvent(eventName, csharpToLuaAction);
                Instance.luaDelegateMap[key] = csharpToLuaAction; // 保存委托
                break;
            default:
                LogUtility.Warning(LogLayer.Framework, "EventCentre", "Lua cannot register for C# to C# events");
                break;
        }
    }
    
    /// <summary>
    /// 取消注册Lua事件（复用注册时的委托实例）
    /// </summary>
    public static void UnregisterLuaEvent(EventPort port, string eventName, LuaFunction handler)
    {
        if (handler == null)
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", "取消注册Lua事件失败：handler为空");
            return;
        }

        var key = Tuple.Create(port, eventName, handler);
        if (Instance.luaDelegateMap.TryGetValue(key, out var targetDelegate))
        {
            // 使用注册时的委托实例进行移除
            Instance.RemoveEvent(port, eventName, targetDelegate);
            Instance.luaDelegateMap.Remove(key); // 清理映射
        }
        else
        {
            LogUtility.Warning(LogLayer.Framework, "EventCentre", $"未找到需要取消的事件：port={port}, eventName={eventName}");
        }
    }

    /// <summary>
    /// 触发Lua事件
    /// </summary>
    /// <param name="port"></param>
    /// <param name="eventName"></param>
    /// <param name="data"></param>
    public static void TriggerEventFromLua(EventPort port, string eventName, LuaTable data = null)
    {
        switch (port)
        {
            case EventPort.LuaToLua:
                Instance.TriggerLuaToLuaEvent(eventName, data);
                break;
            case EventPort.LuaToCsharp:
                Instance.TriggerLuaToCSharpEvent(eventName);
                break;
            case EventPort.CsharpToLua:
                Instance.TriggerCSharpToLuaEvent(eventName, data);
                break;
            default:
                LogUtility.Warning(LogLayer.Framework, "EventCentre", "Lua cannot trigger C# to C# events");
                break;
        }
    }

    #endregion

    #region 实用方法
    /// <summary>
    /// 检查事件是否存在
    /// </summary>
    public bool HasEvent(EventPort port, string eventName)
    {
        return eventDictionaries[port].ContainsKey(eventName) && 
               eventDictionaries[port][eventName] != null;
    }

    /// <summary>
    /// 获取事件监听器数量
    /// </summary> 
    public int GetEventListenerCount(EventPort port, string eventName)
    {
        if (!eventDictionaries[port].ContainsKey(eventName) || 
            eventDictionaries[port][eventName] == null)
            return 0;
        
        return eventDictionaries[port][eventName].GetInvocationList().Length;
    }

    /// <summary>
    /// 清除所有事件
    /// </summary>
    public void ClearAllEvents()
    {
        foreach (var portEvents in eventDictionaries.Values)
        {
            portEvents.Clear();
        }
        luaDelegateMap.Clear();
    }

    #endregion
}
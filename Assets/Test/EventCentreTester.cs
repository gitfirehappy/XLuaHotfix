using UnityEngine;
using XLua;
using System;

[LuaCallCSharp]
public class EventCentreTester : MonoBehaviour
{
    // 自定义Lua Loader路径配置（根据实际项目路径修改）
    public string[] customLuaPaths = {"Assets/AboutXLua/LuaScripts/"};

    // 测试用标志位
    private bool csharpToCsharpTriggered;
    private bool luaToLuaTriggered;
    private bool luaToCsharpTriggered;
    private bool csharpToLuaTriggered;

    // Lua环境（实际项目中可能已有全局实例）
    private LuaEnv luaEnv;

    private void Awake()
    {
        luaEnv = new LuaEnv();
        LuaEnvManager.Set(luaEnv);
        
        // 注册自定义加载器
        luaEnv.AddLoader((ref string path) => {
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
        
        // 初始化事件中心Lua端
        luaEnv.DoString("require 'EventCentre'");
        
        Debug.Log("初始化完成");
    }

    [ContextMenu("测试C#到C#事件")]
    public void TestCsharpToCsharp()
    {
        Debug.Log("=== 开始测试C#到C#事件 ===");
        string testEvent = "CsharpToCsharp_Test";
        csharpToCsharpTriggered = false;

        // 1. 注册事件
        Action testAction = () => 
        {
            csharpToCsharpTriggered = true;
            Debug.Log("C#到C#事件触发成功");
        };
        EventCentre.Instance.AddCSharpEvent(testEvent, testAction);

        // 2. 查询事件
        bool hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToCsharp, testEvent);
        int count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToCsharp, testEvent);
        Debug.Log($"注册后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        // 3. 触发事件
        EventCentre.Instance.TriggerCSharpEvent(testEvent);
        Debug.Log($"事件是否触发: {csharpToCsharpTriggered}");

        // 4. 取消注册
        EventCentre.Instance.RemoveEvent(EventCentre.EventPort.CsharpToCsharp, testEvent, testAction);

        // 5. 再次查询
        hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToCsharp, testEvent);
        count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToCsharp, testEvent);
        Debug.Log($"取消后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        Debug.Log("=== C#到C#事件测试结束 ===");
    }

    [ContextMenu("测试Lua到Lua事件")]
    public void TestLuaToLua()
    {
        Debug.Log("=== 开始测试Lua到Lua事件 ===");
        string testEvent = "LuaToLua_Test";
        luaToLuaTriggered = false;

        // 1. 注册事件（通过C#封装的LuaToLua事件接口）
        Action<LuaTable> testAction = (data) =>
        {
            luaToLuaTriggered = true;
            Debug.Log("Lua到Lua事件触发成功");
            if (data != null)
            {
                Debug.Log($"事件数据: {data.Get<int>("testValue")}");
            }
        };
        EventCentre.Instance.AddLuaToLuaEvent(testEvent, testAction);

        // 2. 查询事件
        bool hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToLua, testEvent);
        int count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToLua, testEvent);
        Debug.Log($"注册后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        // 3. 触发事件
        var data = luaEnv.NewTable();
        data.Set("testValue", 123);
        EventCentre.Instance.TriggerLuaToLuaEvent(testEvent, data);
        Debug.Log($"事件是否触发: {luaToLuaTriggered}");

        // 4. 取消注册
        EventCentre.Instance.RemoveEvent(EventCentre.EventPort.LuaToLua, testEvent, testAction);

        // 5. 再次查询
        hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToLua, testEvent);
        count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToLua, testEvent);
        Debug.Log($"取消后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        Debug.Log("=== Lua到Lua事件测试结束 ===");
    }

    [ContextMenu("测试Lua到C#事件")]
    public void TestLuaToCsharp()
    {
        Debug.Log("=== 开始测试Lua到C#事件 ===");
        string testEvent = "LuaToCsharp_Test";
        luaToCsharpTriggered = false;

        // 1. 注册事件（C#侧监听）
        Action testAction = () =>
        {
            luaToCsharpTriggered = true;
            Debug.Log("Lua到C#事件触发成功");
        };
        EventCentre.Instance.AddLuaToCSharpEvent(testEvent, testAction);

        // 2. 查询事件
        bool hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToCsharp, testEvent);
        int count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToCsharp, testEvent);
        Debug.Log($"注册后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        // 3. 触发事件（模拟Lua侧触发）
        EventCentre.Instance.TriggerLuaToCSharpEvent(testEvent);
        Debug.Log($"事件是否触发: {luaToCsharpTriggered}");

        // 4. 取消注册
        EventCentre.Instance.RemoveEvent(EventCentre.EventPort.LuaToCsharp, testEvent, testAction);

        // 5. 再次查询
        hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToCsharp, testEvent);
        count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToCsharp, testEvent);
        Debug.Log($"取消后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        Debug.Log("=== Lua到C#事件测试结束 ===");
    }

    [ContextMenu("测试C#到Lua事件")]
    public void TestCsharpToLua()
    {
        Debug.Log("=== 开始测试C#到Lua事件 ===");
        string testEvent = "CsharpToLua_Test";
        csharpToLuaTriggered = false;

        // 1. 注册事件（通过C#接口注册Lua回调）
        Action<LuaTable> testAction = (data) =>
        {
            csharpToLuaTriggered = true;
            Debug.Log("C#到Lua事件触发成功");
            if (data != null)
            {
                Debug.Log($"事件数据: {data.Get<string>("message")}");
            }
        };
        EventCentre.Instance.AddCSharpToLuaEvent(testEvent, testAction);

        // 2. 查询事件
        bool hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToLua, testEvent);
        int count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToLua, testEvent);
        Debug.Log($"注册后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        // 3. 触发事件
        var data = luaEnv.NewTable();
        data.Set("message", "Hello from C#");
        EventCentre.Instance.TriggerCSharpToLuaEvent(testEvent, data);
        Debug.Log($"事件是否触发: {csharpToLuaTriggered}");

        // 4. 取消注册
        EventCentre.Instance.RemoveEvent(EventCentre.EventPort.CsharpToLua, testEvent, testAction);

        // 5. 再次查询
        hasEvent = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToLua, testEvent);
        count = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToLua, testEvent);
        Debug.Log($"取消后 - 事件是否存在: {hasEvent}, 监听数量: {count}");

        Debug.Log("=== C#到Lua事件测试结束 ===");
    }

    [ContextMenu("测试清除所有事件")]
    public void TestClearAllEvents()
    {
        Debug.Log("=== 开始测试清除所有事件 ===");

        // 先注册一些测试事件
        EventCentre.Instance.AddCSharpEvent("TestClear1", () => { });
        EventCentre.Instance.AddLuaToLuaEvent("TestClear2", (data) => { });

        // 清除前查询
        bool has1 = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToCsharp, "TestClear1");
        bool has2 = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToLua, "TestClear2");
        Debug.Log($"清除前 - 事件1存在: {has1}, 事件2存在: {has2}");

        // 执行清除
        EventCentre.Instance.ClearAllEvents();

        // 清除后查询
        has1 = EventCentre.Instance.HasEvent(EventCentre.EventPort.CsharpToCsharp, "TestClear1");
        has2 = EventCentre.Instance.HasEvent(EventCentre.EventPort.LuaToLua, "TestClear2");
        Debug.Log($"清除后 - 事件1存在: {has1}, 事件2存在: {has2}");

        Debug.Log("=== 清除所有事件测试结束 ===");
    }
    
    [ContextMenu("测试事件中心窗口")]
    public void TestEventViewer()
    {
        Debug.Log("=== 开始测试事件中心窗口 ===");
    
        // 注册一些C#到C#事件
        EventCentre.Instance.AddCSharpEvent("TestViewer_Event1", () => {
            Debug.Log("TestViewer事件1触发");
        });
    
        EventCentre.Instance.AddCSharpEvent<int>("TestViewer_Event2", (value) => {
            Debug.Log($"TestViewer事件2触发，参数值: {value}");
        });
    
        // 注册Lua到Lua事件
        EventCentre.Instance.AddLuaToLuaEvent("TestViewer_LuaEvent1", (data) => {
            Debug.Log("TestViewer Lua事件1触发");
        });
    
        // 注册Lua到C#事件
        EventCentre.Instance.AddLuaToCSharpEvent("TestViewer_LuaToCSharp1", () => {
            Debug.Log("TestViewer Lua到C#事件1触发");
        });
    
        // 注册C#到Lua事件
        EventCentre.Instance.AddCSharpToLuaEvent("TestViewer_CSharpToLua1", (data) => {
            Debug.Log("TestViewer C#到Lua事件1触发");
        });
    
        // 查询并打印当前注册的事件数量
        int csharpEvents = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToCsharp, "TestViewer_Event1");
        int luaToLuaEvents = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToLua, "TestViewer_LuaEvent1");
        int luaToCsharpEvents = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.LuaToCsharp, "TestViewer_LuaToCSharp1");
        int csharpToLuaEvents = EventCentre.Instance.GetEventListenerCount(EventCentre.EventPort.CsharpToLua, "TestViewer_CSharpToLua1");
    
        Debug.Log($"注册完成后各端口事件数量 - " +
                  $"C#到C#: {csharpEvents}, " +
                  $"Lua到Lua: {luaToLuaEvents}, " +
                  $"Lua到C#: {luaToCsharpEvents}, " +
                  $"C#到Lua: {csharpToLuaEvents}");
    
        Debug.Log("事件已注册，请打开Event Viewer窗口查看。这些事件将保留在系统中直到测试完成或手动清除。");
        Debug.Log("=== 事件中心窗口测试结束 ===");
    }


    private void OnDestroy()
    {
        // 清理Lua环境
        if (luaEnv != null)
        {
            luaEnv.Dispose();
            luaEnv = null;
        }
    }
}
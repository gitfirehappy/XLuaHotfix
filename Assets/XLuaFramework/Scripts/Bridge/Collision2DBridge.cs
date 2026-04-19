using System.Threading.Tasks;
using UnityEngine;
using XLua;


public class Collision2DBridge : MonoBehaviour,IBridge
{
    private LuaTable luaInstance;
    private LuaFunction onCollisionEnterFunc, onCollisionExitFunc;
    private LuaFunction onTriggerEnterFunc, onTriggerExitFunc;
    private LuaFunction onCollisionStayFunc, onTriggerStayFunc;
    
    public async Task InitializeAsync(LuaTable luaTable)
    {
        luaInstance = luaTable;
        
        onCollisionEnterFunc = luaInstance.Get<LuaFunction>("OnCollisionEnter2D");
        onCollisionExitFunc = luaInstance.Get<LuaFunction>("OnCollisionExit2D");
        onCollisionStayFunc = luaInstance.Get<LuaFunction>("OnCollisionStay2D");
        onTriggerEnterFunc = luaInstance.Get<LuaFunction>("OnTriggerEnter2D");
        onTriggerExitFunc = luaInstance.Get<LuaFunction>("OnTriggerExit2D");
        onTriggerStayFunc = luaInstance.Get<LuaFunction>("OnTriggerStay2D");
        
        await Task.CompletedTask;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        onCollisionEnterFunc?.Call(luaInstance, collision);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        onCollisionExitFunc?.Call(luaInstance, collision);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        onCollisionStayFunc?.Call(luaInstance, collision);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        onTriggerEnterFunc?.Call(luaInstance, other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        onTriggerExitFunc?.Call(luaInstance, other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        onTriggerStayFunc?.Call(luaInstance, other);
    }

    public void OnDestroy()
    {
        // 清理Lua引用
        onCollisionEnterFunc?.Dispose();
        onCollisionExitFunc?.Dispose();
        onCollisionStayFunc?.Dispose();
        onTriggerEnterFunc?.Dispose();
        onTriggerExitFunc?.Dispose();
        onTriggerStayFunc?.Dispose();
    }
}
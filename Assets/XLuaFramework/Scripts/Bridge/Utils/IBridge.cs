using System.Threading.Tasks;
using XLua;

public interface IBridge
{
    // 初始化桥接组件，传入Lua实例
    Task InitializeAsync(LuaTable luaInstance);
}
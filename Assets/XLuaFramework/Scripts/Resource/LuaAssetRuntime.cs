using System;

/// <summary>
/// XLuaFramework 的资源加载显式注入口：零自举魔法，宿主在启动链显式调用 SetLoader。
/// 未注册即使用会 fail-fast，错误消息直接给出修复步骤。
/// </summary>
public static class LuaAssetRuntime
{
    private static ILuaAssetLoader _loader;

    public static bool IsRegistered => _loader != null;

    public static void SetLoader(ILuaAssetLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        UnityEngine.Debug.Log($"[LuaAssetRuntime] loader registered: {loader.GetType().FullName}");
    }

    /// <summary>测试或切换宿主期间清空注册。</summary>
    public static void Reset()
    {
        _loader = null;
    }

    public static ILuaAssetLoader Loader
    {
        get
        {
            if (_loader == null)
                throw new InvalidOperationException(
                    "LuaAssetRuntime loader not registered. Inject one at startup: " +
                    "LuaAssetRuntime.SetLoader(your ILuaAssetLoader implementation).");
            return _loader;
        }
    }
}

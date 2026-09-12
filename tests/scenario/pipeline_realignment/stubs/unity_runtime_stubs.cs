using System;

namespace UnityEngine
{
    /// <summary>
    /// UnityEngine.Object 的最小替身：AssetHandle{T} 的泛型约束与缓存字段需要它存在。
    /// 场景只验证句柄所有权语义，不模拟 Unity 资源生命周期。
    /// </summary>
    public class Object
    {
        public string name;
    }

    /// <summary>日志替身：生产代码通过它输出句柄告警，场景不校验日志文本。</summary>
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}

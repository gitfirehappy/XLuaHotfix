using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// IBuildTask 解析器 —— 启动时扫描全部程序集找到所有 IBuildTask 实现，
/// 按 TaskName 构建索引并缓存 Type。提供 CreateTask（实例化）和 Exists（存在性检查）。
/// </summary>
public static class BuildTaskResolver
{
    private static Dictionary<string, Type> _index;
    private static bool _initialized;

    /// <summary>扫描全部程序集构建 TaskName -> Type 索引，重复调用仅执行一次</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _index = new Dictionary<string, Type>(StringComparer.Ordinal);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }
            if (types == null) continue;

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (!typeof(IBuildTask).IsAssignableFrom(type))
                    continue;

                IBuildTask instance;
                try
                {
                    instance = (IBuildTask)Activator.CreateInstance(type);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(instance.TaskName))
                    continue;

                if (_index.TryGetValue(instance.TaskName, out var existingType))
                    throw new InvalidOperationException(
                        $"IBuildTask TaskName 重复 '{instance.TaskName}': " +
                        $"'{existingType.FullName}' 与 '{type.FullName}'。TaskName 必须唯一。");

                _index[instance.TaskName] = type;
            }
        }
        _initialized = true;
    }

    /// <summary>按 TaskName 创建 IBuildTask 实例，不存在时抛出 ArgumentException</summary>
    public static IBuildTask CreateTask(string taskName)
    {
        Initialize();
        if (!_index.TryGetValue(taskName, out var type))
            throw new ArgumentException($"未注册 TaskName 为 '{taskName}' 的 IBuildTask。");
        return (IBuildTask)Activator.CreateInstance(type);
    }

    /// <summary>检查指定 TaskName 是否存在对应的 IBuildTask 实现</summary>
    public static bool Exists(string taskName)
    {
        Initialize();
        return _index.ContainsKey(taskName);
    }

    /// <summary>按 TaskName 查询 IBuildTask 实现类型，供编辑器定位源码等只读功能使用。</summary>
    public static bool TryGetTaskType(string taskName, out Type type)
    {
        Initialize();
        return _index.TryGetValue(taskName, out type);
    }

    /// <summary>返回当前程序集扫描到的所有 TaskName，供编辑器创建菜单使用。</summary>
    public static string[] GetTaskNames()
    {
        Initialize();
        return _index.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}

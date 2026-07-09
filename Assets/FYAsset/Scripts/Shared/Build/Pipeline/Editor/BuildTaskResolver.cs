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
    private static List<TaskResolutionDiagnostic> _diagnostics;
    private static bool _initialized;

    /// <summary>扫描全部程序集构建 TaskName -> Type 索引，重复调用仅执行一次</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _index = new Dictionary<string, Type>(StringComparer.Ordinal);
        _diagnostics = new List<TaskResolutionDiagnostic>();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                RecordLoaderException(asm, ex);
                types = ex.Types;
            }
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
                catch (Exception ex)
                {
                    RecordConstructorException(asm, type, ex);
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

    /// <summary>尝试创建 Task，并把实例化异常转换成诊断文本。</summary>
    public static bool TryCreateTask(string taskName, out IBuildTask task, out string error)
    {
        Initialize();
        task = null;
        error = null;

        if (!_index.TryGetValue(taskName, out var type))
        {
            error = BuildMissingTaskMessage(taskName);
            return false;
        }

        try
        {
            task = (IBuildTask)Activator.CreateInstance(type);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Task '{taskName}' 构造失败: {type.FullName} — {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}";
            return false;
        }
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

    /// <summary>返回最近一次 Task 发现过程中的反射/构造诊断。</summary>
    public static IReadOnlyList<TaskResolutionDiagnostic> GetDiagnostics()
    {
        Initialize();
        return _diagnostics;
    }

    /// <summary>构建配置引用的 TaskName 缺失时，附带 resolver 诊断线索。</summary>
    public static string BuildMissingTaskMessage(string taskName)
    {
        Initialize();
        string message = $"'{taskName}' — 未找到对应的 IBuildTask 实现。";

        var matching = _diagnostics
            .Where(d => string.Equals(d.TaskNameHint, taskName, StringComparison.Ordinal)
                || string.Equals(d.TypeName, taskName, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(d.TypeFullName)
                    && d.TypeFullName.EndsWith("." + taskName, StringComparison.Ordinal)))
            .ToList();

        if (matching.Count == 0)
        {
            if (_diagnostics.Count == 0)
                return message;

            return message + " Resolver diagnostics: " + string.Join(" | ",
                _diagnostics.Take(5).Select(d => d.Message));
        }

        return message + " Resolver diagnostics: " + string.Join(" | ", matching.Select(d => d.Message));
    }

    private static void RecordLoaderException(Assembly assembly, ReflectionTypeLoadException ex)
    {
        if (ex.LoaderExceptions == null || ex.LoaderExceptions.Length == 0)
        {
            _diagnostics.Add(TaskResolutionDiagnostic.Loader(assembly.GetName().Name,
                $"Assembly '{assembly.GetName().Name}' GetTypes failed: {ex.Message}"));
            return;
        }

        for (int i = 0; i < ex.LoaderExceptions.Length; i++)
        {
            var loader = ex.LoaderExceptions[i];
            if (loader == null)
                continue;

            _diagnostics.Add(TaskResolutionDiagnostic.Loader(assembly.GetName().Name,
                $"Assembly '{assembly.GetName().Name}' loader exception: {loader.GetType().Name}: {loader.Message}"));
        }
    }

    private static void RecordConstructorException(Assembly assembly, Type type, Exception ex)
    {
        Exception root = Unwrap(ex);
        string typeName = type.FullName ?? type.Name;
        _diagnostics.Add(TaskResolutionDiagnostic.Constructor(
            assembly.GetName().Name,
            type.Name,
            typeName,
            type.Name,
            $"IBuildTask '{typeName}' constructor failed: {root.GetType().Name}: {root.Message}"));
    }

    private static Exception Unwrap(Exception ex)
    {
        return ex is TargetInvocationException tie && tie.InnerException != null
            ? tie.InnerException
            : ex;
    }
}

/// <summary>
/// BuildTaskResolver 初始化期间发现的非致命诊断。
/// </summary>
public readonly struct TaskResolutionDiagnostic
{
    public readonly string AssemblyName;
    public readonly string TypeName;
    public readonly string TypeFullName;
    public readonly string TaskNameHint;
    public readonly string Message;

    private TaskResolutionDiagnostic(string assemblyName, string typeName, string typeFullName, string taskNameHint, string message)
    {
        AssemblyName = assemblyName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        TypeFullName = typeFullName ?? string.Empty;
        TaskNameHint = taskNameHint ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public static TaskResolutionDiagnostic Loader(string assemblyName, string message)
    {
        return new TaskResolutionDiagnostic(assemblyName, string.Empty, string.Empty, string.Empty, message);
    }

    public static TaskResolutionDiagnostic Constructor(string assemblyName, string typeName, string typeFullName, string taskNameHint, string message)
    {
        return new TaskResolutionDiagnostic(assemblyName, typeName, typeFullName, taskNameHint, message);
    }
}

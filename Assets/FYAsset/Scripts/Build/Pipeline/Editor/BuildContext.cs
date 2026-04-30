using System.Collections.Generic;

/// <summary>
/// 构建管线上下文 —— 类型安全的 KV 数据总线。
/// 各 Task 通过 ReadKeys/WriteKeys 声明确保数据流可见性，
/// DAGScheduler 在 Phase 0 Validate 阶段校验 Read-before-Write 和 W-W 冲突。
/// 内部使用 Dictionary&lt;string, object&gt; 存储，兼容值类型和引用类型。
/// </summary>
public class BuildContext
{
    private readonly Dictionary<string, object> _data = new();

    /// <summary>写入键值对，同 Key 会被静默覆盖（W-W 冲突由 DAGScheduler Validate 前置检测）</summary>
    public void Set<T>(string key, T value)
    {
        _data[key] = value;
    }

    /// <summary>读取键值对，Key 不存在时返回 default(T)</summary>
    public T Get<T>(string key)
    {
        if (_data.TryGetValue(key, out object value))
            return (T)value;
        return default;
    }

    /// <summary>读取键值对，Key 不存在时抛出 KeyNotFoundException</summary>
    public T Require<T>(string key)
    {
        if (_data.TryGetValue(key, out object value))
            return (T)value;
        throw new KeyNotFoundException($"Required key '{key}' not found in BuildContext.");
    }

    /// <summary>检查指定 Key 是否已存储</summary>
    public bool Has(string key)
    {
        return _data.ContainsKey(key);
    }
}

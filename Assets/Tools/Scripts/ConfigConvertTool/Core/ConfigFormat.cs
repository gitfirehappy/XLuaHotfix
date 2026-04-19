
/// <summary>
/// 支持的配置文件格式
/// </summary>
public enum ConfigFormat
{
    Csv,
    Lua,
    Json,
    Xml,
    // 可以继续添加其他支持的格式
}

/// <summary>
/// 配置文件类型: 数组或者键值对
/// </summary>
public enum ConfigMode
{
    Array,
    KeyValue,
}
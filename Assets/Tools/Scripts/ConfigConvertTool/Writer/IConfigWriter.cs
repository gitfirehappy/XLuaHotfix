
/// <summary>
/// 配置文件写入接口
/// </summary>
public interface IConfigWriter
{
    ConfigFormat SupportedFormat { get; } // 添加支持的格式属性

    void Write(string outPutPath, ConfigData data, WriterOptions options = null);
}

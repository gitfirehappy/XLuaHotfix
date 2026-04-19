using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ConfigConverter : Singleton<ConfigConverter>
{
    private ConfigConvertSettings _configConvertSettings;

    private Dictionary<ConfigFormat, IConfigReader> _readers = new Dictionary<ConfigFormat, IConfigReader>
    {
        { ConfigFormat.Csv, new CsvReader() },
        { ConfigFormat.Json, new JsonReader() },
        { ConfigFormat.Xml, new XmlReader() },
        // 可以添加其他读取器
    };

    private Dictionary<ConfigFormat, IConfigWriter> _writers = new Dictionary<ConfigFormat, IConfigWriter>
    {
        { ConfigFormat.Lua, new LuaWriter() },
        { ConfigFormat.Json, new JsonWriter() },
        { ConfigFormat.Xml, new XmlWriter() },
        // 可以添加其他写入器
    };

    #region 外部方法

    /// <summary>
    /// 转换单个文件(运行时主要调用)
    /// </summary>
    /// <param name="inputFile">输入文件</param>
    /// <param name="channel">转换通道</param>
    /// <returns></returns>
    public bool ConvertFile(string inputFile, ConfigConvertChannel channel)
    {
        if (!File.Exists(inputFile))
        {
            Debug.LogError($"输入文件不存在: {inputFile}");
            return false;
        }

        if (!ValidateChannel(channel))
        {
            Debug.LogError($"通道配置验证失败: {channel.name}");
            return false;
        }

        try
        {
            // 获取文件名（不含扩展名）
            string fileName = Path.GetFileNameWithoutExtension(inputFile);

            // 构建输出路径
            string outputFilePath = Path.Combine(
                channel.outputFolder,
                $"{fileName}.{channel.outputFormat.ToString().ToLower()}");

            // 确保输出目录存在
            string outputDir = Path.GetDirectoryName(outputFilePath);
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                Debug.Log($"输出目录不存在，已创建: {outputDir}");
            }

            // 读取文件
            if (!_readers.TryGetValue(channel.inputFormat, out IConfigReader reader))
            {
                Debug.LogError($"找不到 {channel.inputFormat} 格式的读取器");
                return false;
            }

            ConfigData data = reader.Read(inputFile);

            // 写入文件
            if (!_writers.TryGetValue(channel.outputFormat, out IConfigWriter writer))
            {
                Debug.LogError($"找不到 {channel.outputFormat} 格式的写入器");
                return false;
            }

            // 使用默认选项写入
            writer.Write(outputFilePath, data);

            Debug.Log($"转换成功: {inputFile} -> {outputFilePath}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"转换文件 {inputFile} 时出错: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 进行配置转换(单个通道)
    /// </summary>
    public void Convert(ConfigConvertChannel channel)
    {
        if (!ValidateChannel(channel)) return;

        // 获取输入目录中的所有匹配文件
        string searchPattern = GetFilePattern(channel.inputFormat);
        string[] inputFiles = Directory.GetFiles(channel.inputFolder, searchPattern);

        if (inputFiles.Length == 0)
        {
            Debug.LogWarning($"在目录 {channel.inputFolder} 中没有找到 {channel.inputFormat} 文件");
            return;
        }

        int successCount = 0;
        foreach (string inputFile in inputFiles)
        {
            if (ConvertFile(inputFile, channel))
            {
                successCount++;
            }
        }

        Debug.Log($"通道: {channel.name} 转换完成: {successCount}/{inputFiles.Length} 个文件成功转换");
    }

    /// <summary>
    /// 进行所有通道配置转换(尽量不要在运行时调用)
    /// </summary>
    public void ConvertAll(ConfigConvertSettings settings)
    {
        if (settings == null || settings.channels == null || settings.channels.Count == 0)
        {
            Debug.LogWarning("没有配置转换通道，请检查ConfigConvertSettings");
            return;
        }

        int totalChannels = settings.channels.Count;
        int completedChannels = 0;

        foreach (var channel in settings.channels)
        {
            Debug.Log($"开始转换通道: {channel.name} ({completedChannels + 1}/{totalChannels})");

            Convert(channel);
            completedChannels++;
        }

        Debug.Log($"所有通道转换完成: {completedChannels}/{totalChannels} 个通道成功处理");
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 验证通道配置是否有效
    /// </summary>
    private bool ValidateChannel(ConfigConvertChannel channel)
    {
        if (string.IsNullOrEmpty(channel.inputFolder) || !Directory.Exists(channel.inputFolder))
        {
            Debug.LogError($"通道 '{channel.name}' 的输入目录不存在: {channel.inputFolder}");
            return false;
        }

        if (string.IsNullOrEmpty(channel.outputFolder))
        {
            Debug.LogError($"通道 '{channel.name}' 的输出目录未设置");
            return false;
        }

        if (!_readers.ContainsKey(channel.inputFormat))
        {
            Debug.LogError($"通道 '{channel.name}' 的输入格式 {channel.inputFormat} 不受支持");
            return false;
        }

        if (!_writers.ContainsKey(channel.outputFormat))
        {
            Debug.LogError($"通道 '{channel.name}' 的输出格式 {channel.outputFormat} 不受支持");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 根据格式获取文件搜索模式
    /// </summary>
    private string GetFilePattern(ConfigFormat format)
    {
        switch (format)
        {
            case ConfigFormat.Csv:
                return "*.csv";
            case ConfigFormat.Lua:
                return "*.lua";
            case ConfigFormat.Json:
                return "*.json";
            case ConfigFormat.Xml:
                return "*.xml";
            default:
                return "*.*";
        }
    }

    #endregion
}
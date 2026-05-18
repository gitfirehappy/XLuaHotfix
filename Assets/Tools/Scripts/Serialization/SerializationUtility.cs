using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// 统一序列化入口：编解码器注册、格式探测、文件读写。
/// </summary>
public static class SerializationUtility
{
    private static readonly Dictionary<string, ISerializationCodec> _codecs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BinaryCodec _binaryCodec = new();

    static SerializationUtility()
    {
        RegisterCodec(new JsonCodec());
        RegisterCodec(_binaryCodec);
        BinarySerializerInitializer.Initialize();
    }

    public static BinaryCodec GetBinaryCodec() => _binaryCodec;

    /// <summary>注册或覆盖编解码器。</summary>
    public static void RegisterCodec(ISerializationCodec codec)
    {
        if (codec == null)
        {
            throw new ArgumentNullException(nameof(codec));
        }

        if (string.IsNullOrEmpty(codec.CodecId))
        {
            throw new ArgumentException("CodecId 不能为空", nameof(codec));
        }

        _codecs[codec.CodecId] = codec;
    }

    /// <summary>自动探测格式（当前：匹配 BinaryHeader 为 binary，否则 json）。</summary>
    public static string DetectFormat(byte[] data)
    {
        return BinaryHeader.HasValidMagic(data) ? "binary" : JsonCodec.JsonCodecId;
    }

    /// <summary>按指定 codec 序列化对象。</summary>
    public static byte[] Serialize<T>(T obj, string codecId = JsonCodec.JsonCodecId, bool prettyPrint = false)
    {
        var codec = GetCodec(codecId);
        return codec.Serialize(obj, prettyPrint);
    }

    /// <summary>按指定 codec 反序列化对象。</summary>
    public static T Deserialize<T>(byte[] data, string codecId)
    {
        var codec = GetCodec(codecId);
        return codec.Deserialize<T>(data);
    }

    /// <summary>自动探测格式后反序列化对象。</summary>
    public static T Deserialize<T>(byte[] data)
    {
        string codecId = DetectFormat(data);
        var codec = GetCodec(codecId);
        return codec.Deserialize<T>(data);
    }

    /// <summary>从 JSON 文本反序列化对象。</summary>
    public static T DeserializeJson<T>(string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json ?? string.Empty);
        var codec = GetCodec(JsonCodec.JsonCodecId);
        return codec.Deserialize<T>(data);
    }

    /// <summary>将对象序列化为 JSON 文本。</summary>
    public static string SerializeToJson<T>(T obj, bool prettyPrint = false)
    {
        var codec = GetCodec(JsonCodec.JsonCodecId);
        byte[] data = codec.Serialize(obj, prettyPrint);
        return Encoding.UTF8.GetString(data);
    }

    /// <summary>写文件（默认 JSON）。</summary>
    public static void WriteToFile<T>(string path, T obj, string codecId = JsonCodec.JsonCodecId, bool prettyPrint = true)
    {
        byte[] data = Serialize(obj, codecId, prettyPrint);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, data);
    }

    /// <summary>异步写文件（默认 JSON）。</summary>
    public static async Task WriteToFileAsync<T>(string path, T obj, string codecId = JsonCodec.JsonCodecId, bool prettyPrint = true)
    {
        byte[] data = Serialize(obj, codecId, prettyPrint);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, data);
    }

    /// <summary>读文件并自动探测反序列化。</summary>
    public static T ReadFromFile<T>(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return Deserialize<T>(data);
    }

    /// <summary>异步读文件并自动探测反序列化。</summary>
    public static async Task<T> ReadFromFileAsync<T>(string path)
    {
        byte[] data = await File.ReadAllBytesAsync(path);
        return Deserialize<T>(data);
    }

    private static ISerializationCodec GetCodec(string codecId)
    {
        if (string.IsNullOrEmpty(codecId))
        {
            throw new ArgumentException("codecId 不能为空", nameof(codecId));
        }

        if (_codecs.TryGetValue(codecId, out var codec))
        {
            return codec;
        }

        throw new InvalidOperationException($"未注册的序列化编解码器: {codecId}");
    }
}

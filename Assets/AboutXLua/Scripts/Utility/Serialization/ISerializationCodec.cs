using System;

/// <summary>
/// 序列化编解码器接口（仅负责 byte[] 与对象转换，不负责文件 I/O）。
/// </summary>
public interface ISerializationCodec
{
    /// <summary>编解码器唯一标识（如 json / binary）。</summary>
    string CodecId { get; }

    /// <summary>将对象序列化为字节数组。</summary>
    byte[] Serialize<T>(T obj, bool prettyPrint = false);

    /// <summary>将字节数组反序列化为对象。</summary>
    T Deserialize<T>(byte[] data);
}

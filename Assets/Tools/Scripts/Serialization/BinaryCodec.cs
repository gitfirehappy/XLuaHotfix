using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 二进制编解码器：提供基础注册与头部路由能力。
/// </summary>
public sealed class BinaryCodec : ISerializationCodec
{
    public const string BinaryCodecId = "binary";

    public string CodecId => BinaryCodecId;

    private readonly Dictionary<Type, BinaryTypeHandler> _typeHandlers = new();
    private readonly Dictionary<uint, BinaryTypeHandler> _magicHandlers = new();

    /// <summary>
    /// 封装注册二进制类型处理程序，使用默认模式版本。
    /// </summary>
    /// <param name="magic">二进制魔数，用于标识类型。</param>
    /// <param name="writeWithHeader">写入二进制数据的委托，包含头部信息。</param>
    /// <param name="readWithHeader">读取二进制数据的委托，包含头部信息。</param>
    public void Register<T>(uint magic, Action<BinaryWriter, T> writeWithHeader, Func<BinaryReader, T> readWithHeader)
    {
        Register(magic, schemaVersion: 1, writeWithHeader, readWithHeader);
    }

    /// <summary>
    /// 注册二进制类型处理程序。
    /// </summary>
    /// <param name="magic">二进制魔数，用于标识类型。</param>
    /// <param name="schemaVersion">二进制模式版本，用于兼容性检查。</param>
    /// <param name="writeWithHeader">写入二进制数据的委托，包含头部信息。</param>
    /// <param name="readWithHeader">读取二进制数据的委托，包含头部信息。</param>
    public void Register<T>(uint magic, ushort schemaVersion, Action<BinaryWriter, T> writeWithHeader, Func<BinaryReader, T> readWithHeader)
    {
        if (writeWithHeader == null)
        {
            throw new ArgumentNullException(nameof(writeWithHeader));
        }

        if (readWithHeader == null)
        {
            throw new ArgumentNullException(nameof(readWithHeader));
        }

        BinaryHeader.RegisterMagic(magic);

        var handler = new BinaryTypeHandler(
            magic,
            schemaVersion,
            t =>
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writeWithHeader(writer, (T)t);
                return ms.ToArray();
            },
            data =>
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                var header = BinaryHeader.ReadHeader(reader);
                if (header.Magic != magic)
                {
                    throw new InvalidDataException($"Magic 不匹配，期望: {magic}, 实际: {header.Magic}");
                }

                if (header.SchemaVersion > schemaVersion)
                {
                    throw new InvalidDataException($"SchemaVersion 不兼容，当前: {schemaVersion}, 文件: {header.SchemaVersion}");
                }

                ms.Position = 0;
                using var reader2 = new BinaryReader(ms);
                return readWithHeader(reader2);
            });

        _typeHandlers[typeof(T)] = handler;
        _magicHandlers[magic] = handler;
    }

    /// <summary>
    /// 序列化对象为二进制数据。
    /// </summary>
    /// <param name="obj">要序列化的对象。</param>
    /// <param name="prettyPrint">是否以格式化打印方式序列化。</param>
    /// <returns>序列化后的二进制数据。</returns>
    public byte[] Serialize<T>(T obj, bool prettyPrint = false)
    {
        if (obj == null)
        {
            return Array.Empty<byte>();
        }

        var type = typeof(T);
        if (!_typeHandlers.TryGetValue(type, out var handler))
        {
            throw new InvalidOperationException($"未注册二进制类型: {type.FullName}");
        }

        return handler.Serialize(obj);
    }

    /// <summary>
    /// 反序列化二进制数据为对象。
    /// </summary>
    /// <param name="data">要反序列化的二进制数据。</param>
    /// <returns>反序列化后的对象。</returns>
    public T Deserialize<T>(byte[] data)
    {
        if (!BinaryHeader.TryReadHeader(data, out var header))
        {
            return default;
        }

        if (!_magicHandlers.TryGetValue(header.Magic, out var handler))
        {
            throw new InvalidOperationException($"未注册的二进制 Magic: {header.Magic}");
        }

        var obj = handler.Deserialize(data);
        if (obj is T typed)
        {
            return typed;
        }

        throw new InvalidCastException($"反序列化类型不匹配，期望: {typeof(T).FullName}, 实际: {obj?.GetType().FullName}");
    }

    /// <summary>
    /// 二进制类型处理程序：封装序列化与反序列化逻辑。
    /// </summary>
    private sealed class BinaryTypeHandler
    {
        public uint Magic { get; }
        public ushort SchemaVersion { get; }
        private readonly Func<object, byte[]> _serializer;
        private readonly Func<byte[], object> _deserializer;

        public BinaryTypeHandler(uint magic, ushort schemaVersion, Func<object, byte[]> serializer, Func<byte[], object> deserializer)
        {
            Magic = magic;
            SchemaVersion = schemaVersion;
            _serializer = serializer;
            _deserializer = deserializer;
        }

        public byte[] Serialize(object obj) => _serializer(obj);

        public object Deserialize(byte[] data) => _deserializer(data);
    }
}

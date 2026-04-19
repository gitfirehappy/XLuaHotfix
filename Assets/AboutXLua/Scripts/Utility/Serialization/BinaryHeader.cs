using System;
using System.Collections.Generic;
using System.IO;

public readonly struct BinaryHeaderInfo
{
    public readonly uint Magic;
    public readonly ushort SchemaVersion;
    public readonly ushort Flags;

    public BinaryHeaderInfo(uint magic, ushort schemaVersion, ushort flags)
    {
        Magic = magic;
        SchemaVersion = schemaVersion;
        Flags = flags;
    }
}

/// <summary>
/// 二进制头部格式探测工具。
/// S1 阶段仅提供基础设施，不注册任何 Magic。
/// </summary>
public static class BinaryHeader
{
    public const int HeaderSize = 8;

    private static readonly HashSet<uint> _registeredMagics = new HashSet<uint>();

    /// <summary>注册二进制 Magic（S2/S3 使用）。</summary>
    public static void RegisterMagic(uint magic)
    {
        if (magic == 0)
        {
            throw new ArgumentException("magic 不能为 0", nameof(magic));
        }

        _registeredMagics.Add(magic);
    }

    /// <summary>写入二进制头部（Magic + SchemaVersion + Flags）。</summary>
    public static void WriteHeader(BinaryWriter writer, uint magic, ushort schemaVersion, ushort flags = 0)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.Write(magic);
        writer.Write(schemaVersion);
        writer.Write(flags);
    }

    /// <summary>读取二进制头部（不做 Magic 合法性校验）。</summary>
    public static BinaryHeaderInfo ReadHeader(BinaryReader reader)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        uint magic = reader.ReadUInt32();
        ushort schemaVersion = reader.ReadUInt16();
        ushort flags = reader.ReadUInt16();
        return new BinaryHeaderInfo(magic, schemaVersion, flags);
    }

    /// <summary>从字节数组尝试读取头部。</summary>
    public static bool TryReadHeader(byte[] data, out BinaryHeaderInfo header)
    {
        if (data == null || data.Length < HeaderSize)
        {
            header = default;
            return false;
        }

        uint magic = BitConverter.ToUInt32(data, 0);
        ushort schemaVersion = BitConverter.ToUInt16(data, 4);
        ushort flags = BitConverter.ToUInt16(data, 6);
        header = new BinaryHeaderInfo(magic, schemaVersion, flags);
        return true;
    }

    /// <summary>判断字节数据前4字节是否匹配已注册 Magic。</summary>
    public static bool HasValidMagic(byte[] data)
    {
        if (!TryReadHeader(data, out var header))
        {
            return false;
        }

        return _registeredMagics.Contains(header.Magic);
    }
}

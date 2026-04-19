using System;
using System.Collections.Generic;

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
        _registeredMagics.Add(magic);
    }

    /// <summary>判断字节数据前4字节是否匹配已注册 Magic。</summary>
    public static bool HasValidMagic(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            return false;
        }

        uint magic = BitConverter.ToUInt32(data, 0);
        return _registeredMagics.Contains(magic);
    }
}

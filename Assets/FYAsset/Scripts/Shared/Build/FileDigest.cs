using System;
using System.IO;

/// <summary>
/// 单个物理文件的摘要：集合内的名称、内容 Hash、CRC32 与字节数。
/// 构建缓存、发布缓存、构建摘要与无状态 Diff 共用该类型。
/// </summary>
/// <remarks>
/// 只依赖 System 与 HashGenerator（同为纯 .NET），因此构建缓存等纯逻辑可以直接引用，无需 UnityEngine/UnityEditor。
/// 声明为 readonly struct：摘要产生后不再变化，避免下游修改后与磁盘事实不符。
/// 字段不含绝对路径与时间戳，可跨 attempt 目录、跨构建比较。
/// </remarks>
public readonly struct FileDigest
{
    /// <summary>文件在所属集合内的名称，不含目录</summary>
    public readonly string Name;

    /// <summary>内容 MD5，小写十六进制</summary>
    public readonly string Hash;

    /// <summary>内容 CRC32</summary>
    public readonly uint CRC;

    /// <summary>字节数</summary>
    public readonly long Size;

    public FileDigest(string name, string hash, uint crc, long size)
    {
        Name = name;
        Hash = hash;
        CRC = crc;
        Size = size;
    }

    /// <summary>四个字段是否都可用于比较；任一缺失即视为无效摘要。</summary>
    public bool IsComplete =>
        !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Hash) && Size >= 0;

    /// <summary>四个字段全部一致才算同一份物理文件。</summary>
    public bool Matches(in FileDigest other)
    {
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
               && string.Equals(Hash, other.Hash, StringComparison.Ordinal)
               && CRC == other.CRC
               && Size == other.Size;
    }

    /// <summary>
    /// 一次流式遍历读取文件并计算摘要；文件缺失、被截断或不可读时返回 false，不抛异常。
    /// </summary>
    /// <param name="name">摘要中的文件名称；为空时使用磁盘文件名。</param>
    public static bool TryCreate(string filePath, string name, out FileDigest digest)
    {
        digest = default;
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return false;

            long size = info.Length;
            HashGenerator.ComputeFileHashAndCRC(filePath, out string hash, out uint crc);
            digest = new FileDigest(string.IsNullOrEmpty(name) ? info.Name : name, hash, crc, size);
            return true;
        }
        catch (Exception)
        {
            // IO、权限、文件被并发删除等异常一律表达为“摘要不可用”，由调用方退化处理。
            digest = default;
            return false;
        }
    }
}

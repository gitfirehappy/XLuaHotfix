using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public enum HashAlgorithmType
{
    MD5,
    CRC32
}

/// <summary>
/// 统一 Hash 生成工具（MD5 / CRC32 / 后续可扩展 SHA256 等）。
/// 所有文件 hash 计算走此入口。
/// </summary>
public static class HashGenerator
{
    #region CRC32 Table

    private static readonly uint[] CrcTable = new uint[256];
    private static bool _crcTableReady;

    private static void EnsureCrcTable()
    {
        if (_crcTableReady) return;
        const uint poly = 0xEDB88320u;
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ poly;
                else
                    crc >>= 1;
            }
            CrcTable[i] = crc;
        }
        _crcTableReady = true;
    }

    #endregion

    #region Shortcut Methods

    /// <summary>文件 MD5 Hash（hex 字符串）</summary>
    public static string GenerateFileHash(string filePath)
        => ComputeFileHash(filePath, HashAlgorithmType.MD5);

    /// <summary>文件 CRC32 校验码</summary>
    public static uint GenerateFileCRC(string filePath)
    {
        EnsureCrcTable();
        byte[] data = File.ReadAllBytes(filePath);
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < data.Length; i++)
            crc = (crc >> 8) ^ CrcTable[(crc ^ data[i]) & 0xFF];
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>字符串 MD5 Hash（hex 字符串）</summary>
    public static string GenerateStringHash(string content)
    {
        using (var md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>目录内容组合 MD5 Hash</summary>
    public static string GenerateDirectoryHash(string directoryPath)
    {
        var hashList = new StringBuilder();
        foreach (var filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            hashList.Append(GenerateFileHash(filePath));

        using (var finalMd5 = MD5.Create())
        {
            return BitConverter.ToString(finalMd5.ComputeHash(Encoding.UTF8.GetBytes(hashList.ToString())))
                .Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>包含所有依赖项的深度 MD5 Hash</summary>
#if UNITY_EDITOR
    public static string GenerateDeepHash(string assetPath)
    {
        if (!File.Exists(assetPath)) return string.Empty;

        string[] dependencies = UnityEditor.AssetDatabase.GetDependencies(assetPath, true);
        Array.Sort(dependencies);

        var sb = new StringBuilder();
        foreach (var depPath in dependencies)
        {
            if (depPath.EndsWith(".cs")) continue;
            if (!File.Exists(depPath)) continue;
            sb.Append(GenerateFileHash(depPath));
        }

        return GenerateStringHash(sb.ToString());
    }
#endif

    #endregion

    #region Generic Methods (enum 选择)

    /// <summary>按算法枚举计算文件 Hash（返回 hex 字符串或原始 hex）</summary>
    public static string ComputeFileHash(string filePath, HashAlgorithmType algorithm)
    {
        switch (algorithm)
        {
            case HashAlgorithmType.MD5:
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }

            case HashAlgorithmType.CRC32:
                return GenerateFileCRC(filePath).ToString("X8");

            default:
                throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
        }
    }

    #endregion
}

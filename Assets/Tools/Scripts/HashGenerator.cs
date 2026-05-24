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

    /// <summary>按给定顺序组合多个文件内容并计算 MD5。缺失文件以路径标记参与计算，保持结果确定。</summary>
    public static string GenerateCompositeFileHash(params string[] filePaths)
    {
        using (var md5 = MD5.Create())
        {
            if (filePaths != null)
            {
                for (int i = 0; i < filePaths.Length; i++)
                {
                    AppendCompositeFile(md5, filePaths[i]);
                }
            }

            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>按给定顺序组合多个文件内容并计算 CRC32。缺失文件以路径标记参与计算，保持结果确定。</summary>
    public static uint GenerateCompositeFileCRC(params string[] filePaths)
    {
        EnsureCrcTable();
        uint crc = 0xFFFFFFFFu;
        if (filePaths != null)
        {
            for (int i = 0; i < filePaths.Length; i++)
            {
                UpdateCompositeFileCRC(filePaths[i], ref crc);
            }
        }
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

    #region Composite Helpers

    private static void AppendCompositeFile(HashAlgorithm algorithm, string filePath)
    {
        // 文件路径作为分隔标记参与 Hash，避免 A+B 与 AB 这类内容拼接歧义。
        byte[] marker = Encoding.UTF8.GetBytes(filePath ?? string.Empty);
        algorithm.TransformBlock(marker, 0, marker.Length, null, 0);

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            byte[] missing = Encoding.UTF8.GetBytes("<missing>");
            algorithm.TransformBlock(missing, 0, missing.Length, null, 0);
            return;
        }

        using (var stream = File.OpenRead(filePath))
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                algorithm.TransformBlock(buffer, 0, read, null, 0);
            }
        }
    }

    private static void UpdateCompositeFileCRC(string filePath, ref uint crc)
    {
        // CRC 与 MD5 使用同一份组合语义：路径标记 + 文件内容，缺失文件也稳定参与计算。
        byte[] marker = Encoding.UTF8.GetBytes(filePath ?? string.Empty);
        UpdateCRC(marker, marker.Length, ref crc);

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            byte[] missing = Encoding.UTF8.GetBytes("<missing>");
            UpdateCRC(missing, missing.Length, ref crc);
            return;
        }

        using (var stream = File.OpenRead(filePath))
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                UpdateCRC(buffer, read, ref crc);
            }
        }
    }

    private static void UpdateCRC(byte[] data, int length, ref uint crc)
    {
        for (int i = 0; i < length; i++)
            crc = (crc >> 8) ^ CrcTable[(crc ^ data[i]) & 0xFF];
    }

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

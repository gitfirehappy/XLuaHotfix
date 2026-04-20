using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

/// <summary>
/// MD5 哈希生成工具。
/// </summary>
public static class HashGenerator
{
    /// <summary>
    /// 生成字符串的 MD5 哈希。
    /// </summary>
    public static string GenerateStringHash(string content)
    {
        using (var md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// 生成单个文件的 MD5 哈希。
    /// </summary>
    public static string GenerateFileHash(string filePath)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(filePath))
        {
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// 生成目录内容的组合 MD5。
    /// </summary>
    /// <param name="directoryPath">要计算内容哈希的目录路径。</param>
    public static string GenerateDirectoryHash(string directoryPath)
    {
        var hashList = new StringBuilder();

        foreach (var filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            hashList.Append(GenerateFileHash(filePath));
        }

        using (var finalMd5 = MD5.Create())
        {
            return BitConverter.ToString(finalMd5.ComputeHash(Encoding.UTF8.GetBytes(hashList.ToString())))
                .Replace("-", "")
                .ToLowerInvariant();
        }
    }

    /// <summary>
    /// 生成包含所有依赖项的深度 Hash。
    /// 适用于 ScriptableObject 或 Prefab，只要依赖资源发生变化，结果就会变化。
    /// </summary>
    /// <param name="assetPath">主资源路径。</param>
    /// <returns>组合后的 Hash。</returns>
    public static string GenerateDeepHash(string assetPath)
    {
        if (!File.Exists(assetPath)) return string.Empty;

        string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
        Array.Sort(dependencies);

        StringBuilder sb = new StringBuilder();

        foreach (var depPath in dependencies)
        {
            // 通常热更只关注资源内容变化，默认跳过脚本文件。
            if (depPath.EndsWith(".cs")) continue;

            if (!File.Exists(depPath)) continue;

            string fileHash = GenerateFileHash(depPath);
            sb.Append(fileHash);
        }

        return GenerateStringHash(sb.ToString());
    }
}

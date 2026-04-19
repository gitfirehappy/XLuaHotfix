using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

/// <summary>
/// MD5哈希生成器
/// </summary>
public static class HashGenerator
{
    /// <summary>
    /// 生成字符串的MD5哈希
    /// </summary>
    public static string GenerateStringHash(string content)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// 生成单个文件的MD5哈希
    /// </summary>
    public static string GenerateFileHash(string filePath)
    {
        using (var md5 = MD5.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
    
    /// <summary>
    /// 生成热更新包的MD5
    /// </summary>
    /// <param name="hotfixDir">热更包的路径</param>
    public static string GeneratePackageHash(string hotfixDir)
    {
        var hashList = new StringBuilder();
        
        // 计算整个热更新包目录的MD5（跳过version_state.json自身）
        foreach (var file in Directory.GetFiles(hotfixDir, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "version_state.json") continue;
            
            hashList.Append(GenerateFileHash(file));
        }
        
        // 对所有文件hash拼接后计算最终MD5
        using (var finalMd5 = MD5.Create())
        {
            return BitConverter.ToString(finalMd5.ComputeHash(Encoding.UTF8.GetBytes(hashList.ToString())))
                .Replace("-", "")
                .ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// 生成包含所有依赖项的深度 Hash
    /// (适用于 ScriptableObject 或 Prefab，只要引用变了，Hash 就会变)
    /// </summary>
    /// <param name="assetPath">主资源路径</param>
    /// <returns>组合后的 Hash</returns>
    public static string GenerateDeepHash(string assetPath)
    {
        if (!File.Exists(assetPath)) return string.Empty;

        // 1. 获取所有依赖项 (recursive = true 表示递归查找，比如 SO -> Mat -> Texture)
        string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
        
        // 2. 极其重要：排序！
        // Unity 返回的顺序可能不固定，为了保证 Hash 一致性，必须按路径排序
        Array.Sort(dependencies);

        StringBuilder sb = new StringBuilder();

        foreach (var depPath in dependencies)
        {
            // 排除 .cs 脚本文件 (可选)
            // 通常热更仅更新资源。如果 C# 代码变了，通常意味着需要发整包或者 DLL 热更
            // 且 .cs 文件的 meta 变动频繁，建议跳过 .cs，除非你的 SO 结构依赖脚本内容的特定版本
            if (depPath.EndsWith(".cs")) continue;
            
            // 跳过不存在的文件
            if (!File.Exists(depPath)) continue;

            // 3. 计算每个依赖文件的 Hash 并拼接到 StringBuilder
            string fileHash = GenerateFileHash(depPath);
            sb.Append(fileHash);
        }

        // 4. 将巨大的组合字符串再次 Hash，得到最终结果
        return GenerateStringHash(sb.ToString());
    }
}
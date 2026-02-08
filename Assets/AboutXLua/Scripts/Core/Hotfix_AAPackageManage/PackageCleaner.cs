using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class PackageCleaner
{
    /// <summary>
    /// 应用更新：删除旧文件，移动新文件
    /// </summary>
    public static void ApplyUpdate(List<string> filesToDelete, string tempDownloadRoot, string finalRoot)
    {
        // 删除 Local 中不再需要的旧 Bundle
        string localBundleRoot = Path.Combine(finalRoot, "bundles");
        if (Directory.Exists(localBundleRoot) && filesToDelete != null && filesToDelete.Count > 0)
        {
            foreach (var prefix in filesToDelete)
            {
                if(string.IsNullOrEmpty(prefix)) continue;

                try
                {

                    string[] matchFiles = Directory.GetFiles(localBundleRoot, $"{prefix}*.bundle");
                    if (matchFiles.Length == 0)
                    {
                        // 如果是第一次热更，原始资源位于 StreamingAssets 直接跳过
                        Debug.Log($"[PackageCleaner] 待删除文件在缓存中未找到 (可能是整包资源): {prefix}");
                    }
                    else
                    {
                        foreach (string filePath in matchFiles)
                        {
                            try
                            {
                                File.Delete(filePath);
                                Debug.Log(
                                    $"[PackageCleaner] 删除过期 Bundle: {Path.GetFileName(filePath)} (匹配规则: {prefix})");
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning($"删除失败: {filePath}\n{e}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PackageCleaner] 搜索待删除文件出错 (Prefix: {prefix}): {e.Message}");
                }
            }
        }

        // 将 Remote (Temp) 中的文件移动到 Local
        // 包括 bundles 文件夹和 catalog/version 文件
        MoveDirectory(tempDownloadRoot, finalRoot);
        
        Debug.Log("[PackageCleaner] 热更文件覆盖完成。");
    }

    /// <summary>
    /// 递归移动文件夹内容
    /// </summary>
    private static void MoveDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

        // 移动文件
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            
            if (File.Exists(destFile)) File.Delete(destFile); // 覆盖旧的
            File.Move(file, destFile);
        }

        // 递归移动子目录 (主要是 bundles)
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            string destSubDir = Path.Combine(destDir, dirName);
            MoveDirectory(dir, destSubDir);
        }

        // 移完后删除空的源目录
        Directory.Delete(sourceDir, true);
    }
    
    /// <summary>
    /// 大版本清理：清空所有热更内容
    /// </summary>
    public static void ClearAllHotfix()
    {
        // 路径已根据BuildIndex锁定
        if (Directory.Exists(PathManager.HotfixRoot))
            Directory.Delete(PathManager.HotfixRoot, true);
        PathManager.EnsureDirectories();
    }
    
    /// <summary>
    /// 清理非当前定位包体的所有 Build_xxxx 目录
    /// 避免旧包体逐渐积累占用用户空间
    /// </summary>
    /// <param name="maxKeepCount">最多保留的包体数量（包括当前包），默认为1只保留当前包</param>
    public static void CleanOldBuildPackages(int maxKeepCount = 1)
    {
        if (!Directory.Exists(PathManager.EnvRoot))
        {
            Debug.Log("[PackageCleaner] EnvRoot 不存在，无需清理旧包体");
            return;
        }

        try
        {
            // 获取当前定位的包体目录名称
            string currentBuildDir = Path.GetFileName(PathManager.CurrentGUIDRoot);
            
            // 获取所有 Build_xxxx 目录
            var allBuildDirs = Directory.GetDirectories(PathManager.EnvRoot, "Build_*")
                .Select(path => new DirectoryInfo(path))
                .ToList();

            if (allBuildDirs.Count <= maxKeepCount)
            {
                Debug.Log($"[PackageCleaner] 当前包体数量 ({allBuildDirs.Count}) 未超过限制 ({maxKeepCount})，无需清理");
                return;
            }

            // 按最后修改时间排序，保留最新的 maxKeepCount 个
            var sortedDirs = allBuildDirs.OrderByDescending(d => d.LastWriteTime).ToList();
            
            int cleanedCount = 0;
            long freedSpace = 0;

            for (int i = maxKeepCount; i < sortedDirs.Count; i++)
            {
                var dirInfo = sortedDirs[i];
                
                // 跳过当前定位的包体（双重保险）
                if (dirInfo.Name == currentBuildDir)
                {
                    Debug.Log($"[PackageCleaner] 跳过当前定位包体: {dirInfo.Name}");
                    continue;
                }

                try
                {
                    // 计算目录大小（用于日志）
                    long dirSize = GetDirectorySize(dirInfo.FullName);
                    
                    Directory.Delete(dirInfo.FullName, true);
                    
                    cleanedCount++;
                    freedSpace += dirSize;
                    
                    Debug.Log($"[PackageCleaner] 已删除旧包体: {dirInfo.Name}, 释放空间: {FormatBytes(dirSize)}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageCleaner] 删除旧包体失败: {dirInfo.Name}\n{ex.Message}");
                }
            }

            if (cleanedCount > 0)
            {
                Debug.Log($"[PackageCleaner] 旧包体清理完成，共删除 {cleanedCount} 个包体，释放空间: {FormatBytes(freedSpace)}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PackageCleaner] 清理旧包体时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 计算目录总大小
    /// </summary>
    private static long GetDirectorySize(string dirPath)
    {
        if (!Directory.Exists(dirPath)) return 0;

        long size = 0;
        try
        {
            DirectoryInfo dirInfo = new DirectoryInfo(dirPath);
            
            // 累加所有文件大小
            foreach (FileInfo file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }
        }
        catch
        {
            // 如果遇到权限问题等，返回已计算的部分
        }
        
        return size;
    }

    /// <summary>
    /// 格式化字节数为可读的字符串
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}
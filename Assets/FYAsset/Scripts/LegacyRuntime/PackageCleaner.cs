using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class PackageCleaner
{
    /// <summary>
    /// 大版本清理：清空所有热更内容
    /// </summary>
    public static void ClearAllHotfix()
    {
        if (FileHelper.DirectoryExists(PathManager.HotfixRoot))
        {
            FileHelper.TryDeleteDirectory(PathManager.HotfixRoot, true);
            Debug.Log($"[PackageCleaner] 已清空热更根目录: {PathManager.HotfixRoot}");
        }
        PathManager.EnsureDirectories();
    }
    
    /// <summary>
    /// 清理非当前定位包体的所有 Build_xxxx 目录
    /// 避免旧包体逐渐积累占用用户空间
    /// </summary>
    /// <param name="maxKeepCount">保留最近的包体数量（包括当前包），建议设为2以便进行差异比对</param>
    public static void CleanOldBuildPackages(int maxKeepCount = 2)
    {
        if (!FileHelper.DirectoryExists(PathManager.HotfixRoot))
        {
            Debug.Log("[PackageCleaner] HotfixRoot 不存在，无需清理旧包体");
            return;
        }

        try
        {
            // 获取当前定位的包体目录名称 (e.g. Build_xxxx)
            // PathManager.CurrentGUIDRoot format: .../Hotfix/Build_xxxx
            string currentBuildDirName = new DirectoryInfo(PathManager.CurrentGUIDRoot).Name;
            
            // 获取所有 Build_xxxx 目录
            var allBuildDirs = FileHelper.GetDirectories(PathManager.HotfixRoot, "Build_*")
                .Select(path => new DirectoryInfo(path))
                .ToList();

            if (allBuildDirs.Count <= maxKeepCount)
            {
                Debug.Log($"[PackageCleaner] 当前包体数量 ({allBuildDirs.Count}) 未超过限制 ({maxKeepCount})，无需清理");
                return;
            }

            // 按最后修改时间倒序排列 (最新的在前)
            var sortedDirs = allBuildDirs.OrderByDescending(d => d.LastWriteTime).ToList();
            
            int cleanedCount = 0;
            long freedSpace = 0;

            // 从第 maxKeepCount 个开始删除
            // 保留最新的 maxKeepCount 个
            for (int i = maxKeepCount; i < sortedDirs.Count; i++)
            {
                var dirInfo = sortedDirs[i];
                
                // 绝对禁止删除当前正在使用的热更包
                if (dirInfo.Name == currentBuildDirName)
                {
                    Debug.LogWarning($"[PackageCleaner] 试图删除当前包体 (逻辑错误?): {dirInfo.Name}，已跳过。");
                    continue;
                }

                try
                {
                    long dirSize = GetDirectorySize(dirInfo.FullName);
                    FileHelper.TryDeleteDirectory(dirInfo.FullName, true);
                    
                    cleanedCount++;
                    freedSpace += dirSize;
                    
                    Debug.Log($"[PackageCleaner] 已删除过期包体: {dirInfo.Name}, 释放空间: {FormatBytes(dirSize)}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageCleaner] 删除失败: {dirInfo.Name}\n{ex.Message}");
                }
            }

            if (cleanedCount > 0)
            {
                Debug.Log($"[PackageCleaner] 旧包体清理完成，共删除 {cleanedCount} 个，释放空间: {FormatBytes(freedSpace)}");
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
        if (!FileHelper.DirectoryExists(dirPath)) return 0;

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
        catch (Exception ex)
        {
            Debug.LogWarning($"[PackageCleaner] 统计目录大小失败: {dirPath}, 已返回部分结果。原因: {ex.Message}");
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

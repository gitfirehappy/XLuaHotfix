using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 用户端路径管理
/// </summary>
public static class PathManager
{
    public static readonly string PersistentRoot = Path.Combine(Application.persistentDataPath, Constants.PROJECTNAME);
    
    // 运行时动态决定的路径
    public static string EnvRoot { get; private set; }    // .../[Platform]/[Debug]
    public static string CurrentGUIDRoot { get; private set; } // .../[Platform]/[Debug]/Hotfix/[GUID]
    public static string HotfixRoot { get; private set; } // .../[Platform]/[Debug]/Hotfix
   
    public static string CacheRoot { get; private set; }
    public static string SaveRoot { get; private set; }
    public static string LogRoot { get; private set; }

    /// <summary>
    /// 初始化路径
    /// </summary>
    public static void Initialize(BuildIndexData buildIndex)
    {
        string platform = buildIndex.Platform;
        if(string.IsNullOrEmpty(platform)) platform = "Unknown";

        string envDir = buildIndex.IsDebug ? "Debug" : "Release";
        string guidDir = buildIndex.BuildGUID; // 现在 GUID 目录直接位于 Hotfix 下，名字由 manifest/buildIndex 决定，通常是 Build_xxxx

        // 组装路径结构
        // .../ProjectName/[Platform]/Release
        EnvRoot = Path.Combine(PersistentRoot, platform, envDir); 
        
        // .../ProjectName/[Platform]/Release/Hotfix
        HotfixRoot = Path.Combine(EnvRoot, "Hotfix");
        
        // .../ProjectName/[Platform]/Release/Hotfix/Build_abc-123-guid (当前生效目录)
        // 注意：这里假设 buildIndex.BuildGUID 已经是完整的目录名 (如 Build_2023...) 或者只是 GUID 部分
        // 根据之前的逻辑，BuildProjectManager 生成的是 Build_YYYYMMDD_Ver
        // 这里的 guidDir 需要与热更流程中记录的一致
        if (!guidDir.StartsWith("Build_")) guidDir = "Build_" + guidDir;
        CurrentGUIDRoot = Path.Combine(HotfixRoot, guidDir);
        
        // Save, Logs, Cache 提升到 EnvRoot 下
        CacheRoot = Path.Combine(EnvRoot, "Cache");
        SaveRoot = Path.Combine(EnvRoot, "Saves");
        LogRoot = Path.Combine(EnvRoot, "Logs");
        
        Debug.Log($"[PathManager] 路径已锁定至 GUID: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    /// <summary>
    /// 切换当前活动的 Build 目录（热更下载完成后调用）
    /// </summary>
    /// <param name="newBuildName">新的 Build 目录名，例如 "Build_20260209_2.0.0"</param>
    public static void SwitchToNewBuild(string newBuildName)
    {
        if (string.IsNullOrEmpty(newBuildName))
        {
            Debug.LogError("[PathManager] SwitchToNewBuild: newBuildName 不能为空");
            return;
        }
        
        string guidDir = newBuildName;
        if (!guidDir.StartsWith("Build_")) guidDir = "Build_" + guidDir;
        
        CurrentGUIDRoot = Path.Combine(HotfixRoot, guidDir);
        Debug.Log($"[PathManager] 已切换至新 Build: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(PersistentRoot);
        Directory.CreateDirectory(HotfixRoot);
        Directory.CreateDirectory(CurrentGUIDRoot); 
        // Bundles 目录由下载逻辑创建，或者是 Build 流程生成
        
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(SaveRoot);
        Directory.CreateDirectory(LogRoot);
    }
}

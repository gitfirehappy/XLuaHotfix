using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 用户端路径管理
/// </summary>
public static class PathManager
{
    public static readonly string PersistentRoot = Path.Combine(Application.persistentDataPath, "ProjectName");
    
    // 运行时动态决定的路径
    public static string EnvRoot { get; private set; }    // .../[Platform]/[Debug]
    public static string CurrentGUIDRoot { get; private set; } // .../[Platform]/[Debug]/[GUID]
    public static string HotfixRoot { get; private set; } // .../[Platform]/[Debug]/[GUID]/Hotfix
   
    public static string LocalRoot { get; private set; }
    public static string TempRoot { get; private set; }
    
    public static string LocalBundleRoot { get; private set; }
    public static string TempBundleRoot { get; private set; }
    
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
        string guidDir = "Build_" + buildIndex.BuildGUID;

        // 组装路径结构
        // .../ProjectName/[Platform]/Release
        EnvRoot = Path.Combine(PersistentRoot, platform, envDir); 
        
        // .../ProjectName/[Platform]/Release/abc-123-guid (当前生效目录)
        CurrentGUIDRoot = Path.Combine(EnvRoot, guidDir);
        
        HotfixRoot = Path.Combine(CurrentGUIDRoot, "Hotfix");
        
        // 定位 Local 和 Temp
        LocalRoot = Path.Combine(HotfixRoot, "Local");
        TempRoot = Path.Combine(HotfixRoot, "Temp");
        
        LocalBundleRoot = Path.Combine(LocalRoot, "bundles");
        TempBundleRoot = Path.Combine(TempRoot, "bundles");
        
        CacheRoot = Path.Combine(CurrentGUIDRoot, "Cache");
        SaveRoot = Path.Combine(CurrentGUIDRoot, "Saves");
        LogRoot = Path.Combine(CurrentGUIDRoot, "Logs");
        
        Debug.Log($"[PathManager] 路径已锁定至 GUID: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(PersistentRoot);
        Directory.CreateDirectory(HotfixRoot);
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(LocalBundleRoot);
        Directory.CreateDirectory(TempBundleRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(SaveRoot);
        Directory.CreateDirectory(LogRoot);
    }
}


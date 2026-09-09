using System.IO;
using UnityEngine;

/// <summary>
/// 运行时路径管理。
/// 管理 persistentDataPath 下的热更、缓存、存档和日志目录。
/// </summary>
public static class RuntimePathManager
{
    public static string PersistentRoot => FYAssetPathUtility.JoinFilePath(Application.persistentDataPath, FYAssetSettings.Instance.ProjectName);
    
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
        string guidDir = buildIndex.BuildGUID;

        // .../ProjectName/[Platform]/Release
        EnvRoot = FYAssetPathUtility.JoinFilePath(PersistentRoot, platform, envDir);
        
        // .../ProjectName/[Platform]/Release/Hotfix
        HotfixRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Hotfix");
        
        // .../ProjectName/[Platform]/Release/Hotfix/Build_xxx (当前生效目录)
        // buildIndex.BuildGUID 可能是完整目录名或仅 GUID 段，统一补 Build_ 前缀
        if (!guidDir.StartsWith("Build_")) guidDir = "Build_" + guidDir;
        CurrentGUIDRoot = FYAssetPathUtility.JoinFilePath(HotfixRoot, guidDir);
        
        CacheRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Cache");
        SaveRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Saves");
        LogRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Logs");
        
        Debug.Log($"[RuntimePathManager] 路径已锁定至 GUID: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    /// <summary>
    /// 切换当前活动的 Build 目录（热更下载完成后调用）
    /// </summary>
    /// <param name="newBuildName">新的 Build 目录名，例如 "Build_20260209123045_2.0.0"</param>
    public static void SwitchToNewBuild(string newBuildName)
    {
        if (string.IsNullOrEmpty(newBuildName))
        {
            Debug.LogError("[RuntimePathManager] SwitchToNewBuild: newBuildName 不能为空");
            return;
        }
        
        string guidDir = newBuildName;
        if (!guidDir.StartsWith("Build_")) guidDir = "Build_" + guidDir;
        
        CurrentGUIDRoot = FYAssetPathUtility.JoinFilePath(HotfixRoot, guidDir);
        Debug.Log($"[RuntimePathManager] 已切换至新 Build: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    public static void EnsureDirectories()
    {
        FileHelper.EnsureDirectory(PersistentRoot);
        FileHelper.EnsureDirectory(HotfixRoot);
        FileHelper.EnsureDirectory(CurrentGUIDRoot);
        // Bundles 目录由热更下载或构建流程创建，这里不创建
        
        FileHelper.EnsureDirectory(CacheRoot);
        FileHelper.EnsureDirectory(SaveRoot);
        FileHelper.EnsureDirectory(LogRoot);
    }
}

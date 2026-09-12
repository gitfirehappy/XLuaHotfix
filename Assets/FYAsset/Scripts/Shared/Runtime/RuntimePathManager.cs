using System.IO;
using UnityEngine;

/// <summary>
/// 运行时路径和当前激活包根的唯一持有者。
/// </summary>
/// <remarks>
/// 运行时上下文只读取 <see cref="ActivePackageRoot"/>。内置包根与本地热更包根由激活流程显式切换，加载器不自行回退。
/// </remarks>
public static class RuntimePathManager
{
    public static string PersistentRoot => FYAssetPathUtility.JoinFilePath(Application.persistentDataPath, FYAssetSettings.Instance.ProjectName);
    
    // 运行时根据 BuildIndex 计算的目录。
    public static string EnvRoot { get; private set; }
    public static string CurrentGUIDRoot { get; private set; }
    public static string HotfixRoot { get; private set; }
   
    public static string CacheRoot { get; private set; }
    public static string SaveRoot { get; private set; }
    public static string LogRoot { get; private set; }

    /// <summary>安装包内置完整包根；Standalone 模式使用隔离子目录。</summary>
    public static string BuiltInPackageRoot => Mode == RuntimeMode.Standalone
        ? FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.STANDALONE_DIRECTORY_NAME)
        : Application.streamingAssetsPath;

    /// <summary>当前运行模式；初始化前默认为 Online。</summary>
    public static RuntimeMode Mode { get; private set; }

    /// <summary>
    /// 当前激活的资源包根；Manifest、Bundle 和 RawFile 都从这里读取。
    /// </summary>
    public static string ActivePackageRoot { get; private set; }

    /// <summary>
    /// 初始化路径，并锁定本次运行模式。
    /// </summary>
    public static void Initialize(BuildIndexData buildIndex)
    {
        string platform = buildIndex.Platform;
        if(string.IsNullOrEmpty(platform)) platform = "Unknown";

        string envDir = buildIndex.IsDebug ? "Debug" : "Release";
        string guidDir = buildIndex.BuildGUID;

        // RuntimeMode 是后续包根推导和热更决策的唯一来源。
        Mode = buildIndex.RuntimeMode;

        // .../ProjectName/[Platform]/Release
        EnvRoot = FYAssetPathUtility.JoinFilePath(PersistentRoot, platform, envDir);
        
        // .../ProjectName/[Platform]/Release/Hotfix
        HotfixRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Hotfix");
        
        // 当前包身份和读取根在激活时同步更新。
        CurrentGUIDRoot = GetHotfixPackageRoot(guidDir);
        ActivePackageRoot = CurrentGUIDRoot;
        
        CacheRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Cache");
        SaveRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Saves");
        LogRoot = FYAssetPathUtility.JoinFilePath(EnvRoot, "Logs");
        
        Debug.Log($"[RuntimePathManager] 路径已锁定至 GUID: {guidDir}\nRoot: {CurrentGUIDRoot}");
    }

    /// <summary>
    /// 计算 HotfixRoot 下的包根路径，不改变当前激活包根或包身份。
    /// </summary>
    /// <param name="packageName">包目录名，可省略 "Build_" 前缀。</param>
    /// <remarks>
    /// 检查阶段需要先算出候选包路径再决定是否激活，因此路径计算与激活必须分离。
    /// </remarks>
    public static string GetHotfixPackageRoot(string packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return null;

        string guidDir = packageName;
        if (!guidDir.StartsWith("Build_")) guidDir = "Build_" + guidDir;
        return FYAssetPathUtility.JoinFilePath(HotfixRoot, guidDir);
    }

    /// <summary>
    /// 目标包隔离写入根的目录名后缀：与正式 Build_* 同父目录、同卷，仅用于准备阶段。
    /// </summary>
    public const string STAGING_DIRECTORY_SUFFIX = ".staging";

    /// <summary>
    /// 换入 backup 目录的目录名后缀：正式包目录被新内容替换前的临时位置。
    /// </summary>
    public const string BACKUP_DIRECTORY_SUFFIX = ".backup";

    /// <summary>
    /// 计算目标包的隔离 staging 根；它始终是 HotfixRoot 的直接子级，不等于正式 Build_* 路径。
    /// </summary>
    /// <param name="packageName">包目录名，可省略 "Build_" 前缀。</param>
    /// <remarks>
    /// HotfixFlowBase 的所有目标写入（Bundle、manifest、元数据）都使用本路径，
    /// 正式包根只在 Apply 换入阶段由 staging move 得到。
    /// </remarks>
    public static string GetHotfixStagingRoot(string packageName)
    {
        return GetSuffixedPackageRoot(packageName, STAGING_DIRECTORY_SUFFIX);
    }

    /// <summary>
    /// 计算目标包的 backup 根：换入前正式包目录的临时位置，成功激活后删除。
    /// </summary>
    /// <param name="packageName">包目录名，可省略 "Build_" 前缀。</param>
    public static string GetHotfixBackupRoot(string packageName)
    {
        return GetSuffixedPackageRoot(packageName, BACKUP_DIRECTORY_SUFFIX);
    }

    private static string GetSuffixedPackageRoot(string packageName, string suffix)
    {
        string packageRoot = GetHotfixPackageRoot(packageName);
        return string.IsNullOrEmpty(packageRoot) ? null : packageRoot + suffix;
    }

    /// <summary>
    /// 切换当前活动的 Build 目录（热更下载完成后调用），并把它设为当前激活包根。
    /// </summary>
    /// <param name="newBuildName">新的 Build 目录名，例如 "Build_20260209123045_2.0.0"</param>
    /// <remarks>
    /// 与 <see cref="ActivateBuiltInPackage"/> 是互斥的两种激活结果，后调用者生效；
    /// 同一参数重复调用幂等（只是重新赋同一个路径）。
    /// </remarks>
    public static void SwitchToNewBuild(string newBuildName)
    {
        string packageRoot = GetHotfixPackageRoot(newBuildName);
        if (string.IsNullOrEmpty(packageRoot))
        {
            Debug.LogError("[RuntimePathManager] SwitchToNewBuild: newBuildName 不能为空");
            return;
        }

        CurrentGUIDRoot = packageRoot;
        // 包身份指针与读取根必须同步：本地包激活后不再读取其他包根
        ActivePackageRoot = CurrentGUIDRoot;
        Debug.Log($"[RuntimePathManager] 已切换至新 Build: {Path.GetFileName(packageRoot)}\nRoot: {CurrentGUIDRoot}");
    }

    /// <summary>
    /// 把内置完整包根设为当前激活包根（首次启动、单机模式或本地包不可用时的退化路径）。
    /// </summary>
    /// <remarks>
    /// 语义约定：
    /// 1. 只改写 <see cref="ActivePackageRoot"/>，不改写 <see cref="CurrentGUIDRoot"/> 与 <see cref="Mode"/>——
    ///    前者是本地热更包的目录身份，内置包运行时仍然保留它用于后续比较与切换；
    ///    后者是 BuildIndex 携带的运行模式，退化到内置包不改变运行模式，下次启动照常检查远端。
    /// 2. 目标根由 <see cref="BuiltInPackageRoot"/> 统一推导（Standalone 时含隔离子目录），
    ///    调用方不得自行拼接 StreamingAssets 路径。
    /// 3. 幂等：重复调用只是重新赋同一个路径。
    /// 4. 与 <see cref="SwitchToNewBuild"/> 的顺序关系：两者互斥，后调用者决定最终读取根；
    ///    激活流程若在切换本地包目录之后决定继续使用内置包，必须在本调用之后不再调用 SwitchToNewBuild。
    /// </remarks>
    public static void ActivateBuiltInPackage()
    {
        ActivePackageRoot = BuiltInPackageRoot;
        Debug.Log($"[RuntimePathManager] 已激活内置包根: {ActivePackageRoot}");
    }

    public static void EnsureDirectories()
    {
        FileHelper.EnsureDirectory(PersistentRoot);
        FileHelper.EnsureDirectory(HotfixRoot);
        FileHelper.EnsureDirectory(CurrentGUIDRoot);
        // Bundles 目录由热更下载或构建流程按需创建。
        
        FileHelper.EnsureDirectory(CacheRoot);
        FileHelper.EnsureDirectory(SaveRoot);
        FileHelper.EnsureDirectory(LogRoot);
    }
}

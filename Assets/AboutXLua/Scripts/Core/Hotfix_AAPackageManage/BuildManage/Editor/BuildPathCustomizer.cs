#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建后路径整理工具
/// 将 AA 的构建结果整理为：
/// OutputRoot/
///   ├─ catalog.json
///   ├─ version_state.json （这步还没生成）
///   └─ bundles/
///        ├─ bundle_a.bundle
///        └─ bundle_b.bundle
/// </summary>
public static class BuildPathCustomizer
{
    /// <summary>
    /// 整理构建产物
    /// </summary>
    /// <param name="buildSourceDir">Addressables 默认输出目录 (ServerData/Platform)</param>
    /// <param name="finalOutputDir">最终打包输出目录 (Project/HotfixOutput/Packages/ProjectName_...)</param>
    public static void OrganizeBuildOutput(string buildSourceDir, string finalOutputDir)
    {
        if (Directory.Exists(finalOutputDir))
        {
            Directory.Delete(finalOutputDir, true);
        }
        Directory.CreateDirectory(finalOutputDir);

        string bundleTargetDir = Path.Combine(finalOutputDir, "bundles");
        Directory.CreateDirectory(bundleTargetDir);

        var sourceFiles = Directory.GetFiles(buildSourceDir, "*", SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            string fileName = Path.GetFileName(file);
            string extension = Path.GetExtension(file).ToLower();

            // 处理 Catalog (通常是 catalog_hash.json，需重命名为 catalog.json)
            if (fileName.StartsWith("catalog") && extension == ".json")
            {
                string targetPath = Path.Combine(finalOutputDir, "catalog.json");
                File.Copy(file, targetPath, true);
                Debug.Log($"[PathCustomizer] Catalog 已复制并重命名: {targetPath}");
            }
            // 架构使用 version_state.json 进行版本比对，不需要 AA 自带的 hash 校验
            else if (fileName.StartsWith("catalog") && extension == ".hash")
            {
               // 直接跳过，不复制到 finalOutputDir
                continue;
            }
            // 处理 Bundles (.bundle)
            // 全量导出，下载时再通过 version_state.json 优化
            else if (extension == ".bundle")
            {
                string targetPath = Path.Combine(bundleTargetDir, fileName);
                File.Copy(file, targetPath, true);
            }
            // 其他文件 (如 bin 等，Addressable 默认 .bundle 扩展名，如果是 .bin 需自行适配)
            else if (extension == ".bin") 
            {
                string targetPath = Path.Combine(bundleTargetDir, fileName);
                File.Copy(file, targetPath, true);
            }
        }
        
        Debug.Log($"[PathCustomizer] 构建产物整理完毕: {finalOutputDir}");
    }
    
    /// <summary>
    /// 清理 Addressables 的默认输出目录 (ServerData/[Platform])
    /// </summary>
    public static void CleanServerData()
    {
        string platformSubDir = EditorUserBuildSettings.activeBuildTarget.ToString();
        string serverDataPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ServerData", platformSubDir);
        
        if (Directory.Exists(serverDataPath))
        {
            try 
            {
                Directory.Delete(serverDataPath, true);
                Debug.Log($"[BuildPathCustomizer] 已清空旧构建数据: {serverDataPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildPathCustomizer] 清空 ServerData 失败 (可能是文件占用)，请手动检查: {e.Message}");
            }
        }
        
        // 重新创建空目录（BuildPlayerContent 也会自动创建，为了保险起见）
        if (!Directory.Exists(serverDataPath))
        {
            Directory.CreateDirectory(serverDataPath);
        }
    }
}
#endif
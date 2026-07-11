#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Addressables 构建产物整理器。
/// 将直接写入最终目录的 AA catalog 规范化为：
/// OutputRoot/
///   ├─ catalog.json
///   └─ bundles/
///        ├─ bundle_a.bundle
///        └─ bundle_b.bundle
/// </summary>
public static class AddressablesBuildOutputOrganizer
{
    /// <summary>
    /// 规范化 catalog 文件名并移除未发布的 hash 文件。
    /// </summary>
    /// <param name="finalOutputDir">Addressables 直接写入的最终包目录。</param>
    public static void NormalizeBuildOutput(string finalOutputDir)
    {
        finalOutputDir = FYAssetPathUtility.NormalizePath(finalOutputDir);
        string catalogPath = FYAssetPathUtility.JoinFilePath(finalOutputDir, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        bool catalogFound = FileHelper.Exists(catalogPath);
        string[] files = FileHelper.GetFiles(finalOutputDir, "*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            string fileName = Path.GetFileName(file);
            string extension = Path.GetExtension(file).ToLowerInvariant();

            if (fileName.StartsWith("catalog") && extension == ".json")
            {
                if (!string.Equals(FYAssetPathUtility.NormalizePath(file), catalogPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    FileHelper.CopyFile(file, catalogPath, true);
                    FileHelper.TryDelete(file);
                }
                catalogFound = true;
            }
            else if (fileName.StartsWith("catalog") && extension == ".hash")
                FileHelper.TryDelete(file);
        }

        if (!catalogFound)
            throw new FileNotFoundException("Addressables catalog was not generated in the final package directory.", finalOutputDir);

        Debug.Log($"[AddressablesBuildOutputOrganizer] Catalog 已规范化: {catalogPath}");
    }

    [MenuItem("FYAsset/Tests/AA Output Organizer Self Check")]
    public static void RunSelfCheck()
    {
        string root = Path.Combine(Path.GetTempPath(), nameof(AddressablesBuildOutputOrganizer) + "_" + Guid.NewGuid().ToString("N"));
        string bundles = Path.Combine(root, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        try
        {
            Directory.CreateDirectory(bundles);
            File.WriteAllText(Path.Combine(root, "catalog_test.json"), "{}");
            File.WriteAllText(Path.Combine(root, "catalog_test.hash"), "hash");
            File.WriteAllText(Path.Combine(bundles, "test.bundle"), "bundle");

            NormalizeBuildOutput(root);

            Require(File.Exists(Path.Combine(root, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME)), "catalog.json was not created.");
            Require(!File.Exists(Path.Combine(root, "catalog_test.json")), "Source catalog was not removed.");
            Require(!File.Exists(Path.Combine(root, "catalog_test.hash")), "Catalog hash was not removed.");
            Require(File.Exists(Path.Combine(bundles, "test.bundle")), "Bundle output was changed.");
            Debug.Log($"[{nameof(AddressablesBuildOutputOrganizer)}] PASS - catalog normalization verified.");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif

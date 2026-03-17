using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LuaDirectoryScanner
{
    public static void ScanAndSync(LuaAutoSyncConfig config)
    {
        if (config == null)
        {
            Debug.LogWarning("LuaDirectoryScanner: config 为空，跳过扫描");
            return;
        }

        int mappingsProcessed = 0;
        int scriptsAdded = 0;

        foreach (var mapping in config.mappings)
        {
            if (string.IsNullOrEmpty(mapping.directoryPath))
            {
                Debug.LogWarning("LuaDirectoryScanner: 跳过空目录路径的映射");
                continue;
            }

            string fullPath = Path.Combine(Application.dataPath, mapping.directoryPath);
            if (!Directory.Exists(fullPath))
            {
                Debug.LogWarning($"LuaDirectoryScanner: 目录不存在，跳过: Assets/{mapping.directoryPath}");
                continue;
            }

            LuaScriptContainer container = mapping.container;

            #region 自动创建 Container SO
            if (container == null && !string.IsNullOrEmpty(mapping.outputDirectory))
            {
                container = CreateContainerAsset(mapping.directoryPath, mapping.outputDirectory);
                if (container != null)
                {
                    mapping.container = container;
                    EditorUtility.SetDirty(config);
                }
            }
            #endregion

            if (container == null)
            {
                Debug.LogWarning($"LuaDirectoryScanner: 映射 '{mapping.directoryPath}' 无容器且未配置生成目录，跳过");
                continue;
            }

            #region 扫描并合并
            var searchOption = mapping.recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var luaFiles = new List<string>();

            foreach (string ext in new[] { "*.lua", "*.lua.txt" })
            {
                luaFiles.AddRange(Directory.GetFiles(fullPath, ext, searchOption));
            }

            var existingAssets = new HashSet<TextAsset>(container.luaAssets.Where(a => a != null));
            int addedThisMapping = 0;

            foreach (string filePath in luaFiles)
            {
                string assetPath = "Assets" + filePath.Substring(Application.dataPath.Length).Replace('\\', '/');
                var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);

                if (textAsset != null && !existingAssets.Contains(textAsset))
                {
                    container.luaAssets.Add(textAsset);
                    existingAssets.Add(textAsset);
                    addedThisMapping++;
                }
            }
            #endregion

            if (addedThisMapping > 0)
            {
                EditorUtility.SetDirty(container);
            }

            scriptsAdded += addedThisMapping;
            mappingsProcessed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"LuaDirectoryScanner: 扫描完成，处理了 {mappingsProcessed} 个映射，新增 {scriptsAdded} 个脚本");
    }

    private static LuaScriptContainer CreateContainerAsset(string directoryPath, string outputDirectory)
    {
        string outputAssetDir = $"Assets/{outputDirectory}";
        if (!AssetDatabase.IsValidFolder(outputAssetDir))
        {
            CreateFolderRecursive(outputAssetDir);
        }

        string dirName = Path.GetFileName(directoryPath.TrimEnd('/', '\\'));
        string assetPath = $"{outputAssetDir}/{dirName}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<LuaScriptContainer>(assetPath);
        if (existing != null)
        {
            Debug.Log($"LuaDirectoryScanner: 已存在容器 {assetPath}，直接使用");
            return existing;
        }

        var newContainer = ScriptableObject.CreateInstance<LuaScriptContainer>();
        newContainer.groupName = dirName;
        AssetDatabase.CreateAsset(newContainer, assetPath);
        Debug.Log($"LuaDirectoryScanner: 自动创建容器 {assetPath}");
        return newContainer;
    }

    private static void CreateFolderRecursive(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源导入流水线 — 继承 AssetPostprocessor，自动发现并执行所有 IAssetImportRule 实现。
/// </summary>
public class AssetImportPipelineProcessor : AssetPostprocessor
{
    private static List<IAssetImportRule> _rules;

    private static List<IAssetImportRule> Rules
    {
        get
        {
            if (_rules == null)
            {
                RebuildRuleCache();
            }

            return _rules;
        }
    }

    /// <summary>
    /// 通过反射扫描所有程序集，找到 IAssetImportRule 实现类并实例化。
    /// </summary>
    private static void RebuildRuleCache()
    {
        _rules = new List<IAssetImportRule>();
        Type ruleType = typeof(IAssetImportRule);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            if (types == null)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    continue;
                }

                if (!ruleType.IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    var instance = Activator.CreateInstance(type) as IAssetImportRule;
                    if (instance != null)
                    {
                        _rules.Add(instance);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AssetImportPipeline] 无法实例化规则 {type.Name}: {ex.Message}");
                }
            }
        }

        Debug.Log($"[AssetImportPipeline] 已加载 {_rules.Count} 条导入规则");
    }

    /// <summary>
    /// 手动刷新规则缓存（新增/修改规则后如未自动生效可点此）。
    /// </summary>
    [MenuItem("XLua/Lua 工具/刷新导入规则缓存", false, 22)]
    public static void RefreshRules()
    {
        _rules = null;
        Debug.Log("[AssetImportPipeline] 规则缓存已清空，下次导入将自动重建");
    }

    private void OnPreprocessTexture()
    {
        RunPreprocess(assetImporter);
    }

    private void OnPreprocessModel()
    {
        RunPreprocess(assetImporter);
    }

    private void OnPreprocessAudio()
    {
        RunPreprocess(assetImporter);
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            foreach (var rule in Rules)
            {
                if (!rule.Match(path))
                {
                    continue;
                }

                try
                {
                    rule.OnPostprocess(path);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AssetImportPipeline] 后处理规则 '{rule.RuleName}' 执行失败: {path} | {ex.Message}");
                }
            }
        }
    }

    private void RunPreprocess(AssetImporter importer)
    {
        if (importer == null)
        {
            return;
        }

        foreach (var rule in Rules)
        {
            if (!rule.Match(importer.assetPath))
            {
                continue;
            }

            try
            {
                Debug.Log($"[AssetImportPipeline] 应用规则 '{rule.RuleName}' → {importer.assetPath}");
                rule.OnPreprocess(importer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssetImportPipeline] 预处理规则 '{rule.RuleName}' 执行失败: {importer.assetPath} | {ex.Message}");
            }
        }
    }
}

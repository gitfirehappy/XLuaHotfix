using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 引擎隐式资产自动收集 Task。
/// 扫描 Shader（全项目）和 Resources 目录下所有资产，统一打入 "$shared" Group
/// 并以每类独立的 BundleKey 隔离为不同 Bundle。
/// Unity 内置资源（Default-Material 等）已在 player build 中，无需额外收集。
/// 在 TaskAnalyzeDependencies 之前执行，使 builtin 资产参与依赖分析。
/// </summary>
public class TaskCollectBuiltins : IBuildTask
{
    public string TaskName => "TaskCollectBuiltins";
    /// <summary>可扩展的扫描类别：新增类型只需追加一行</summary>
    private static readonly BuiltinCategory[] Categories = new[]
    {
        new BuiltinCategory { Filter = "t:Shader", Dir = null,                 BundleKey = "shaders",   Label = "Shader" },
        new BuiltinCategory { Filter = "",         Dir = "Assets/Resources",    BundleKey = "resources", Label = "Resources" },
    };

    private struct BuiltinCategory
    {
        /// <summary>AssetDatabase.FindAssets 的 filter 字符串</summary>
        public string Filter;
        /// <summary>可选：限定搜索目录，null 表示全项目搜索</summary>
        public string Dir;
        /// <summary>BundleName 的 BundleKey 段</summary>
        public string BundleKey;
        /// <summary>日志用标签</summary>
        public string Label;
    }

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var assets = ctx.Get<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        if (assets == null || assets.Count == 0)
            return BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets,
                "TaskCollectAssets 未产出 Asset。无法收集 Builtin。", false);

        // 取第一个 Package 的 PackageName
        string pkgName = assets[0].PackageName ?? "Default";

        // 构建已有 GUID 集合
        var existingGUIDs = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].AssetGUID))
                existingGUIDs.Add(assets[i].AssetGUID);
        }

        var warnings = new List<string>();
        int totalAdded = 0;

        for (int c = 0; c < Categories.Length; c++)
        {
            var cat = Categories[c];
            string[] guids;
            if (!string.IsNullOrEmpty(cat.Dir))
                guids = AssetDatabase.FindAssets(cat.Filter, new[] { cat.Dir });
            else
                guids = AssetDatabase.FindAssets(cat.Filter);

            int added = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                if (existingGUIDs.Contains(guid))
                    continue;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                    continue;

                string primaryType = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? cat.Label;

                assets.Add(new CollectedAssetInfo
                {
                    AssetPath = path,
                    AssetGUID = guid,
                    Address = AssetAddressGenerator.GenerateAddress(path, primaryType, AssetAddressStyle.ShortName),
                    PrimaryType = primaryType,
                    Labels = new List<string>(),
                    GroupLabels = new List<string>(),
                    AssetLabels = new List<string>(),
                    GroupName = SystemIdentifiers.SharedGroupName,
                    PackageName = pkgName,
                    BundleName = BundleNameBuilder.BuildShared(
                        pkgName,
                        cat.BundleKey,
                        EPayloadKind.Serialized,
                        primaryType),
                    BundlePackingMode = BundlePackingMode.PackSeparately,
                    Classification = new AssetClassification
                    {
                        Role = EAssetRole.ImplicitDependency,
                        PayloadKind = EPayloadKind.Serialized
                    },
                    CollectorType = ECollectorType.Implicit,
                    IsInSharedBundle = true,
                    IsDuplicated = false
                });

                existingGUIDs.Add(guid);
                added++;
            }

            if (added > 0)
                warnings.Add($"[BUILTINS] {added} {cat.Label}(s) collected.");
            totalAdded += added;
        }

        ctx.Set(BuildContextKeys.CollectedAssets, assets);
        return BuildTaskResult.Ok(totalAdded > 0 ? warnings : null);
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 引擎隐式资产自动收集 Task —— 对标 Addressables Built In Data 默认 Group。
/// 扫描 Shader、Resources 目录资源、Built-in 额外资源，统一打入 "$shared" Group
/// 并以每类独立的 PackKey 隔离为不同 Bundle。E4 BFS 遍历将其视为已归属资产。
/// 在 TaskAnalyzeDependencies 之前执行。
/// </summary>
public class TaskCollectBuiltins : IBuildTask
{
    public string TaskName => "TaskCollectBuiltins";
    public string[] DependsOn => new[] { "TaskCollectAssets" };
    public string[] ReadKeys => new[] { BuildContextKeys.CollectedAssets };
    public string[] WriteKeys => new[] { BuildContextKeys.CollectedAssets };

    /// <summary>可扩展的扫描类别：新增类型只需追加一行</summary>
    private static readonly BuiltinCategory[] Categories = new[]
    {
        new BuiltinCategory { Filter = "t:Shader",                    Dir = null,                 PackKey = "shaders",   Label = "Shader" },
        new BuiltinCategory { Filter = "t:Material t:Texture2D t:Mesh", Dir = "Assets/Resources", PackKey = "resources", Label = "Resources" },
        new BuiltinCategory { Filter = "t:Material",                  Dir = "Resources",          PackKey = "builtin",   Label = "Builtin" },
    };

    private struct BuiltinCategory
    {
        /// <summary>AssetDatabase.FindAssets 的 filter 字符串</summary>
        public string Filter;
        /// <summary>可选：限定搜索目录，null 表示全项目搜索</summary>
        public string Dir;
        /// <summary>BundleName 的 PackKey 段</summary>
        public string PackKey;
        /// <summary>日志用标签</summary>
        public string Label;
    }

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var assets = ctx.Get<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        if (assets == null || assets.Count == 0)
            return BuildTaskResult.Fail("NO_COLLECTED_ASSETS",
                "TaskCollectAssets produced no assets. Cannot collect builtins.", false);

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

                string primaryType = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? cat.Label;

                assets.Add(new CollectedAssetInfo
                {
                    AssetPath = path,
                    AssetGUID = guid,
                    Address = AssetAddressGenerator.GenerateShortAddress(path, primaryType, true),
                    PrimaryType = primaryType,
                    Labels = new List<string>(),
                    GroupName = SystemIdentifiers.SharedGroupName,
                    PackageName = pkgName,
                    BundleName = BundleNameBuilder.Build(pkgName, "$shared", cat.PackKey),
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

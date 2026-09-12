using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AB 主干第 1 阶段：收集构建输入。
/// 执行 AssetCollectionSetting 扫描（排除规则、RawFile 白名单、Address/Labels 覆盖），
/// 再补入框架内置内容（全项目 Shader 与 Assets/Resources），统一写入 CollectedAssets / SharePolicy。
/// </summary>
/// <remarks>
/// 内置内容必须在依赖分析之前并入同一份 CollectedAssets：
/// 它们与显式采集资源一起参与依赖分析、打包与 Manifest 归属，分两次写入会产生两套事实。
/// </remarks>
public class CollectABAssetsTask : IBuildTask
{
    public string TaskName => "CollectABAssets";

    /// <summary>内置内容分类：过滤条件、可选限定目录、Bundle 名片段与日志标签。</summary>
    private static readonly BuiltinCategory[] BuiltinCategories =
    {
        new BuiltinCategory { Filter = "t:Shader", Dir = null, BundleKey = "shaders", Label = "Shader" },
        new BuiltinCategory { Filter = "", Dir = "Assets/Resources", BundleKey = "resources", Label = "Resources" },
    };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        AssetCollectionSetting setting = CollectorMutationUtility.LoadSetting();
        if (setting == null)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.SettingNull,
                $"未找到 AssetCollectionSetting: {FYAssetABSettings.Instance.AssetCollectionSettingPath}");
        }

        var warnings = new List<string>();

        List<BuildMessage> validationMessages = AssetCollectionSettingValidator.Validate(setting);
        if (AppendMessages(validationMessages, warnings))
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"AssetCollectionSetting 校验失败，共 {validationMessages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        ScanResult scanResult = CollectionScanner.Scan(setting, CollectionScanOptions.FromSetting(setting));
        if (AppendMessages(scanResult.Messages, warnings))
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"Collection 扫描失败，共 {scanResult.Messages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        if (scanResult.Assets == null || scanResult.Assets.Count == 0)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets,
                "Collection 扫描没有产出任何 Asset。请检查 Collector 配置。", false);
        }

        AppendBuiltinAssets(scanResult.Assets, warnings);

        ctx.Set(ABBuildContextKeys.CollectedAssets, scanResult.Assets);
        ctx.Set(ABBuildContextKeys.SharePolicy, setting.SharePolicy);

        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// 补入框架内置内容：扫描到的资产统一进 "$shared" Group 并按分类隔离为独立内容；
    /// 已由 Collector 采集的 GUID 不重复加入。Unity 内置资源（Default-Material 等）随 Player 构建，无需收集。
    /// </summary>
    private static void AppendBuiltinAssets(List<CollectedAssetInfo> assets, List<string> warnings)
    {
        var existingGUIDs = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].AssetGUID))
                existingGUIDs.Add(assets[i].AssetGUID);
        }

        for (int c = 0; c < BuiltinCategories.Length; c++)
        {
            BuiltinCategory category = BuiltinCategories[c];
            string[] guids = string.IsNullOrEmpty(category.Dir)
                ? AssetDatabase.FindAssets(category.Filter)
                : AssetDatabase.FindAssets(category.Filter, new[] { category.Dir });

            int added = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                if (existingGUIDs.Contains(guid))
                    continue;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                    continue;

                string primaryType = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? category.Label;

                assets.Add(new CollectedAssetInfo
                {
                    AssetPath = path,
                    AssetGUID = guid,
                    Address = AssetAddressGenerator.GenerateAddress(path, primaryType, AssetAddressStyle.ShortName),
                    PrimaryType = primaryType,
                    Labels = new List<string>(),
                    GroupName = SystemIdentifiers.SharedGroupName,
                    ContentName = BundleNameBuilder.BuildShared(
                        category.BundleKey,
                        AssetContentType.SerializedObject,
                        primaryType),
                    BundlePackingMode = BundlePackingMode.PackSeparately,
                    ContentType = AssetContentType.SerializedObject,
                    // 内置补入属于框架显式收集，仍是可寻址的公共资源。
                    DependencyOrigin = AssetDependencyOrigin.Explicit,
                    IsPublic = true
                });

                existingGUIDs.Add(guid);
                added++;
            }

            if (added > 0)
                warnings.Add($"[BUILTINS] {added} {category.Label}(s) collected.");
        }
    }

    private static bool AppendMessages(List<BuildMessage> messages, List<string> warnings)
    {
        bool hasError = false;
        if (messages == null)
            return false;

        foreach (BuildMessage message in messages)
        {
            warnings.Add($"[{message.Code}] {message.Message} ({message.Source})");
            if (message.Severity == BuildSeverity.Error)
                hasError = true;
        }

        return hasError;
    }

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
}

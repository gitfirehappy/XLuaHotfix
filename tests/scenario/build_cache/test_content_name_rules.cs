using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// 内容逻辑名与物理名规则：逻辑名不含 Package 前缀与 hash 后缀；
/// 物理名由 Group/打包模式可读段与 12 位身份哈希构成，构建 Task 不把 hash 拼回名称。
/// </summary>
internal static class ContentNameRulesTests
{
    private const string BuilderPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/BundleNameBuilder.cs";
    private const string BuildBundlesTaskPath = "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs";
    private const string BundleBuildInfoPath = "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/BundleBuildInfo.cs";

    /// <summary>32 位及以上连续十六进制串视为内容 hash 形态。</summary>
    private const string HashLikePattern = "[0-9a-f]{32,}";

    public static void Declare(GateRun run)
    {
        run.Check("PackSeparatelyNameHasNoHashOrPackagePrefix", PackSeparatelyNameHasNoHashOrPackagePrefix);
        run.Check("PackTogetherNameHasNoHash", PackTogetherNameHasNoHash);
        run.Check("LabelKeyOrderDoesNotChangeName", LabelKeyOrderDoesNotChangeName);
        run.Check("SharedNameHasNoHash", SharedNameHasNoHash);
        run.Check("ShortGuidInPackSeparatelyIsNotContentHash", ShortGuidInPackSeparatelyIsNotContentHash);
        run.Check("BuilderDoesNotAppendFileExtensions", BuilderDoesNotAppendFileExtensions);
        run.Check("BuildTaskUsesPhysicalNameAsBundleName", BuildTaskUsesPhysicalNameAsBundleName);
        run.Check("PhysicalNameIsReadableAndBounded", PhysicalNameIsReadableAndBounded);
        run.Check("WholeGroupAndLabelNamesStayStable", WholeGroupAndLabelNamesStayStable);
        run.Check("BuildTaskDoesNotRenameOutputsByHash", BuildTaskDoesNotRenameOutputsByHash);
        run.Check("BundleBuildInfoDocumentsOutputFileName", BundleBuildInfoDocumentsOutputFileName);
    }

    private static void PackSeparatelyNameHasNoHashOrPackagePrefix()
    {
        string name = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackSeparately, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            new List<string> { "hotfix" }, AssetContentType.SerializedObject, "GameObject");

        Check.False(Regex.IsMatch(name, HashLikePattern), $"逻辑内容名不得包含 hash 后缀（实际={name}）");
        Check.True(name.StartsWith("ui_", StringComparison.Ordinal), $"逻辑内容名必须以 Group 段开头，不得再有 Package 前缀（实际={name}）");
        Check.Contains(name, "_serialized_", $"逻辑内容名必须保留内容类型段（实际={name}）");
        Check.False(name.Contains("pkg", StringComparison.OrdinalIgnoreCase), $"逻辑内容名不得包含 Package 前缀（实际={name}）");
        Check.False(name.Contains(".", StringComparison.Ordinal), $"逻辑内容名不得携带文件扩展名（实际={name}）");
    }

    private static void PackTogetherNameHasNoHash()
    {
        string name = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackTogether, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            null, AssetContentType.SerializedObject, "GameObject");

        Check.False(Regex.IsMatch(name, HashLikePattern), $"PackTogether 内容名不得包含 hash（实际={name}）");
        Check.Equal("ui_serialized_gameobject_all_all", name, "PackTogether 内容名必须由 Group/内容类型/主类型/打包模式组成");
    }

    private static void LabelKeyOrderDoesNotChangeName()
    {
        string forward = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackTogetherByLabel, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            new List<string> { "b", "a" }, AssetContentType.SerializedObject, "GameObject");
        string backward = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackTogetherByLabel, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            new List<string> { "a", "b" }, AssetContentType.SerializedObject, "GameObject");

        Check.Equal(forward, backward, "Label 顺序不同必须得到同一逻辑内容名（成员排序确定性）");
    }

    private static void SharedNameHasNoHash()
    {
        // 共享内容的 BundleKey 来自主类型或固定标识（"lua-index"），不携带内容 hash
        string name = BundleNameBuilder.BuildShared("Texture2D", AssetContentType.SerializedObject, "Texture2D");
        Check.False(Regex.IsMatch(name, HashLikePattern), $"共享内容名不得包含 hash（实际={name}）");
        Check.True(name.StartsWith(SystemIdentifiers.SharedGroupName + "_", StringComparison.Ordinal),
            $"共享内容名必须以共享 Group 段开头（实际={name}）");
        Check.Equal("$shared_serialized_texture2d_texture2d", name, "共享内容名必须由共享 Group/内容类型/主类型/BundleKey 组成");
    }

    private static void ShortGuidInPackSeparatelyIsNotContentHash()
    {
        string name = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackSeparately, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            null, AssetContentType.SerializedObject, "GameObject");

        Check.Contains(name, "1a2b3c4d", $"PackSeparately 的 BundleKey 保留 short GUID 段（实际={name}）");
        Check.False(Regex.IsMatch(name, HashLikePattern), $"short GUID 属于既有命名规则，不得扩展为完整 hash（实际={name}）");
    }

    private static void BuilderDoesNotAppendFileExtensions()
    {
        string name = BundleNameBuilder.Build(
            "sfx", BundlePackingMode.PackSeparately, "Assets/Audio/hit.wav", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            null, AssetContentType.RawFile, "AudioClip");

        Check.False(name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase), $"逻辑内容名不得携带 .bundle 扩展名（实际={name}）");
        Check.Contains(name, "_rawfile_", $"RawFile 内容名必须保留内容类型段（实际={name}）");
    }

    private static void BuildTaskUsesPhysicalNameAsBundleName()
    {
        string code = RepoSource.Read(BuildBundlesTaskPath);
        Check.Contains(code, "assetBundleName = plan.PhysicalName",
            "Serialized 内容的 Unity Bundle 名必须使用物理文件名");
        Check.Contains(code, "assetBundleName = plan.OutputNames[s]",
            "Scene 内容的 Unity Bundle 名必须使用计划内的物理文件名");
        Check.Contains(code, "BundleNameBuilder.BuildPhysicalName(contentName, ResolveReadableName(members))",
            "内容计划必须由逻辑名与可读段派生物理名");
        Check.Contains(code, "OutputFileName = digest.Name",
            "物理输出文件名必须来自实际产物摘要，不得另行拼接 hash");
        Check.NotContains(code, "SceneBundleSuffix", "Scene 不得再拼接旧后缀段");
    }

    /// <summary>物理名契约：12 位小写身份哈希结尾、保留打包模式可读段、超长逻辑名不产生超长物理名。</summary>
    private static void PhysicalNameIsReadableAndBounded()
    {
        string contentName = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackSeparately, "Assets/UI/panel.prefab", "1a2b3c4d5e6f708192a3b4c5d6e7f809",
            null, AssetContentType.SerializedObject, "GameObject");
        string physical = BundleNameBuilder.BuildPhysicalName(contentName, "panel");

        Check.True(Regex.IsMatch(physical, "_[0-9a-f]{12}$"), $"物理名必须以 12 位身份哈希结尾（实际={physical}）");
        Check.Contains(physical, "_asset_", $"PackSeparately 物理名必须保留 asset 段（实际={physical}）");
        Check.Contains(physical, "_panel_", $"物理名必须保留资源短名可读段（实际={physical}）");
        Check.True(physical.Length < 120, $"物理名必须保持短小（实际长度={physical.Length}）");

        string longLogical = BundleNameBuilder.Build(
            "ui", BundlePackingMode.PackSeparately,
            "Assets/Deep/Folder/With/Very/Long/Path/And/Asset/Name/panel.prefab",
            "1a2b3c4d5e6f708192a3b4c5d6e7f809", null, AssetContentType.SerializedObject, "GameObject");
        string longPhysical = BundleNameBuilder.BuildPhysicalName(longLogical, "panel");
        Check.True(longPhysical.Length < 120,
            $"完整路径 Address 不得把物理名撑长（实际长度={longPhysical.Length}，逻辑名长度={longLogical.Length}）");
    }

    /// <summary>整组/标签模式不使用成员短名，与标签集合绑定。</summary>
    private static void WholeGroupAndLabelNamesStayStable()
    {
        string together = BundleNameBuilder.BuildPhysicalName(
            BundleNameBuilder.Build("ui", BundlePackingMode.PackTogether, "Assets/UI/panel.prefab",
                "1a2b3c4d5e6f708192a3b4c5d6e7f809", null, AssetContentType.SerializedObject, "GameObject"),
            "panel");
        Check.True(Regex.IsMatch(together, "_all_[0-9a-f]{12}$"),
            $"PackTogether 物理名必须是 group_all_hash12 形态（实际={together}）");

        string labels = BundleNameBuilder.BuildPhysicalName(
            BundleNameBuilder.Build("ui", BundlePackingMode.PackTogetherByLabel, "Assets/UI/panel.prefab",
                "1a2b3c4d5e6f708192a3b4c5d6e7f809", new List<string> { "b", "a" },
                AssetContentType.SerializedObject, "GameObject"),
            "panel");
        Check.True(Regex.IsMatch(labels, "_labels_.+_[0-9a-f]{12}$"),
            $"PackTogetherByLabel 物理名必须是 group_labels_labelKey_hash12 形态（实际={labels}）");
    }

    private static void BuildTaskDoesNotRenameOutputsByHash()
    {
        string code = RepoSource.Read(BuildBundlesTaskPath);
        Check.NotContains(code, "BundleFileNameStyle", "构建 Task 不得再按 FileNameStyle 重命名产物（物理命名保持现状）");
        Check.NotContains(code, "HashName", "构建 Task 不得再出现 hash 命名分支");
        Check.NotContains(code, "GenerateFileHash", "构建 Task 不得用文件 hash 参与命名");
        Check.False(Regex.IsMatch(code, HashLikePattern), "构建 Task 源码不得出现完整 hash 参与命名的形态");
    }

    private static void BundleBuildInfoDocumentsOutputFileName()
    {
        string code = RepoSource.Read(BundleBuildInfoPath);
        Check.Contains(code, "OutputFileName", "BundleBuildInfo 必须继续区分逻辑内容名与物理输出文件名");
        Check.NotContains(code, "md5hash", "BundleBuildInfo 不得再声明 hash 后缀文件名示例");
    }
}

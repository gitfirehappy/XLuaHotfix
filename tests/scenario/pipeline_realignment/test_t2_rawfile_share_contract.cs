using System;
using System.Text.RegularExpressions;

/// <summary>
/// T2「RawFile 与共享依赖」目标契约门禁（计划 Code Change Map T2、Confirmed Decisions - AB 依赖和共享 / RawFile）。
/// 目标结构：内容类型由 EPayloadKind 改为 AssetContentType（SerializedObject/Scene/RawFile）；
/// 依赖来源改用 AssetDependencyOrigin（Explicit/Implicit）；分类器消费项目级 RawFileRules 白名单；
/// 依赖分析只消费 ForceSharePatterns/NoSharePatterns，删除 MinReferenceCount/MinAssetSizeBytes；
/// CollectedAssetInfo 用 ContentType/DependencyOrigin/IsPublic/ContentName 表达构建中间模型。
/// </summary>
internal static class RawFileShareContractTests
{
    private const string AbRoot = "Assets/FYAsset/Scripts/AB";
    private const string ScriptsRoot = "Assets/FYAsset/Scripts";
    private const string ClassifierPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/AssetClassifier.cs";
    private const string DependencyAnalyzerPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs";
    private const string CollectedAssetInfoPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectedAssetInfo.cs";

    public static void Run()
    {
        GateChecks.RunAll(
            ("AssetContentTypeDeclaresContentKinds", VerifyAssetContentTypeDeclaresContentKinds),
            ("AssetDependencyOriginDeclaresOrigins", VerifyAssetDependencyOriginDeclaresOrigins),
            ("EPayloadKindRemovedFromScripts", VerifyEPayloadKindRemovedFromScripts),
            ("ShareThresholdFieldsRemovedFromTree", VerifyShareThresholdFieldsRemovedFromTree),
            ("AssetClassifierConsumesRawFileRules", VerifyAssetClassifierConsumesRawFileRules),
            ("DependencyAnalyzerConsumesSharePolicyPatterns", VerifyDependencyAnalyzerConsumesSharePolicyPatterns),
            ("CollectedAssetInfoDeclaresTargetFields", VerifyCollectedAssetInfoDeclaresTargetFields),
            ("CollectedAssetInfoDropsLegacyFields", VerifyCollectedAssetInfoDropsLegacyFields));
    }

    /// <summary>内容类型枚举必须覆盖序列化资产、场景与原始文件三条构建路线。</summary>
    private static void VerifyAssetContentTypeDeclaresContentKinds()
    {
        AssertDeclaringTypeHasMembers(AbRoot, "AssetContentType",
            new[] { "SerializedObject", "Scene", "RawFile" },
            "AssetContentType 必须声明 SerializedObject/Scene/RawFile：分类顺序的三种出口分别对应 Bundle 序列化资产、Scene 和 RawFile 构建路线");
    }

    /// <summary>依赖来源只区分显式声明与自动发现。</summary>
    private static void VerifyAssetDependencyOriginDeclaresOrigins()
    {
        AssertDeclaringTypeHasMembers(AbRoot, "AssetDependencyOrigin",
            new[] { "Explicit", "Implicit" },
            "AssetDependencyOrigin 必须声明 Explicit/Implicit：显式收集即公共资源，隐式发现的依赖内部化，该区分只服务构建诊断");
    }

    /// <summary>旧载荷枚举名必须从脚本整树消失。</summary>
    private static void VerifyEPayloadKindRemovedFromScripts()
    {
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "EPayloadKind",
            "EPayloadKind 必须从 Assets/FYAsset/Scripts 整树消失：内容类型由 AssetContentType 表达，旧枚举名残留会让序列化与运行时判断分叉");
    }

    /// <summary>共享阈值字段必须删除，共享决策只由 Force/NoShare 模式决定。</summary>
    private static void VerifyShareThresholdFieldsRemovedFromTree()
    {
        GateAssert.TreeHasNoSymbol(AbRoot, "MinReferenceCount",
            "MinReferenceCount 必须从 Assets/FYAsset/Scripts/AB 整树消失：计划明确删除引用数阈值，单引用随行、多引用共享由引用次数本身决定");
        GateAssert.TreeHasNoSymbol(AbRoot, "MinAssetSizeBytes",
            "MinAssetSizeBytes 必须从 Assets/FYAsset/Scripts/AB 整树消失：计划明确删除体积阈值，共享决策不以资产大小为依据");
    }

    /// <summary>分类器按项目级白名单判定 RawFile。</summary>
    private static void VerifyAssetClassifierConsumesRawFileRules()
    {
        string code = RepoSource.ReadCode(ClassifierPath);
        GateAssert.HasSymbol(code, "RawFileRules",
            "AssetClassifier.cs 必须引用 RawFileRules：分类顺序要求先判定项目级 RawFile 白名单，再判定 Scene 与 SerializedObject");
    }

    /// <summary>依赖分析的共享决策输入只允许 Force/NoShare 模式。</summary>
    private static void VerifyDependencyAnalyzerConsumesSharePolicyPatterns()
    {
        string code = RepoSource.ReadCode(DependencyAnalyzerPath);
        GateAssert.HasSymbol(code, "ForceSharePatterns",
            "DependencyAnalyzer.cs 必须消费 ForceSharePatterns：ForceShare 强制共享，同时命中 NoShare 时应阻断构建");
        GateAssert.HasSymbol(code, "NoSharePatterns",
            "DependencyAnalyzer.cs 必须消费 NoSharePatterns：NoShare 禁止共享，是共享策略的另一半输入");
        GateAssert.NoSymbol(code, "MinReferenceCount",
            "DependencyAnalyzer.cs 不得再读取 MinReferenceCount：引用数阈值已删除，隐式依赖按单引用随行、多引用共享处理");
        GateAssert.NoSymbol(code, "MinAssetSizeBytes",
            "DependencyAnalyzer.cs 不得再读取 MinAssetSizeBytes：体积阈值已删除，共享决策不以资产大小为依据");
    }

    /// <summary>构建中间模型的目标字段。</summary>
    private static void VerifyCollectedAssetInfoDeclaresTargetFields()
    {
        string code = RepoSource.ReadCode(CollectedAssetInfoPath);
        GateAssert.HasSymbol(code, "ContentType",
            "CollectedAssetInfo 必须含 ContentType：构建内容类型是区分 SerializedObject/Scene/RawFile 构建路线的字段");
        GateAssert.HasSymbol(code, "DependencyOrigin",
            "CollectedAssetInfo 必须含 DependencyOrigin：显式/隐式来源是公共资源与内部依赖的判定依据");
        GateAssert.HasSymbol(code, "IsPublic",
            "CollectedAssetInfo 必须含 IsPublic：显式收集的资源都是公共资源，该标记决定 Address 与索引可见性");
        GateAssert.HasSymbol(code, "ContentName",
            "CollectedAssetInfo 必须含 ContentName：构建内容名称取代旧 Bundle 名称与 Package 前缀");
    }

    /// <summary>旧的 Role/Payload 覆盖字段不得留在构建中间模型里。</summary>
    private static void VerifyCollectedAssetInfoDropsLegacyFields()
    {
        string code = RepoSource.ReadCode(CollectedAssetInfoPath);
        GateAssert.NoSymbol(code, "Role",
            "CollectedAssetInfo 不得再含 Role：资产角色语义删除，公共性由 IsPublic 表达");
        GateAssert.NoSymbol(code, "PayloadKind",
            "CollectedAssetInfo 不得再含 PayloadKind：载荷类型被 ContentType 取代");
        GateAssert.NoSymbol(code, "AutoRole",
            "CollectedAssetInfo 不得再含 AutoRole：自动角色推断随 Role 字段一起删除");
        GateAssert.NoSymbol(code, "AutoPayload",
            "CollectedAssetInfo 不得再含 AutoPayload：自动载荷推断随 PayloadKind 字段一起删除");
    }

    /// <summary>
    /// 断言目录树中存在声明指定类型的文件，且该文件的净化源码同时出现全部成员名。
    /// 先定位真正声明该类型的文件，避免成员名仅出现在引用处就判定为满足。
    /// </summary>
    private static void AssertDeclaringTypeHasMembers(string root, string typeName, string[] members, string purpose)
    {
        var candidates = RepoSource.FilesContaining(root, typeName);
        GateAssert.True(candidates.Count > 0, $"{purpose}（{root} 内未出现类型名 {typeName}）");

        for (int i = 0; i < candidates.Count; i++)
        {
            string code = RepoSource.ReadCode(candidates[i]);
            if (!Regex.IsMatch(code, @"\b(?:class|struct|enum|interface)\s+" + Regex.Escape(typeName) + @"\b"))
                continue;

            bool complete = true;
            for (int j = 0; j < members.Length; j++)
                complete &= RepoSource.HasToken(code, members[j]);

            if (complete)
                return;
        }

        GateAssert.True(false,
            $"{purpose}（{root} 中声明 {typeName} 的文件未同时包含成员 {string.Join("/", members)}；候选文件: {string.Join(", ", candidates)}）");
    }
}

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Collection 配置契约门禁。
/// 验证 AssetCollectionSetting 的持有关系、规则字段、反射规则体系清理和配置资产结构。
/// </summary>
internal static class CollectionContractTests
{
    private const string SettingPath = "Assets/FYAsset/Scripts/AB/Build/Collector/AssetCollectionSetting.cs";
    private const string SettingFileName = "AssetCollectionSetting.cs";
    private const string CollectorEnumsPath = "Assets/FYAsset/Scripts/AB/Build/Collector/CollectorEnums.cs";
    private const string SettingAssetPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";
    private const string ScriptsRoot = "Assets/FYAsset/Scripts";
    private const string AbRoot = "Assets/FYAsset/Scripts/AB";
    private const string RulesDir = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules";

    public static void Run()
    {
        GateChecks.RunAll(
            ("SettingDeclaresGroupsInsteadOfPackages", VerifySettingDeclaresGroupsInsteadOfPackages),
            ("SettingDeclaresAssetAddressEntries", VerifySettingDeclaresAssetAddressEntries),
            ("SettingOwnsRawFileRulesAndSharePolicy", VerifySettingOwnsRawFileRulesAndSharePolicy),
            ("SettingDropsGlobalAddressStyleAndKeepsIgnorePatterns", VerifySettingDropsGlobalAddressStyleAndKeepsIgnorePatterns),
            ("CollectorEnumsDropLegacyKinds", VerifyCollectorEnumsDropLegacyKinds),
            ("CollectionGroupDropsLabelsField", VerifyCollectionGroupDropsLabelsField),
            ("CollectorKeepsOnlyPathFields", VerifyCollectorKeepsOnlyPathFields),
            ("AssetAddressEntryTypeDeclaresAddressFields", VerifyAssetAddressEntryTypeDeclaresAddressFields),
            ("RawFileRulesTypeDeclaresPatterns", VerifyRawFileRulesTypeDeclaresPatterns),
            ("SharePolicyConfigTypeDeclaresSharePatterns", VerifySharePolicyConfigTypeDeclaresSharePatterns),
            ("ReflectionRuleFilesAndDirectoryRemoved", VerifyReflectionRuleFilesAndDirectoryRemoved),
            ("ReflectionRuleSymbolsRemovedFromScripts", VerifyReflectionRuleSymbolsRemovedFromScripts),
            ("LegacyConfigSymbolsRemovedFromScripts", VerifyLegacyConfigSymbolsRemovedFromScripts),
            ("CollectionScannerKeepsDefaultExclusions", VerifyCollectionScannerKeepsDefaultExclusions),
            ("CollectorSettingAssetDropsLegacyYamlKeys", VerifyCollectorSettingAssetDropsLegacyYamlKeys));
    }

    /// <summary>
    /// 规则反射体系删除后，「默认脚本 / 程序集 / 元文件 / Editor 目录排除」必须由扫描层保留：
    /// .cs 等文件必须被忽略，否则会进入采集并争抢同名自动 Address。
    /// </summary>
    private static void VerifyCollectionScannerKeepsDefaultExclusions()
    {
        const string classifierPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/AssetClassifier.cs";
        const string scannerPath = "Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectionScanner.cs";

        string classifier = RepoSource.ReadCode(classifierPath);
        GateAssert.HasSymbol(classifier, "IsExcludedByDefault",
            $"{classifierPath} 必须提供扫描阶段默认排除判定，替代被删除的 CollectAll 过滤规则");

        // 扩展名是字符串字面量，ReadCode 会把它们清空，因此这里读原文核对具体取值。
        string classifierRaw = RepoSource.Read(classifierPath);
        string[] excludedExtensions = { ".meta", ".cs", ".dll", ".asmdef", ".asmref", ".gitignore" };
        for (int i = 0; i < excludedExtensions.Length; i++)
        {
            GateAssert.Contains(classifierRaw, "\"" + excludedExtensions[i] + "\"",
                $"默认排除必须覆盖 {excludedExtensions[i]}：CollectAll 的等价行为要求脚本、程序集定义与元文件不参与采集（{classifierPath}）");
        }

        GateAssert.Contains(classifierRaw, "/Editor/",
            $"默认排除必须覆盖 Editor 目录：CollectAll 的等价行为要求编辑器专用内容不参与运行时打包（{classifierPath}）");

        string scanner = RepoSource.ReadCode(scannerPath);
        GateAssert.HasSymbol(scanner, "IsExcludedByDefault",
            $"{scannerPath} 采集每个资产时必须调用默认排除判定，否则 .cs 等文件会重新进入采集");
        GateAssert.Contains(scanner, "Directory.Exists",
            $"{scannerPath} 必须跳过目录型资产（如 xlua.bundle 这类 DefaultAsset 文件夹）："
            + "它们既不能作为 SerializedObject 构建，也不能当作单个物理文件拷贝");
    }

    /// <summary>Setting 直接持有 Groups，不再有 Package 中间层。</summary>
    private static void VerifySettingDeclaresGroupsInsteadOfPackages()
    {
        string body = SettingBody();
        GateAssert.HasSymbol(body, "Groups",
            "AssetCollectionSetting 必须直接持有 Groups：计划删除 Package 配置层，Setting 成为 Group 列表的唯一持有者");
        GateAssert.NoSymbol(body, "Packages",
            "AssetCollectionSetting 不得再含 Packages 字段：计划明确删除 AssetCollectionPackage，且不保留当前 Package 兼容壳");
    }

    /// <summary>资产级元数据从自动表改为人工覆盖表。</summary>
    private static void VerifySettingDeclaresAssetAddressEntries()
    {
        string body = SettingBody();
        GateAssert.HasSymbol(body, "AssetAddressEntries",
            "AssetCollectionSetting 必须持有 AssetAddressEntries：资源 Address/Labels 只保存用户明确编辑的数据");
        GateAssert.NoSymbol(body, "AssetOverrides",
            "AssetCollectionSetting 不得保留 AssetOverrides 旧设计名称");
        GateAssert.NoSymbol(body, "AssetEntries",
            "AssetCollectionSetting 不得再含 AssetEntries");
    }

    /// <summary>项目级 RawFile 规则与共享策略都上移到 Setting。</summary>
    private static void VerifySettingOwnsRawFileRulesAndSharePolicy()
    {
        string body = SettingBody();
        GateAssert.HasSymbol(body, "RawFileRules",
            "AssetCollectionSetting 必须持有 RawFileRules 字段：RawFile 白名单是项目级规则，位于分类顺序的第二位");
        GateAssert.HasSymbol(body, "SharePolicy",
            "AssetCollectionSetting 必须持有 SharePolicy 字段：SharePolicyConfig 从 Package 级上移到 Setting，成为共享/禁共享策略的唯一来源");
    }

    private static void VerifySettingDropsGlobalAddressStyleAndKeepsIgnorePatterns()
    {
        string body = SettingBody();
        GateAssert.NoSymbol(body, "AddressStyle",
            "AssetCollectionSetting 不得持有全局 AddressStyle：地址样式改为逐资产 Details 操作");
        GateAssert.HasSymbol(body, "IgnorePatterns",
            "AssetCollectionSetting 必须保留 IgnorePatterns：全局排除规则仍是 Collection 输入");
    }

    /// <summary>采集器类型、资产角色和强制载荷三个旧枚举必须消失。</summary>
    private static void VerifyCollectorEnumsDropLegacyKinds()
    {
        // CollectorEnums.cs 可以并入其他文件，但旧枚举仍由整树断言拦截。
        if (!RepoSource.FileExists(CollectorEnumsPath))
            return;

        string code = RepoSource.ReadCode(CollectorEnumsPath);
        GateAssert.NoSymbol(code, "ECollectorType",
            "CollectorEnums.cs 不得再声明 ECollectorType：显式 Collector 收集的资源都是公共资源，不再需要采集器类型");
        GateAssert.NoSymbol(code, "EAssetRole",
            "CollectorEnums.cs 不得再声明 EAssetRole：资产角色语义被 Public 标记与依赖来源取代");
        GateAssert.NoSymbol(code, "EForcePayloadKind",
            "CollectorEnums.cs 不得再声明 EForcePayloadKind：逐资产载荷覆盖被取消，内容类型改由分类顺序决定");
    }

    /// <summary>Group 不再承担标签归属。</summary>
    private static void VerifyCollectionGroupDropsLabelsField()
    {
        string body = TypeBody(RepoSource.ReadCode(SettingPath), SettingFileName, "AssetCollectionGroup");
        GateAssert.NoSymbol(body, "Labels",
            "AssetCollectionGroup 不得再含 Labels 字段：Group 只控制显式资源打包，标签改由 AssetOverride 逐资产声明");
    }

    /// <summary>Collector 只剩路径与路径类型。</summary>
    private static void VerifyCollectorKeepsOnlyPathFields()
    {
        string body = TypeBody(RepoSource.ReadCode(SettingPath), SettingFileName, "Collector");
        GateAssert.HasSymbol(body, "CollectPath",
            "Collector 必须保留 CollectPath：采集路径仍是 Collector 的唯一输入");
        GateAssert.HasSymbol(body, "CollectPathType",
            "Collector 必须保留 CollectPathType：目录/单文件两种采集方式属于必须保留的等价行为");
        GateAssert.NoSymbol(body, "CollectorType",
            "Collector 不得再含 CollectorType：资产角色语义删除，显式收集即公共资源");
        GateAssert.NoSymbol(body, "ForcePayloadKind",
            "Collector 不得再含 ForcePayloadKind：逐资产载荷覆盖删除，内容类型由分类顺序决定");
        GateAssert.NoSymbol(body, "FilterRuleName",
            "Collector 不得再含 FilterRuleName：序列化规则名随反射规则体系一起删除");
        GateAssert.NoSymbol(body, "GroupRuleName",
            "Collector 不得再含 GroupRuleName：序列化规则名随反射规则体系一起删除");
    }

    private static void VerifyAssetAddressEntryTypeDeclaresAddressFields()
    {
        AssertTypeDeclaresMembers(AbRoot, "AssetAddressEntry",
            new[] { "AssetGUID", "Address", "Labels" },
            "目标类型 AssetAddressEntry 必须声明 AssetGUID/Address/Labels");
        GateAssert.TreeHasNoSymbol(AbRoot, "AssetOverride",
            "AssetOverride 旧类型必须从 AB 脚本树消失");
    }

    private static void VerifyRawFileRulesTypeDeclaresPatterns()
    {
        AssertTypeDeclaresMembers(AbRoot, "RawFileRules",
            new[] { "Patterns" },
            "目标类型 RawFileRules 只保留与 Ignore 共用 gitignore 语义的 Patterns 列表");
        string code = RepoSource.ReadCode("Assets/FYAsset/Scripts/AB/Build/Collector/RawFileRules.cs");
        GateAssert.NoSymbol(code, "Extensions", "RawFileRules 不得保留 Extensions 字段");
        GateAssert.NoSymbol(code, "FileNames", "RawFileRules 不得保留 FileNames 字段");
        GateAssert.NoSymbol(code, "Folders", "RawFileRules 不得保留 Folders 字段");
    }

    /// <summary>共享策略配置仍由 Setting 持有两个模式列表。</summary>
    private static void VerifySharePolicyConfigTypeDeclaresSharePatterns()
    {
        AssertTypeDeclaresMembers(AbRoot, "SharePolicyConfig",
            new[] { "ForceSharePatterns", "NoSharePatterns" },
            "目标类型 SharePolicyConfig 必须声明 ForceSharePatterns/NoSharePatterns：强制共享与禁止共享是共享决策的完整输入，同时命中应阻断构建");
    }

    /// <summary>反射规则体系的六个文件与承载目录必须删除。</summary>
    private static void VerifyReflectionRuleFilesAndDirectoryRemoved()
    {
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/RuleResolver.cs",
            "RuleResolver.cs 必须删除：计划取消按类名字符串反射解析过滤/分组规则的做法");
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules/CollectAll.cs",
            "Rules/CollectAll.cs 必须删除：默认收集行为回归扫描器本身，不再由规则类提供");
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules/GroupAll.cs",
            "Rules/GroupAll.cs 必须删除：分组行为回归 Group 配置本身，不再由规则类提供");
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules/Interfaces/IFilterRule.cs",
            "Rules/Interfaces/IFilterRule.cs 必须删除：过滤规则接口随反射体系整体移除");
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules/Interfaces/IGroupRule.cs",
            "Rules/Interfaces/IGroupRule.cs 必须删除：分组规则接口随反射体系整体移除");
        GateAssert.FileMissing("Assets/FYAsset/Scripts/AB/Build/Collector/Editor/UI/RuleDropdownHelper.cs",
            "UI/RuleDropdownHelper.cs 必须删除：规则下拉框是反射规则体系在编辑器侧的入口，随体系一起移除");
        GateAssert.DirectoryMissing(RulesDir,
            "Rules 目录必须删除：反射规则体系整体移除后不应留下承载目录");
    }

    /// <summary>反射规则体系的符号不得残留任何引用。</summary>
    private static void VerifyReflectionRuleSymbolsRemovedFromScripts()
    {
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "RuleResolver",
            "RuleResolver 符号必须从 Assets/FYAsset/Scripts 整树消失：反射规则解析被取消，任何残留引用都会让删除不彻底");
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "IFilterRule",
            "IFilterRule 符号必须从 Assets/FYAsset/Scripts 整树消失：过滤规则接口被取消");
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "IGroupRule",
            "IGroupRule 符号必须从 Assets/FYAsset/Scripts 整树消失：分组规则接口被取消");
    }

    /// <summary>旧配置层枚举与 Package 类型不得残留任何引用。</summary>
    private static void VerifyLegacyConfigSymbolsRemovedFromScripts()
    {
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "AssetCollectionPackage",
            "AssetCollectionPackage 必须从 Assets/FYAsset/Scripts 整树消失：Setting 直接持有 Groups，且计划明确不保留 Package 兼容壳");
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "ECollectorType",
            "ECollectorType 必须从 Assets/FYAsset/Scripts 整树消失：显式收集即公共资源，采集器类型语义删除");
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "EAssetRole",
            "EAssetRole 必须从 Assets/FYAsset/Scripts 整树消失：资产角色语义被 Public 标记与依赖来源取代");
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "EForcePayloadKind",
            "EForcePayloadKind 必须从 Assets/FYAsset/Scripts 整树消失：逐资产载荷覆盖删除，内容类型由分类顺序决定");
    }

    /// <summary>配置资产必须与目标结构同步，不残留旧 YAML 键。</summary>
    private static void VerifyCollectorSettingAssetDropsLegacyYamlKeys()
    {
        GateAssert.FileExists(SettingAssetPath,
            "配置资产 CollectorSetting.asset 必须存在：它是项目级 Collection 配置的唯一事实来源");
        string asset = RepoSource.Read(SettingAssetPath);
        GateAssert.NotContains(asset, "Packages:",
            "CollectorSetting.asset 不得再序列化 Packages 键：Package 配置层删除后配置资产必须同步重建，否则旧数据会被静默反序列化");
        GateAssert.NotContains(asset, "CollectorType:",
            "CollectorSetting.asset 不得再序列化 CollectorType 键：Collector 只保留路径字段，旧键会留下无主数据");
        GateAssert.NotContains(asset, "ForcePayloadKind:",
            "CollectorSetting.asset 不得再序列化 ForcePayloadKind 键：逐资产载荷覆盖已删除");
        GateAssert.NotContains(asset, "FilterRuleName:",
            "CollectorSetting.asset 不得再序列化 FilterRuleName 键：序列化规则名随反射体系删除");
        GateAssert.NotContains(asset, "GroupRuleName:",
            "CollectorSetting.asset 不得再序列化 GroupRuleName 键：序列化规则名随反射体系删除");
        GateAssert.NotContains(asset, "Role:",
            "CollectorSetting.asset 不得再序列化 Role 键：资产角色语义已删除");
        GateAssert.NotContains(asset, "PayloadKind:",
            "CollectorSetting.asset 不得再序列化 PayloadKind 键：载荷类型被 AssetContentType 取代");
    }

    // ---- 取值与定位辅助 ----

    private static string SettingBody() =>
        TypeBody(RepoSource.ReadCode(SettingPath), SettingFileName, "AssetCollectionSetting");

    /// <summary>
    /// 取出净化源码中指定类型声明的类体（第一个 '{' 到配对 '}'）。
    /// 目标契约按“某个类型必须/不得含某字段”表述，因此判断限定在该类型声明内部，
    /// 避免同文件其他类型的同名成员造成误判。
    /// </summary>
    private static string TypeBody(string code, string fileName, string typeName)
    {
        Match declaration = Regex.Match(code, @"\b(?:class|struct|enum|interface)\s+" + Regex.Escape(typeName) + @"\b");
        GateAssert.True(declaration.Success,
            $"{fileName} 必须声明类型 {typeName}：目标是让该配置类型承载对应字段");

        int open = code.IndexOf('{', declaration.Index);
        GateAssert.True(open >= 0, $"{fileName} 中类型 {typeName} 的声明缺少类型体起始 '{{'");

        int close = MatchBrace(code, open);
        GateAssert.True(close > open, $"{fileName} 中类型 {typeName} 的类型体花括号不配对，无法定位目标字段");
        return code.Substring(open, close - open + 1);
    }

    /// <summary>返回与 openIndex 处 '{' 配对的 '}' 下标；不配对时返回 -1。</summary>
    private static int MatchBrace(string code, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 断言目录树中存在声明指定类型的文件，且该类型体同时包含全部指定成员。
    /// 先定位声明文件再检查，避免同名成员出现在其他类型里造成误判。
    /// </summary>
    private static void AssertTypeDeclaresMembers(string root, string typeName, string[] members, string purpose)
    {
        var candidates = RepoSource.FilesContaining(root, typeName);
        GateAssert.True(candidates.Count > 0, $"{purpose}（{root} 内未出现类型名 {typeName}）");

        for (int i = 0; i < candidates.Count; i++)
        {
            string code = RepoSource.ReadCode(candidates[i]);
            if (!Regex.IsMatch(code, @"\b(?:class|struct|enum|interface)\s+" + Regex.Escape(typeName) + @"\b"))
                continue;

            string body = TypeBody(code, candidates[i], typeName);
            bool complete = true;
            for (int j = 0; j < members.Length; j++)
                complete &= RepoSource.HasToken(body, members[j]);

            if (complete)
                return;
        }

        GateAssert.True(false,
            $"{purpose}（{root} 中声明 {typeName} 的类型体未同时包含成员 {string.Join("/", members)}；候选文件: {string.Join(", ", candidates)}）");
    }
}

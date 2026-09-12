using System;

/// <summary>
/// T4「ABManifest 与验证」目标契约门禁（计划 Code Change Map T4 与 Target Data Model - ABManifest）。
/// 目标结构：ManifestBundleEntry 改为 ManifestContentEntry（承载 Bundle 与 RawFile 的统一文件事实）；
/// ABManifest 用 PackageVersion/AssetEntries/ContentEntries 描述一次构建；
/// ManifestAssetEntry 用 EntryId/Address/PrimaryType/Labels/SourcePath/IsPublic/ContentType/ContentIndex 描述资产；
/// 删除 Manifest 自 Hash、DeliveryBundles、Group、AutoAddress、BundleIndex、PayloadKind 与写包 Manifest 的独立 Task，
/// 并同步重建 Compat 序列化生成代码。
/// </summary>
internal static class ManifestContractTests
{
    private const string ScriptsRoot = "Assets/FYAsset/Scripts";
    private const string ManifestPath = "Assets/FYAsset/Scripts/AB/Runtime/Manifests/ABManifest.cs";
    private const string ManifestAssetEntryPath = "Assets/FYAsset/Scripts/AB/Runtime/Manifests/ManifestAssetEntry.cs";
    private const string ManifestContentEntryPath = "Assets/FYAsset/Scripts/AB/Runtime/Manifests/ManifestContentEntry.cs";
    private const string ManifestBundleEntryPath = "Assets/FYAsset/Scripts/AB/Runtime/Manifests/ManifestBundleEntry.cs";
    private const string WritePackageManifestTaskPath = "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskWriteABPackageManifest.cs";
    private const string ContentEntrySerializerPath = "Assets/FYAsset/Scripts/Compat/Serialization/Generated/ManifestContentEntry_BinarySerializer.cs";
    private const string BundleEntrySerializerPath = "Assets/FYAsset/Scripts/Compat/Serialization/Generated/ManifestBundleEntry_BinarySerializer.cs";

    public static void Run()
    {
        GateChecks.RunAll(
            ("ManifestContentEntryTypeExists", VerifyManifestContentEntryTypeExists),
            ("ManifestBundleEntryTypeRemoved", VerifyManifestBundleEntryTypeRemoved),
            ("ManifestDeclaresTargetEntries", VerifyManifestDeclaresTargetEntries),
            ("ManifestAssetEntryDeclaresTargetFields", VerifyManifestAssetEntryDeclaresTargetFields),
            ("ManifestAssetEntryDropsLegacyFields", VerifyManifestAssetEntryDropsLegacyFields),
            ("ManifestContentEntryDeclaresFileFacts", VerifyManifestContentEntryDeclaresFileFacts),
            ("ManifestBundleEntrySymbolRemovedFromScripts", VerifyManifestBundleEntrySymbolRemovedFromScripts),
            ("WritePackageManifestTaskRemoved", VerifyWritePackageManifestTaskRemoved),
            ("GeneratedSerializersFollowManifestTypes", VerifyGeneratedSerializersFollowManifestTypes));
    }

    /// <summary>内容条目类型必须存在，Bundle 条目类型文件必须删除。</summary>
    private static void VerifyManifestContentEntryTypeExists()
    {
        GateAssert.FileExists(ManifestContentEntryPath,
            "ManifestContentEntry.cs 必须存在：Bundle 与 RawFile 产出统一的 Content 条目，承载可下载文件的名称、Hash、CRC、大小与依赖下标");
    }

    private static void VerifyManifestBundleEntryTypeRemoved()
    {
        GateAssert.FileMissing(ManifestBundleEntryPath,
            "ManifestBundleEntry.cs 必须删除：Bundle 条目语义被 Content 条目取代，RawFile 是普通物理文件而非 Bundle");
    }

    /// <summary>Manifest 顶层字段收敛为版本、资产条目与内容条目。</summary>
    private static void VerifyManifestDeclaresTargetEntries()
    {
        string code = RepoSource.ReadCode(ManifestPath);
        GateAssert.HasSymbol(code, "PackageVersion",
            "ABManifest 必须保留 PackageVersion：同 Major 前向更新与版本核对以 Manifest 版本为准");
        GateAssert.HasSymbol(code, "AssetEntries",
            "ABManifest 必须保留 AssetEntries：资产条目是运行时按 Address/EntryId 查询的唯一入口");
        GateAssert.HasSymbol(code, "ContentEntries",
            "ABManifest 必须含 ContentEntries：原 BundleEntries 改为 ContentEntries，承载可下载内容（Bundle 与 RawFile）的完整文件集合");
        GateAssert.NoSymbol(code, "BundleEntries",
            "ABManifest 不得再含 BundleEntries：Bundle 条目改为 ContentEntries，RawFile 不伪装成 Bundle");
        GateAssert.NoSymbol(code, "DeliveryBundles",
            "ABManifest 不得再含 DeliveryBundles：交付变化由 Manifest 差异比较得出，不再自描述交付集合");
        GateAssert.NoSymbol(code, "PackageName",
            "ABManifest 不得再含 PackageName：多包支持待定，当前不保留 Package 兼容壳");
        GateAssert.NoSymbol(code, "BuildTimestamp",
            "ABManifest 不得再含 BuildTimestamp：构建时间属于构建摘要，Manifest 只描述可交付内容");
        GateAssert.NoSymbol(code, "FileHash",
            "ABManifest 不得再含自引用 FileHash：清单自 Hash 会让内容与自身循环依赖，文件 Hash 只存在于 Content 条目");
    }

    /// <summary>资产条目的目标字段。</summary>
    private static void VerifyManifestAssetEntryDeclaresTargetFields()
    {
        string code = RepoSource.ReadCode(ManifestAssetEntryPath);
        GateAssert.HasSymbol(code, "EntryId",
            "ManifestAssetEntry 必须保留 EntryId：它是句柄身份与缓存键的权威标识");
        GateAssert.HasSymbol(code, "Address",
            "ManifestAssetEntry 必须保留 Address：非加载查询只返回 Address");
        GateAssert.HasSymbol(code, "PrimaryType",
            "ManifestAssetEntry 必须保留 PrimaryType：运行时按类型查询需要主类型名");
        GateAssert.HasSymbol(code, "Labels",
            "ManifestAssetEntry 必须保留 Labels：按标签查询需要条目自带标签");
        GateAssert.HasSymbol(code, "SourcePath",
            "ManifestAssetEntry 必须保留 SourcePath：加载时仍需工程路径作为回退来源");
        GateAssert.HasSymbol(code, "IsPublic",
            "ManifestAssetEntry 必须含 IsPublic：显式收集的资源是公共资源，隐式依赖不得进入公共查询索引");
        GateAssert.HasSymbol(code, "ContentType",
            "ManifestAssetEntry 必须含 ContentType：运行时据此选择 SerializedObject/Scene/RawFile 的加载路径");
        GateAssert.HasSymbol(code, "ContentIndex",
            "ManifestAssetEntry 必须含 ContentIndex：资产条目通过下标指向 Content 条目，形成一对多映射");
    }

    /// <summary>资产条目的旧字段必须删除。</summary>
    private static void VerifyManifestAssetEntryDropsLegacyFields()
    {
        string code = RepoSource.ReadCode(ManifestAssetEntryPath);
        GateAssert.NoSymbol(code, "Group",
            "ManifestAssetEntry 不得再含 Group：Group 只控制显式资源打包，不是运行时查询过滤条件");
        GateAssert.NoSymbol(code, "AutoAddress",
            "ManifestAssetEntry 不得再含 AutoAddress：Address 是否自动生成属于构建期信息，运行时不需要区分");
        GateAssert.NoSymbol(code, "BundleIndex",
            "ManifestAssetEntry 不得再含 BundleIndex：资产指向 Content 条目下标，Bundle 与 RawFile 统一为内容");
        GateAssert.NoSymbol(code, "PayloadKind",
            "ManifestAssetEntry 不得再含 PayloadKind：载荷类型被 ContentType 取代");
    }

    /// <summary>内容条目必须完整描述物理文件事实。</summary>
    private static void VerifyManifestContentEntryDeclaresFileFacts()
    {
        GateAssert.FileExists(ManifestContentEntryPath,
            "ManifestContentEntry.cs 必须存在：内容条目是可下载文件的唯一事实来源");
        string code = RepoSource.ReadCode(ManifestContentEntryPath);
        GateAssert.HasSymbol(code, "FileName",
            "ManifestContentEntry 必须含 FileName：下载与本地查重以文件名为键");
        GateAssert.HasSymbol(code, "FileHash",
            "ManifestContentEntry 必须含 FileHash：Hash 相同即可复用本地文件，是下载优化的依据");
        GateAssert.HasSymbol(code, "FileCRC",
            "ManifestContentEntry 必须含 FileCRC：下载与复用文件需要 CRC 校验");
        GateAssert.HasSymbol(code, "FileSize",
            "ManifestContentEntry 必须含 FileSize：下载与交付需要文件长度");
        GateAssert.HasSymbol(code, "ContentType",
            "ManifestContentEntry 必须含 ContentType：Bundle 与 RawFile 的加载和交付方式不同，必须可区分");
        GateAssert.HasSymbol(code, "DependencyIndices",
            "ManifestContentEntry 必须含 DependencyIndices：最终依赖来自 Unity 构建后的 AssetBundleManifest，运行时只消费内容级依赖下标");
    }

    /// <summary>旧 Bundle 条目符号不得残留任何引用。</summary>
    private static void VerifyManifestBundleEntrySymbolRemovedFromScripts()
    {
        GateAssert.TreeHasNoSymbol(ScriptsRoot, "ManifestBundleEntry",
            "ManifestBundleEntry 必须从 Assets/FYAsset/Scripts 整树消失：运行时、构建、热更与编辑器预览都必须改为消费 Content 条目");
    }

    /// <summary>写包 Manifest 的独立 Task 职责并入 Generate/Export。</summary>
    private static void VerifyWritePackageManifestTaskRemoved()
    {
        GateAssert.FileMissing(WritePackageManifestTaskPath,
            "TaskWriteABPackageManifest.cs 必须删除：写包 Manifest 的职责并入 TaskGenerateManifest 与导出环节，避免两份 Manifest 写入逻辑");
    }

    /// <summary>Compat 序列化生成代码随 Manifest 类型重建。</summary>
    private static void VerifyGeneratedSerializersFollowManifestTypes()
    {
        GateAssert.FileExists(ContentEntrySerializerPath,
            "ManifestContentEntry_BinarySerializer.cs 必须存在：二进制序列化模型与生成代码必须同步重建，否则无法写出 Content 条目");
        GateAssert.FileMissing(BundleEntrySerializerPath,
            "ManifestBundleEntry_BinarySerializer.cs 必须删除：类型改名后旧生成器会引用不存在的类型，且计划明确不兼容旧数据");
    }
}

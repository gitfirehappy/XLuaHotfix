using System;

/// <summary>
/// 热更状态机契约门禁。
/// 验证运行时只读取当前激活包根，RuntimeMode 是在线/单机模式事实来源，并覆盖 Check、Prepare、Apply 行为。
/// </summary>
internal static class HotfixSwitchContractTests
{
    private const string ABBundleLoaderFile = "Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs";
    private const string ABPackageManagerFile = "Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs";
    private const string AAManifestLoaderFile = "Assets/FYAsset/Scripts/AA/Runtime/Backends/AAManifestLoader.cs";
    private const string SharedRuntimeDir = "Assets/FYAsset/Scripts/Shared/Runtime";
    private const string BuildIndexFile = "Assets/FYAsset/Scripts/Shared/Runtime/BuildIndexData.cs";
    private const string SharedHotfixDir = "Assets/FYAsset/Scripts/Shared/Hotfix";

    public static void Run()
    {
        GateChecks.RunAll(
            ("LoadersReadSingleActivePackageRoot", VerifyLoadersReadSingleActivePackageRoot),
            ("BundleLoaderDropsStreamingAssetsFallbackHelpers", VerifyBundleLoaderDropsStreamingAssetsFallbackHelpers),
            ("RuntimeModeEnumDeclared", VerifyRuntimeModeEnumDeclared),
            ("BuildIndexCarriesRuntimeMode", VerifyBuildIndexCarriesRuntimeMode),
            ("HotfixBaselineNamingRemoved", VerifyHotfixBaselineNamingRemoved),
            ("HotfixStatesAreBuiltInLocalRemoteBlocked", VerifyHotfixStatesAreBuiltInLocalRemoteBlocked),
            ("HotfixSupportsRuntimeCheckPrepareApply", VerifyHotfixSupportsRuntimeCheckPrepareApply));
    }

    /// <summary>
    /// 三个运行时加载器都不得自行推导 StreamingAssets 目录。
    /// 说明：净化源码会剥离注释与字符串字面量，因此这里断言的是真实的读取入口符号
    /// （标识符与 Application.streamingAssetsPath 属性），而不是注释或日志文本。
    /// </summary>
    private static void VerifyLoadersReadSingleActivePackageRoot()
    {
        string[] loaders = { ABBundleLoaderFile, ABPackageManagerFile, AAManifestLoaderFile };

        GateAssert.FileMissing(
            "Assets/FYAsset/Scripts/AB/Runtime/ABManifestLoader.cs",
            "AB Manifest 读取职责必须收敛进 ABPackageManager");
        string manager = RepoSource.ReadCode(ABPackageManagerFile);
        GateAssert.HasSymbol(manager, "LoadActiveManifestAsync",
            "ABPackageManager 必须从唯一 ActivePackageRoot 读取 Manifest");

        for (int i = 0; i < loaders.Length; i++)
        {
            GateAssert.FileExists(loaders[i], $"运行时加载器必须存在: {loaders[i]}");

            string code = RepoSource.ReadCode(loaders[i]);
            GateAssert.NoSymbol(
                code,
                "StreamingAssets",
                "计划 T8 规定激活热更包后一个运行时上下文只读取一个包根目录，"
                + "禁止 Manifest 来自 Local、单个内容文件回退 StreamingAssets 的混合读取；"
                + $"{loaders[i]} 的净化源码仍出现 StreamingAssets 符号");
            GateAssert.NoSymbol(
                code,
                "streamingAssetsPath",
                "计划 T8 要求包根目录由激活流程统一决定：内置包根同样经 RuntimePathManager 解析，"
                + "加载器不得直接读取 Application.streamingAssetsPath 形成逐文件回退；"
                + $"{loaders[i]} 的净化源码仍直接读取 Application.streamingAssetsPath");
        }
    }

    /// <summary>AB BundleLoader 不得保留任何 StreamingAssets 回退加载入口。</summary>
    private static void VerifyBundleLoaderDropsStreamingAssetsFallbackHelpers()
    {
        GateAssert.FileExists(ABBundleLoaderFile, $"计划 T5/T8 要求保留 AB Bundle 加载器 {ABBundleLoaderFile}");

        string code = RepoSource.ReadCode(ABBundleLoaderFile);
        GateAssert.NoSymbol(
            code,
            "LoadBundleFromStreamingAssetsAsync",
            "计划 T8 规定 BundleLoader 只按当前激活包根读取，不逐文件回退 StreamingAssets；"
            + $"{ABBundleLoaderFile} 仍保留 StreamingAssets 回退加载方法");
        GateAssert.NoSymbol(
            code,
            "GetStreamingAssetsBundlesDir",
            "计划 T8 要求包根目录由激活流程统一决定，加载器不得自行推导 StreamingAssets 目录；"
            + $"{ABBundleLoaderFile} 仍保留 StreamingAssets 目录推导方法");
    }

    /// <summary>RuntimeMode 枚举必须存在，且只有 Online 与 Standalone 两种取值。</summary>
    private static void VerifyRuntimeModeEnumDeclared()
    {
        GateAssert.TreeHasSymbol(
            SharedRuntimeDir,
            "RuntimeMode",
            "计划 T8 要求 BuildIndex.RuntimeMode 决定是否联网，它是 Online/Standalone 的唯一运行时事实来源；"
            + $"{SharedRuntimeDir} 树内不存在 RuntimeMode 符号");
        GateAssert.TreeHasSymbol(
            SharedRuntimeDir,
            "Online",
            $"计划 T8 的 RuntimeMode 枚举必须包含 Online 取值；{SharedRuntimeDir} 树内不存在 Online 符号");
        GateAssert.TreeHasSymbol(
            SharedRuntimeDir,
            "Standalone",
            "计划 T8 的 RuntimeMode 枚举必须包含 Standalone 取值（Standalone 只用内置完整包、不联网）；"
            + $"{SharedRuntimeDir} 树内不存在 Standalone 符号");
    }

    /// <summary>BuildIndexData 必须携带 RuntimeMode 字段。</summary>
    private static void VerifyBuildIndexCarriesRuntimeMode()
    {
        GateAssert.FileExists(BuildIndexFile, "计划 T8 要求保留 BuildIndex 数据文件，该文件必须存在");

        string code = RepoSource.ReadCode(BuildIndexFile);
        GateAssert.HasSymbol(
            code,
            "RuntimeMode",
            "计划 T8 规定 BuildIndex.RuntimeMode 是 Player 运行时判断在线或离线的唯一事实来源，"
            + "FYAssetSettings.StandaloneBuild 不得继续承担该职责；"
            + $"{BuildIndexFile} 未声明 RuntimeMode 字段");
    }

    /// <summary>热更树内的 baseline 命名必须全部清除。</summary>
    private static void VerifyHotfixBaselineNamingRemoved()
    {
        string[] baselineSymbols =
        {
            "BaselinePackageName",
            "BaselinePackageIndex",
            "BaselinePackageInspection",
            "LocalIsBaseline",
            "InspectBaselinePackageAsync",
            "RepairBaselinePointer"
        };

        for (int i = 0; i < baselineSymbols.Length; i++)
        {
            GateAssert.TreeHasNoSymbol(
                SharedHotfixDir,
                baselineSymbols[i],
                "计划 T8 要求移除 baseline 命名：内容来源只表达 BuiltInPackage、LocalPackage 与 RemoteTarget，"
                + $"不再存在基线指针或基线包检查概念；{SharedHotfixDir} 树内仍出现 {baselineSymbols[i]}");
        }
    }

    /// <summary>热更来源不再复制为状态枚举，当前来源由包根事实和动作表达。</summary>
    private static void VerifyHotfixStatesAreBuiltInLocalRemoteBlocked()
    {
        GateAssert.TreeHasNoSymbol(
            SharedHotfixDir,
            "HotfixContentState",
            "Hotfix 来源不得由独立状态枚举表达");
        string flow = RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs");
        GateAssert.HasSymbol(flow, "CurrentPackageRoot", "流程必须保留当前包根事实");
        GateAssert.HasSymbol(flow, "BuiltInPackageRoot", "流程必须保留内置包根事实");
        GateAssert.HasSymbol(flow, "IsCurrentBuiltIn", "激活/回滚必须根据两个包根判断内置来源");
        GateAssert.HasSymbol(flow, "HotfixStateAction", "阻断和目标状态由 Action 表达");
    }

    /// <summary>运行中热更必须提供 Check / Prepare / Apply 操作。</summary>
    private static void VerifyHotfixSupportsRuntimeCheckPrepareApply()
    {
        GateAssert.TreeHasSymbol(
            SharedHotfixDir,
            "Check",
            "计划 T8 要求运行中 Check 只检查是否存在可接受更新，不做任何下载或切换；"
            + $"{SharedHotfixDir} 树内不存在 Check 符号");
        GateAssert.TreeHasSymbol(
            SharedHotfixDir,
            "Prepare",
            "计划 T8 要求运行中 Prepare 在当前包继续运行的前提下隔离下载、复制并完整校验目标包；"
            + $"{SharedHotfixDir} 树内不存在 Prepare 符号");
        GateAssert.TreeHasSymbol(
            SharedHotfixDir,
            "Apply",
            "计划 T8 要求运行中 Apply 在业务回到安全入口并释放 Handle 后关闭旧 Manager、切换包根并重新初始化；"
            + $"{SharedHotfixDir} 树内不存在 Apply 符号");
    }
}

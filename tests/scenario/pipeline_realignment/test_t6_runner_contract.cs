using System;

/// <summary>
/// 构建管线 Runner / Composer 与 AA、AB 固定主干契约门禁（计划 T6）。
/// 目标：Runner 只保留 Run 入口并执行 Composer 产出的 Task 列表，不再提供 whitelist、
/// stop-after、可编辑主干，也不承担 PackageIndex、baseline、发布和版本回滚职责；
/// AA 收敛为 5 个固定阶段、AB 收敛为 6 个固定阶段，每个阶段之间允许 0..N 个自定义 Task；
/// 被并入 Runner 或移出构建的旧 Task 文件必须删除。
/// </summary>
internal static class RunnerContractTests
{
    private const string PipelineEditorDir = "Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor";
    private const string RunnerFile = PipelineEditorDir + "/BuildPipelineRunner.cs";
    private const string PrepareContextTaskFile =
        "Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs";
    private const string PackageIndexTaskFile =
        "Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/Tasks/TaskWritePackageIndex.cs";
    private const string AABackboneFile = "Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/AAPipelineBackbone.cs";
    private const string ABBackboneFile = "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/ABPipelineBackbone.cs";
    private const string ABPackageManifestTaskFile =
        "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskWriteABPackageManifest.cs";
    private const string ABHotfixDiffTaskFile =
        "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskScanABHotfixDiff.cs";
    private const string SharedBuildDir = "Assets/FYAsset/Scripts/Shared/Build";

    public static void Run()
    {
        GateChecks.RunAll(
            ("RunnerExposesRunEntry", VerifyRunnerExposesRunEntry),
            ("RunnerDropsExecuteEntry", VerifyRunnerDropsExecuteEntry),
            ("RunnerDropsWhitelistAndStopAfter", VerifyRunnerDropsWhitelistAndStopAfter),
            ("ComposerTypeDeclared", VerifyComposerTypeDeclared),
            ("CustomTaskEntryCarriesSlotAndTaskName", VerifyCustomTaskEntryCarriesSlotAndTaskName),
            ("CoreTaskSlotTypeDeclared", VerifyCoreTaskSlotTypeDeclared),
            ("AABackboneHasFiveFixedStages", VerifyAABackboneHasFiveFixedStages),
            ("AABackboneDropsLegacyTasks", VerifyAABackboneDropsLegacyTasks),
            ("ABBackboneHasSixFixedStages", VerifyABBackboneHasSixFixedStages),
            ("ABBackboneDropsLegacyTasks", VerifyABBackboneDropsLegacyTasks),
            ("PrepareContextTaskFileRemoved", VerifyPrepareContextTaskFileRemoved),
            ("PackageIndexTaskFileRemoved", VerifyPackageIndexTaskFileRemoved),
            ("ABPackageManifestTaskFileRemoved", VerifyABPackageManifestTaskFileRemoved),
            ("ABHotfixDiffTaskFileRemoved", VerifyABHotfixDiffTaskFileRemoved),
            ("CompleteBuildSummaryTypeDeclared", VerifyCompleteBuildSummaryTypeDeclared));
    }

    /// <summary>Runner 的唯一入口是 Run(request, tasks)，不再暴露配置驱动的 Execute 重载。</summary>
    private static void VerifyRunnerExposesRunEntry()
    {
        GateAssert.FileExists(RunnerFile, "计划 T6 保留 Shared 构建 Runner 作为唯一构建入口，该文件必须存在");

        string code = RepoSource.ReadCode(RunnerFile);
        GateAssert.HasSymbol(
            code,
            "Run",
            "计划 T6 规定 BuildPipelineRunner.Run(BuildRequest, IReadOnlyList<IBuildTask>) 是唯一构建入口，"
            + $"负责创建 Context/attempt、顺序执行并提升正式输出；{RunnerFile} 中不存在 Run 符号");
    }

    /// <summary>
    /// 旧配置驱动入口必须消失：Runner 不再自行读取配置资产并裁剪 Task 列表。
    /// 说明：Runner 调用 <c>IBuildTask.Execute</c> 是正常执行行为，因此这里不禁止 Execute 词元，
    /// 而是禁止 Runner 认识 BuildPipelineConfig（配置只由 Composer 消费）与旧的 ExecuteInternal 实现。
    /// </summary>
    private static void VerifyRunnerDropsExecuteEntry()
    {
        GateAssert.FileExists(RunnerFile, "计划 T6 保留 Shared 构建 Runner 作为唯一构建入口，该文件必须存在");

        string code = RepoSource.ReadCode(RunnerFile);
        GateAssert.NoSymbol(
            code,
            "BuildPipelineConfig",
            "计划 T6 删除 Execute(config, stopAfter, whitelist, expectedBackboneTasks) 系列入口，"
            + "Task 集合只能由 AA/AB Composer 从固定主干与自定义槽位产出后交给 Runner.Run；"
            + $"{RunnerFile} 仍引用 BuildPipelineConfig，说明 Runner 仍在自行解析配置列表");
        GateAssert.NoSymbol(
            code,
            "ExecuteInternal",
            "计划 T6 的 Runner 只保留单一 Run 执行路径，不再存在带 whitelist/stop-after 的内部执行实现；"
            + $"{RunnerFile} 仍存在 ExecuteInternal");
    }

    /// <summary>Runner 不提供 whitelist、stop-after，也不做主干成员校验。</summary>
    private static void VerifyRunnerDropsWhitelistAndStopAfter()
    {
        GateAssert.FileExists(RunnerFile, "计划 T6 保留 Shared 构建 Runner 作为唯一构建入口，该文件必须存在");

        string code = RepoSource.ReadCode(RunnerFile);
        GateAssert.NoSymbol(
            code,
            "taskWhitelist",
            "计划 T6 明确 Runner 不提供 whitelist，Task 集合只能来自 Composer；"
            + $"{RunnerFile} 仍存在 taskWhitelist，生产管线仍可被运行时裁剪");
        GateAssert.NoSymbol(
            code,
            "stopAfterTaskName",
            "计划 T6 明确 Runner 不提供 stop-after，预览必须直接调用无副作用的扫描、Compose 或 Diff 服务，"
            + $"不得通过截断生产管线实现；{RunnerFile} 仍存在 stopAfterTaskName");
        GateAssert.NoSymbol(
            code,
            "expectedBackboneTasks",
            "计划 T6 的主干由 AA/AB Composer 固定定义，不由 Runner 按配置成员校验；"
            + $"{RunnerFile} 仍存在 expectedBackboneTasks，说明主干仍可由配置资产增删");
    }

    /// <summary>Composer 类型必须存在：由 AA/AB 各自定义主干与合法插入位置。</summary>
    private static void VerifyComposerTypeDeclared()
    {
        GateAssert.TreeHasSymbol(
            PipelineEditorDir,
            "BuildPipelineComposer",
            "计划 T6 新增 BuildPipelineComposer.Compose(coreTasks, customTasks)，"
            + "由 AA/AB 各自定义固定主干和合法插入位置；"
            + $"{PipelineEditorDir} 树内不存在该类型");
    }

    /// <summary>自定义 Task 的插入位置必须由 CustomTaskEntry(Slot, TaskName) 表达。</summary>
    private static void VerifyCustomTaskEntryCarriesSlotAndTaskName()
    {
        var hits = RepoSource.FilesContaining(PipelineEditorDir, "CustomTaskEntry");
        GateAssert.True(
            hits.Count > 0,
            "计划 T6 规定自定义 Task 通过 CustomTaskEntry(Slot, TaskName) 声明插入位置，每个位置允许 0..N 个；"
            + $"{PipelineEditorDir} 树内不存在 CustomTaskEntry 类型");

        for (int i = 0; i < hits.Count; i++)
        {
            string code = RepoSource.ReadCode(hits[i]);
            GateAssert.HasSymbol(
                code,
                "Slot",
                $"计划 T6 要求 {hits[i]} 中的 CustomTaskEntry 携带 Slot 字段，用于标识插入到主干哪个阶段之后");
            GateAssert.HasSymbol(
                code,
                "TaskName",
                $"计划 T6 要求 {hits[i]} 中的 CustomTaskEntry 携带 TaskName 字段，用于解析自定义 Task 实现");
        }
    }

    /// <summary>主干槽位类型必须存在：Compose 以 CoreTaskSlot 描述固定阶段序列。</summary>
    private static void VerifyCoreTaskSlotTypeDeclared()
    {
        GateAssert.TreeHasSymbol(
            PipelineEditorDir,
            "CoreTaskSlot",
            "计划 T6 的 BuildPipelineComposer.Compose 接收 IReadOnlyList<CoreTaskSlot> 描述固定主干；"
            + $"{PipelineEditorDir} 树内不存在 CoreTaskSlot 类型");
    }

    /// <summary>AA 主干收敛为 5 个固定阶段。</summary>
    private static void VerifyAABackboneHasFiveFixedStages()
    {
        GateAssert.FileExists(AABackboneFile, "计划 T6 要求 AA 保留固定主干定义文件，该文件必须存在");

        string code = RepoSource.ReadCode(AABackboneFile);
        string[] stages =
        {
            "PrepareAAInput",
            "BuildAAContent",
            "GenerateAAManifest",
            "VerifyAAContent",
            "ExportAAOutput"
        };

        for (int i = 0; i < stages.Length; i++)
        {
            GateAssert.HasSymbol(
                code,
                stages[i],
                "计划 T6 规定 AA 主干固定为 PrepareAAInput → BuildAAContent → GenerateAAManifest "
                + $"→ VerifyAAContent → ExportAAOutput 五段；{AABackboneFile} 缺少阶段 {stages[i]}");
        }
    }

    /// <summary>AA 主干不得再引用被吸收或移出的旧 Task。</summary>
    private static void VerifyAABackboneDropsLegacyTasks()
    {
        GateAssert.FileExists(AABackboneFile, "计划 T6 要求 AA 保留固定主干定义文件，该文件必须存在");

        string code = RepoSource.ReadCode(AABackboneFile);
        string[] legacyTasks =
        {
            "TaskPrepareContext",
            "TaskMoveAAHotfixGroups",
            "TaskScanAAHotfixDiff",
            "TaskWriteAAPackageManifest",
            "TaskOrganizeAAOutput",
            "TaskWritePackageIndex"
        };

        for (int i = 0; i < legacyTasks.Length; i++)
        {
            GateAssert.NoSymbol(
                code,
                legacyTasks[i],
                "计划 T6 要求 PrepareContext 并入 Runner、AA Hotfix 资源选择并入 PrepareAAInput、"
                + "构建结果与 BuildIndex 归 ExportAAOutput、PackageIndex 移出构建；"
                + $"主干不得再引用旧 Task {legacyTasks[i]}（{AABackboneFile}）");
        }
    }

    /// <summary>AB 主干收敛为 6 个固定阶段。</summary>
    private static void VerifyABBackboneHasSixFixedStages()
    {
        GateAssert.FileExists(ABBackboneFile, "计划 T6 要求 AB 保留固定主干定义文件，该文件必须存在");

        string code = RepoSource.ReadCode(ABBackboneFile);
        string[] stages =
        {
            "CollectABAssets",
            "AnalyzeABDependencies",
            "BuildABContent",
            "GenerateABManifest",
            "VerifyABContent",
            "ExportABOutput"
        };

        for (int i = 0; i < stages.Length; i++)
        {
            GateAssert.HasSymbol(
                code,
                stages[i],
                "计划 T6 规定 AB 主干固定为 CollectABAssets → AnalyzeABDependencies → BuildABContent "
                + $"→ GenerateABManifest → VerifyABContent → ExportABOutput 六段；{ABBackboneFile} 缺少阶段 {stages[i]}");
        }
    }

    /// <summary>AB 主干不得再引用被吸收或移出的旧 Task。</summary>
    private static void VerifyABBackboneDropsLegacyTasks()
    {
        GateAssert.FileExists(ABBackboneFile, "计划 T6 要求 AB 保留固定主干定义文件，该文件必须存在");

        string code = RepoSource.ReadCode(ABBackboneFile);
        string[] legacyTasks =
        {
            "TaskPrepareContext",
            "TaskCollectAssets",
            "TaskCollectBuiltins",
            "TaskAnalyzeDependencies",
            "TaskBuildBundles",
            "TaskGenerateManifest",
            "TaskVerifyBuildResult",
            "TaskScanABHotfixDiff",
            "TaskOrganizeOutput",
            "TaskWriteABPackageManifest",
            "TaskWritePackageIndex",
            "TaskExportLocalBuildData"
        };

        for (int i = 0; i < legacyTasks.Length; i++)
        {
            GateAssert.NoSymbol(
                code,
                legacyTasks[i],
                "计划 T6 要求 AB 六个阶段各自吸收旧 Task 职责（收集、依赖、内容、Manifest、校验、导出），"
                + "Hotfix 差异收归 Export/Diff、PackageIndex 移出构建；"
                + $"主干不得再引用旧 Task {legacyTasks[i]}（{ABBackboneFile}）");
        }
    }

    /// <summary>PrepareContext 并入 Runner，独立 Task 文件必须删除。</summary>
    private static void VerifyPrepareContextTaskFileRemoved()
    {
        GateAssert.FileMissing(
            PrepareContextTaskFile,
            "计划 T6 规定 Runner 创建 Context 与 attempt，PrepareContext 职责并入 Runner，"
            + "独立 Task 文件必须删除");
    }

    /// <summary>PackageIndex 移出构建，写入 Task 文件必须删除。</summary>
    private static void VerifyPackageIndexTaskFileRemoved()
    {
        GateAssert.FileMissing(
            PackageIndexTaskFile,
            "计划 T6/T7 规定 BuildRunner 不写 PackageIndex，PackageIndex 由 Publisher 在新隔离目录"
            + "组装校验完成后最后生成并上传，构建期写入 Task 必须删除");
    }

    /// <summary>AB Package Manifest 写入并入 Export 阶段，独立 Task 文件必须删除。</summary>
    private static void VerifyABPackageManifestTaskFileRemoved()
    {
        GateAssert.FileMissing(
            ABPackageManifestTaskFile,
            "计划 T6/T7 规定 AB Package Manifest 逻辑收归 GenerateABManifest/ExportABOutput，"
            + "独立写入 Task 必须删除");
    }

    /// <summary>AB Hotfix 差异扫描移出核心主干，独立 Task 文件必须删除。</summary>
    private static void VerifyABHotfixDiffTaskFileRemoved()
    {
        GateAssert.FileMissing(
            ABHotfixDiffTaskFile,
            "计划 T6 规定 AB Hotfix 差异扫描移出核心主干、逻辑收归 AB Export/Diff，"
            + "独立 Task 文件必须删除");
    }

    /// <summary>CompleteBuildSummary 是构建结果面板的唯一数据源。</summary>
    private static void VerifyCompleteBuildSummaryTypeDeclared()
    {
        GateAssert.TreeHasSymbol(
            SharedBuildDir,
            "CompleteBuildSummary",
            "计划 T6/T7 要求 CompleteBuildSummary 作为构建结果面板唯一数据源，"
            + "承载 BuildId/BackendId/BuildType/RuntimeMode/Version/Files/Messages/Statistics，"
            + $"并供无状态 Diff 消费；{SharedBuildDir} 树内不存在该类型");
    }
}

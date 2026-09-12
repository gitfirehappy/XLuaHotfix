#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 共享构建编排 runner：版本候选来自 Summary Index，构建事实（Summary/Index）由交付事务提交。
/// </summary>
/// <remarks>
/// 版本与构建事实的提交顺序（最后一个可见身份提交点是 Summary Index）：
/// 提升产物 → 应用本地启动数据 → 写正式 Summary → 写 Index → 提交补偿 token；
/// 任一步失败按逆序恢复，构建失败不推进项目版本。
/// </remarks>
public static class BuildProjectRunner
{
    /// <summary>
    /// 最近一次构建的完整摘要（CompleteBuildSummary），构建结果面板的唯一数据源。
    /// 构建在 Export 阶段之前失败时为 null；每次构建开始时先清空，避免读到上一次的结果。
    /// </summary>
    public static CompleteBuildSummary LastSummary { get; private set; }

    /// <summary>构建单机离线包，产物直接写入 StreamingAssets/Standalone/。</summary>
    public static bool BuildStandalone(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
        => RunPlannedBuild(BuildType.Standalone, backendKey, backendFactory, options, attemptDelivery);

    /// <summary>构建完整包，用于大版本更新。</summary>
    public static bool BuildFullPackage(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
    {
        bool success = RunPlannedBuild(BuildType.Full, backendKey, backendFactory, options, attemptDelivery);
        if (success && !Application.isBatchMode)
        {
            EditorApplication.ExecuteMenuItem("File/Build Settings...");
            Debug.Log("[BuildProjectRunner] 请在弹出的Build Settings中选择目标平台和场景，点Build按钮后自动导出包体！");
        }

        return success;
    }

    /// <summary>构建热更包，用于小版本更新。</summary>
    public static bool BuildHotfix(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
        => RunPlannedBuild(BuildType.Hotfix, backendKey, backendFactory, options, attemptDelivery);

    /// <summary>
    /// 读取或重建构建事实索引；索引缺失或损坏只损失定位加速，不阻断本可完成的构建。
    /// </summary>
    private static BuildSummaryIndex LoadOrRebuildIndex(BuildSummaryStore store)
    {
        if (store.TryReadIndex(out BuildSummaryIndex index, out string error))
            return index;

        Debug.LogWarning($"[{nameof(BuildProjectRunner)}] Summary Index 不可用，从正式摘要重建: {error}");
        return store.RebuildIndex();
    }

    /// <summary>按索引的 LastBuildDate 计算当日构建序号。</summary>
    private static int ResolveTodayBuildCount(BuildSummaryIndex index)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        BuildSummaryProjectVersion projectVersion = index.ProjectVersion ?? new BuildSummaryProjectVersion();
        return string.Equals(projectVersion.LastBuildDate, today, StringComparison.Ordinal)
            ? projectVersion.DailyBuildCount + 1
            : 1;
    }

    /// <summary>Hotfix 的粗校验：该后端至少存在一个成功 Full 事实。</summary>
    private static bool HasFullBaseline(BuildSummaryIndex index, string backendKey)
    {
        if (index.Scopes == null)
            return false;

        for (int i = 0; i < index.Scopes.Count; i++)
        {
            BuildSummaryScope scope = index.Scopes[i];
            if (scope == null || string.IsNullOrEmpty(scope.LatestFullSummaryId))
                continue;
            if (string.Equals(scope.Backend, backendKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool RunPlannedBuild(
        BuildType buildType,
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options,
        bool attemptDelivery)
    {
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        BuildSummaryIndex index = LoadOrRebuildIndex(store);

        BuildVersionPlan plan = BuildVersionPlanner.Plan(
            index.ProjectVersion?.CurrentSuccessfulVersion,
            buildType,
            options?.RequestedChannel,
            ResolveTodayBuildCount(index));
        if (!plan.Success)
        {
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 无法确定构建版本: {plan.Error}");
            return false;
        }

        if (buildType == BuildType.Hotfix && !HasFullBaseline(index, backendKey))
        {
            Debug.LogError($"[{nameof(BuildProjectRunner)}] Hotfix 缺少同作用域的成功 Full 基准，拒绝构建。");
            return false;
        }

        return RunBuild(plan.Version, buildType, backendKey, backendFactory, options, attemptDelivery, store, index);
    }

    private static bool RunBuild(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options,
        bool attemptDelivery,
        BuildSummaryStore store,
        BuildSummaryIndex index)
    {
        Debug.Log($"[{nameof(BuildProjectRunner)}] 开始 {buildType} build。Backend={backendKey}, Version={version.GetReleaseVersionString()}, Build={version.Build}");

        LastSummary = null;
        BuildPackageRequest request = null;
        BuildBackendResult buildResult = null;

        try
        {
            request = BuildPackageRequest.Create(version, buildType, backendKey, attemptDelivery);
            Debug.Log($"[{nameof(BuildProjectRunner)}] 已创建 BuildPackageRequest: Package={request.PackageName}, Backend={backendKey}, Output={request.OutputDir}");

            IBuildBackend backend = backendFactory != null
                ? backendFactory()
                : throw new InvalidOperationException("Build backend factory 为 null。");
            buildResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            LastSummary = buildResult.Summary;
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                string reason = err != null ? $"[{err.Code}] {err.Message}" : "未知错误";
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 后端 build 失败: {reason}");
                HandleFailedPackage(request, reason);
                return false;
            }

            if (request.IsAttemptLayout)
            {
                // attempt 交付：Runner 独占提交点，产物 / 本地启动数据 / 构建事实 要么全部完成、要么全部回滚。
                if (!DeliverAttemptBuild(request, backend, buildResult, store, index))
                    return false;

                Debug.Log($"[{nameof(BuildProjectRunner)}] Package build 完成，已交付: {request.DeliveryOutputDir}");
                if (!Application.isBatchMode)
                    TryRevealPackage(request.DeliveryOutputDir);
                return true;
            }

            // 非 attempt 布局：构建成功后直接发布到最终目录。
            if (buildType == BuildType.Standalone)
            {
                PublishBuildArtifacts(request, backend);
                CommitBuildFactsOrFail(request, buildResult, store, index);
                Debug.Log($"[{nameof(BuildProjectRunner)}] Standalone build 完成: {request.OutputDir}");
                if (!Application.isBatchMode)
                    TryRevealPackage(request.OutputDir);
                return true;
            }

            PublishBuildArtifacts(request, backend);
            CommitBuildFactsOrFail(request, buildResult, store, index);

            Debug.Log($"[{nameof(BuildProjectRunner)}] Package build 完成，已发布本地启动数据: {request.OutputDir}");
            if (!Application.isBatchMode)
                TryRevealPackage(request.OutputDir);

            return true;
        }
        catch (Exception ex)
        {
            HandleFailedPackage(request, ex.Message);
            Debug.LogError($"[{nameof(BuildProjectRunner)}] Build 流程异常，Version={version.GetReleaseVersionString()}, Type={buildType}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 非 attempt 布局的构建事实提交：写正式 Summary 与 Index；失败时删除本次 Summary 并恢复旧 Index。
    /// </summary>
    private static void CommitBuildFactsOrFail(
        BuildPackageRequest request,
        BuildBackendResult buildResult,
        BuildSummaryStore store,
        BuildSummaryIndex index)
    {
        BuildFactsCommit facts = CommitBuildFacts(store, index, request, buildResult.Summary);
        if (facts == null)
            throw new InvalidOperationException("构建事实提交失败（正式 Summary 或 Summary Index 未能写入）。");
        facts.Commit();
    }

    /// <summary>
    /// attempt 交付事务：Runner 已提升 → 本地启动数据 → 累计 Hotfix 重置 → Summary → Index。
    /// 产物提升由 BuildPipelineRunner 在运行成功时完成，本事务只消费它返回的交付 token；
    /// 任一步骤失败按逆序补偿，live 状态回到事务开始前的值。
    /// </summary>
    private static bool DeliverAttemptBuild(
        BuildPackageRequest request,
        IBuildBackend backend,
        BuildBackendResult buildResult,
        BuildSummaryStore store,
        BuildSummaryIndex index)
    {
        var compensation = new BuildDeliveryCompensation();
        try
        {
            compensation.PromoteToken = buildResult.PipelineResult?.DeliveryToken;
            if (request.IsAttemptLayout && compensation.PromoteToken == null)
                throw new InvalidOperationException("attempt 布局下 Runner 未返回交付 token，产物提升状态未知。");

            BuildPackageRequest delivered = request.WithPromotedOutput();

            compensation.LocalData = LocalBuildDataExporter.BeginDelivery(delivered, backend?.BuiltInPackageHandler);
            compensation.Facts = CommitBuildFacts(store, index, delivered, buildResult.Summary);
            if (compensation.Facts == null)
                throw new InvalidOperationException("构建事实提交失败（正式 Summary 或 Summary Index 未能写入）。");

            compensation.Commit();
            return true;
        }
        catch (Exception ex)
        {
            compensation.Rollback();
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 交付提交失败，已回滚 live 状态: Package={request.PackageName}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 写正式 Summary 并更新 Index（Index 最后写）。失败时删除本次 Summary 并恢复旧 Index。
    /// </summary>
    /// <remarks>Hotfix 交付前做精确基准校验：同后端、同平台、同通道、同 Major 的成功 Full。</remarks>
    private static BuildFactsCommit CommitBuildFacts(
        BuildSummaryStore store,
        BuildSummaryIndex index,
        BuildPackageRequest request,
        CompleteBuildSummary summary)
    {
        if (summary == null || string.IsNullOrEmpty(summary.BuildId))
            return null;

        var commit = new BuildFactsCommit(store, summary);
        try
        {
            summary.ArtifactRelativePath = ToProjectRelativePath(request.DeliveryOutputDir);
            summary.FinishedAtUtc = DateTime.UtcNow;

            string channel = summary.Version.Channel ?? string.Empty;
            BuildSummaryScope scope = index.GetOrAddScope(summary.BackendId, summary.Platform, channel);

            if (request.BuildType == BuildType.Hotfix)
            {
                if (!ValidateHotfixBaseline(store, scope, summary.Version, out string baselineError))
                {
                    Debug.LogError($"[{nameof(BuildProjectRunner)}] Hotfix 基准校验失败: {baselineError}");
                    commit.Rollback();
                    return null;
                }

                summary.BaseFullSummaryId = scope.LatestFullSummaryId;
            }

            if (!store.TryWriteSummary(summary, out string summaryError))
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 正式 Summary 写入失败: {summaryError}");
                commit.Rollback();
                return null;
            }

            index.ProjectVersion ??= new BuildSummaryProjectVersion();
            index.ProjectVersion.CurrentSuccessfulVersion = summary.Version.GetReleaseVersionString();
            index.ProjectVersion.LastBuildDate = DateTime.Now.ToString("yyyy-MM-dd");
            index.ProjectVersion.DailyBuildCount = summary.Version.Build;

            scope.LatestSuccessfulSummaryId = summary.BuildId;
            if (request.BuildType == BuildType.Full)
                scope.LatestFullSummaryId = summary.BuildId;

            if (!store.TryWriteIndex(index, out string indexError))
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] Summary Index 写入失败: {indexError}");
                commit.Rollback();
                return null;
            }

            return commit;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 构建事实提交异常: {ex}");
            commit.Rollback();
            return null;
        }
    }

    /// <summary>Hotfix 基准必须是同后端、同平台、同通道、同 Major 的可读成功 Full。</summary>
    private static bool ValidateHotfixBaseline(
        BuildSummaryStore store,
        BuildSummaryScope scope,
        VersionNumber hotfixVersion,
        out string error)
    {
        error = string.Empty;
        if (scope == null || string.IsNullOrEmpty(scope.LatestFullSummaryId))
        {
            error = $"作用域 {scope?.Backend}/{scope?.Platform}/{BuildVersionPlanner.DisplayChannel(scope?.Channel)} 没有成功 Full 基准。";
            return false;
        }

        if (!store.TryReadSummaryDocument(scope.Backend, scope.LatestFullSummaryId,
                out CompleteBuildSummary.SummaryDocument full, out string readError))
        {
            error = $"基准 Full 摘要不可读: {readError}";
            return false;
        }

        if (!VersionNumber.TryParse(full.Version, out VersionNumber fullVersion))
        {
            error = $"基准 Full 版本无法解析: '{full.Version}'";
            return false;
        }

        if (fullVersion.Major != hotfixVersion.Major)
        {
            error = $"基准 Full 与 Hotfix 不是同 Major: Full={full.Version}, Hotfix={hotfixVersion.GetReleaseVersionString()}";
            return false;
        }

        return true;
    }

    private static string ToProjectRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;

        string root = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        string path = FYAssetPathUtility.NormalizePath(absolutePath);
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            path = path.Substring(root.Length).TrimStart('/', '\\');
        return path;
    }

    /// <summary>
    /// 交付补偿集合。Capture 在进入对应步骤之前调用；Rollback 逆序执行已登记的补偿；Commit 释放扩展备份。
    /// </summary>
    private sealed class BuildDeliveryCompensation
    {
        public IBuildDeliveryToken PromoteToken;
        public LocalBuildDataExporter.LocalBuildDataDelivery LocalData;
        public BuildFactsCommit Facts;

        public void Commit()
        {
            Facts?.Commit();
            LocalData?.Commit();
            PromoteToken?.Commit();
        }

        public void Rollback()
        {
            RollbackSafely(() => Facts?.Rollback());
            RollbackSafely(() => LocalData?.Rollback());
            RollbackSafely(() => PromoteToken?.Rollback());
        }

        private static void RollbackSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 交付补偿步骤失败（已尽力继续其余补偿）: {ex}");
            }
        }
    }

    /// <summary>
    /// 构建事实提交的补偿记录：事务前索引字节与本次新建摘要标识。
    /// </summary>
    private sealed class BuildFactsCommit
    {
        private readonly BuildSummaryStore _store;
        private readonly CompleteBuildSummary _summary;
        private byte[] _indexBytesBefore;

        public BuildFactsCommit(BuildSummaryStore store, CompleteBuildSummary summary)
        {
            _store = store;
            _summary = summary;
            _indexBytesBefore = store.ReadIndexBytesOrNull();
        }

        public void Commit()
        {
            _indexBytesBefore = null;
        }

        public void Rollback()
        {
            try
            {
                _store.TryDeleteSummary(_summary.BackendId, _summary.BuildId, out _);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 回滚摘要失败: {ex.Message}");
            }

            if (_store.TryRestoreIndex(_indexBytesBefore, out string error))
                _indexBytesBefore = null;
            else
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 回滚 Summary Index 失败: {error}");
        }
    }

    /// <summary>
    /// 非 attempt 布局的交付后处理：只导出本地启动数据。
    /// PackageIndex 是发布事务的产物，由 BuildPublisher 在内容就位并校验后最后生成上传，构建不写它。
    /// </summary>
    private static void PublishBuildArtifacts(BuildPackageRequest request, IBuildBackend backend)
    {
        LocalBuildDataExporter.Publish(request, backend?.BuiltInPackageHandler);
    }

    private static void HandleFailedPackage(BuildPackageRequest request, string reason)
    {
        if (request == null)
            return;
        if (!FileHelper.DirectoryExists(request.OutputDir))
            return;
        if (!IsSafePackageOutputDir(request, out string safetyReason))
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 跳过失败包清理: {safetyReason}");
            TryWriteFailedPackageMarker(request, reason);
            return;
        }

        if (FileHelper.TryDeleteDirectory(request.OutputDir, true))
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 已删除失败包目录: {request.OutputDir}");
            return;
        }

        TryWriteFailedPackageMarker(request, reason);
    }

    private static bool IsSafePackageOutputDir(BuildPackageRequest request, out string reason)
    {
        reason = string.Empty;
        string outputDir = FYAssetPathUtility.NormalizePath(request.OutputDir);

        // attempt 布局：失败清理只允许删除 attempt 根之下的目录，任何 live 出口一律拒绝。
        if (request.IsAttemptLayout)
        {
            string attemptRoot = FYAssetPathUtility.NormalizePath(BuildPathManager.AttemptPackagesRoot);
            string attemptRootWithSeparator = attemptRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison attemptComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.IsNullOrEmpty(outputDir) || !outputDir.StartsWith(attemptRootWithSeparator, attemptComparison))
            {
                reason = $"attempt 布局下只允许清理 attempt 根之下的目录。Root={attemptRoot}, Actual={outputDir}";
                return false;
            }

            return true;
        }

        if (request.BuildType == BuildType.Standalone)
        {
            string standaloneDir = FYAssetPathUtility.NormalizePath(BuildPathManager.StandalonePackageDir);
            if (FYAssetPathUtility.AreSamePath(standaloneDir, outputDir))
                return true;

            reason = $"Standalone OutputDir must equal StandalonePackageDir. Expected={standaloneDir}, Actual={outputDir}";
            return false;
        }

        string packagesDir = FYAssetPathUtility.NormalizePath(BuildPathManager.PackagesDir);
        if (string.IsNullOrEmpty(packagesDir) || string.IsNullOrEmpty(outputDir))
        {
            reason = "PackagesDir or OutputDir is empty.";
            return false;
        }
        if (FYAssetPathUtility.AreSamePath(packagesDir, outputDir))
        {
            reason = $"OutputDir equals PackagesDir: {outputDir}";
            return false;
        }

        string rootWithSeparator = packagesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!outputDir.StartsWith(rootWithSeparator, comparison))
        {
            reason = $"OutputDir is outside PackagesDir. PackagesDir={packagesDir}, OutputDir={outputDir}";
            return false;
        }
        if (!string.Equals(Path.GetFileName(outputDir), request.PackageName, comparison))
        {
            reason = $"OutputDir name does not match PackageName. OutputDir={outputDir}, PackageName={request.PackageName}";
            return false;
        }

        return true;
    }

    private static void TryWriteFailedPackageMarker(BuildPackageRequest request, string reason)
    {
        try
        {
            FileHelper.EnsureDirectory(request.OutputDir);
            var marker = new FailedBuildMarker
            {
                PackageName = request.PackageName,
                Version = request.Version.GetReleaseVersionString(),
                Build = request.Version.Build,
                BuildType = request.BuildType.ToString(),
                BackendMode = request.BackendKey,
                FailedAtUtc = DateTime.UtcNow.ToString("o"),
                Reason = reason ?? string.Empty,
                OutputDir = request.OutputDir
            };

            string markerPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, "FAILED_BUILD.json");
            FileHelper.WriteAllTextAtomic(markerPath, SerializationUtility.SerializeToJson(marker, true));
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 失败包删除失败，已写入标记: {markerPath}");
        }
        catch (Exception markerEx)
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 写入失败包标记失败: {markerEx.Message}");
        }
    }

    private static void TryRevealPackage(string outputDir)
    {
        try
        {
            EditorUtility.RevealInFinder(outputDir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 打开包目录失败: {ex.Message}");
        }
    }

    [Serializable]
    private sealed class FailedBuildMarker
    {
        public string PackageName;
        public string Version;
        public int Build;
        public string BuildType;
        public string BackendMode;
        public string FailedAtUtc;
        public string Reason;
        public string OutputDir;
    }
}
#endif

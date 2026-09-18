#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>共享构建编排：规划版本、执行后端、交付输出并提交构建事实。</summary>
public static class BuildProjectRunner
{
    public static BuildResult BuildStandalone(string backendKey, Func<IBuildBackend> backendFactory, BuildExecutionOptions options = null)
        => RunPlannedBuild(BuildType.Standalone, backendKey, backendFactory, options);

    public static BuildResult BuildFullPackage(string backendKey, Func<IBuildBackend> backendFactory, BuildExecutionOptions options = null)
    {
        BuildResult result = RunPlannedBuild(BuildType.Full, backendKey, backendFactory, options);
        if (result.Success && !Application.isBatchMode)
        {
            EditorApplication.ExecuteMenuItem("File/Build Settings...");
            Debug.Log("[BuildProjectRunner] 请在弹出的 Build Settings 中选择目标平台和场景，点 Build 按钮后导出包体。");
        }
        return result;
    }

    public static BuildResult BuildHotfix(string backendKey, Func<IBuildBackend> backendFactory, BuildExecutionOptions options = null)
        => RunPlannedBuild(BuildType.Hotfix, backendKey, backendFactory, options);

    private static BuildResult RunPlannedBuild(
        BuildType buildType,
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options)
    {
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        BuildSummaryIndex index = LoadOrRebuildIndex(store);
        BuildVersionPlan plan = BuildVersionPlanner.Plan(
            index.ProjectVersion?.CurrentSuccessfulVersion,
            buildType,
            options?.RequestedChannel);
        if (!plan.Success)
            return BuildResult.Fail(BuildMessage.Error(BuildErrorCodes.BuildFailed, plan.Error, nameof(BuildProjectRunner)));

        return RunBuild(plan.Version, buildType, backendKey, backendFactory, options, store, index);
    }

    private static BuildSummaryIndex LoadOrRebuildIndex(BuildSummaryStore store)
    {
        if (store.TryReadIndex(out BuildSummaryIndex index, out string error))
            return index;
        Debug.LogWarning($"[{nameof(BuildProjectRunner)}] Summary Index 不可用，从正式摘要重建: {error}");
        return store.RebuildIndex();
    }

    private static BuildResult RunBuild(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options,
        BuildSummaryStore store,
        BuildSummaryIndex index)
    {
        BuildRequest request = null;
        try
        {
            request = BuildRequest.Create(version, buildType, backendKey);
            if (backendFactory == null)
                return BuildResult.Fail(BuildMessage.Error(BuildErrorCodes.BuildFailed, "Build backend factory 为空。", nameof(BuildProjectRunner)));

            IBuildBackend backend = backendFactory();
            BuildBackendResult backendResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            if (backendResult == null)
                return BuildResult.Fail(BuildMessage.Error(BuildErrorCodes.BuildFailed, "Build backend 返回 null。", nameof(BuildProjectRunner)));
            if (!backendResult.Success)
            {
                HandleFailedPackage(request, backendResult.Error?.Message);
                return BuildResult.Fail(backendResult.Error ?? BuildMessage.Error(BuildErrorCodes.BuildFailed, "构建失败。", nameof(BuildProjectRunner)), backendResult.Summary, backendResult.ReportPath);
            }

            LocalBuildDataExporter.LocalBuildDataDelivery localData = null;
            BuildDeliveryResult delivery = null;
            BuildFactsCommit facts = null;
            try
            {
                localData = LocalBuildDataExporter.BeginDelivery(request, backend.BuiltInPackageHandler);
                delivery = BuildOutputDelivery.Deliver(request.TemporaryOutputDir, request.FinalOutputDir);
                facts = CommitBuildFacts(store, index, request, backendResult.Summary);
                if (facts == null)
                    throw new InvalidOperationException("构建事实提交失败。");

                facts.Commit();
                localData?.Commit();
                delivery.Commit();
                Debug.Log($"[{nameof(BuildProjectRunner)}] 构建完成: {request.PackageName}");
                TryRevealPackage(request.FinalOutputDir);
                return BuildResult.Ok(backendResult.Summary, backendResult.ReportPath);
            }
            catch
            {
                facts?.Rollback();
                localData?.Rollback();
                delivery?.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            HandleFailedPackage(request, ex.Message);
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 构建失败: {ex}");
            return BuildResult.Fail(BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, nameof(BuildProjectRunner)));
        }
    }

    private static BuildFactsCommit CommitBuildFacts(
        BuildSummaryStore store,
        BuildSummaryIndex index,
        BuildRequest request,
        CompleteBuildSummary summary)
    {
        if (summary == null || string.IsNullOrEmpty(summary.BuildId))
            return null;

        var commit = new BuildFactsCommit(store, summary);
        try
        {
            summary.ArtifactRelativePath = ToProjectRelativePath(request.FinalOutputDir);
            summary.FinishedAtUtc = DateTime.UtcNow;
            BuildSummaryScope scope = index.GetOrAddScope(summary.BackendId, summary.Platform, summary.Version.Channel ?? string.Empty);
            if (request.BuildType == BuildType.Hotfix)
                summary.BaseFullSummaryId = scope.LatestFullSummaryId;

            if (!store.TryWriteSummary(summary, out string summaryError))
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 写入 Summary 失败: {summaryError}");
                commit.Rollback();
                return null;
            }

            index.ProjectVersion ??= new BuildSummaryProjectVersion();
            index.ProjectVersion.CurrentSuccessfulVersion = summary.Version.GetReleaseVersionString();
            scope.LatestSuccessfulSummaryId = summary.BuildId;
            if (request.BuildType == BuildType.Full)
                scope.LatestFullSummaryId = summary.BuildId;

            if (!store.TryWriteIndex(index, out string indexError))
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 写入 Summary Index 失败: {indexError}");
                commit.Rollback();
                return null;
            }
            return commit;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 提交构建事实异常: {ex.Message}");
            commit.Rollback();
            return null;
        }
    }

    private static string ToProjectRelativePath(string absolutePath)
    {
        string root = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        string path = FYAssetPathUtility.NormalizePath(absolutePath ?? string.Empty);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(root.Length).TrimStart('/', '\\')
            : path;
    }

    private static void HandleFailedPackage(BuildRequest request, string reason)
    {
        if (request == null || !FileHelper.DirectoryExists(request.TemporaryOutputDir))
            return;
        if (FileHelper.TryDeleteDirectory(request.TemporaryOutputDir, true))
            return;

        try
        {
            FileHelper.WriteAllTextAtomic(
                FYAssetPathUtility.JoinFilePath(request.TemporaryOutputDir, "FAILED_BUILD.json"),
                SerializationUtility.SerializeToJson(new FailedBuildMarker
                {
                    PackageName = request.PackageName,
                    Version = request.Version.GetReleaseVersionString(),
                    BuildType = request.BuildType.ToString(),
                    BackendMode = request.BackendKey,
                    FailedAtUtc = DateTime.UtcNow.ToString("o"),
                    Reason = reason ?? string.Empty
                }, true));
        }
        catch (Exception markerError)
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 写入失败标记失败: {markerError.Message}");
        }
    }

    private static void TryRevealPackage(string path)
    {
        if (Application.isBatchMode)
            return;
        try { EditorUtility.RevealInFinder(path); }
        catch (Exception ex) { Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 打开输出目录失败: {ex.Message}"); }
    }

    private sealed class BuildFactsCommit
    {
        private readonly BuildSummaryStore _store;
        private readonly CompleteBuildSummary _summary;
        private byte[] _oldIndex;

        public BuildFactsCommit(BuildSummaryStore store, CompleteBuildSummary summary)
        {
            _store = store;
            _summary = summary;
            _oldIndex = store.ReadIndexBytesOrNull();
        }

        public void Commit() => _oldIndex = null;

        public void Rollback()
        {
            _store.TryDeleteSummary(_summary.BackendId, _summary.BuildId, out _);
            _store.TryRestoreIndex(_oldIndex, out _);
            _oldIndex = null;
        }
    }

    [Serializable]
    private sealed class FailedBuildMarker
    {
        public string PackageName;
        public string Version;
        public string BuildType;
        public string BackendMode;
        public string FailedAtUtc;
        public string Reason;
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 共享 Build Test 引擎：Full / Hotfix / Chain。
/// CLI 与 Editor Test 页共用此入口。
/// </summary>
public static class BuildTestEngine
{
    public static BuildTestResult Run(BuildTestRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (BuildTestState.TryRecoverStaleRun(out BuildTestResult recoveryResult))
            return recoveryResult;

        var result = new BuildTestResult
        {
            Backend = request.Backend.ToString(),
            Mode = request.Mode.ToString(),
            ExitCode = BuildTestExitCodes.PreconditionFailed
        };

        string runRoot = null;
        BuildTestRecoveryRecord recovery = null;
        List<BuildTestTargetSnapshot> targets = null;
        bool mutatedFixture = false;
        bool projectSnapshotted = false;
        bool targetsSnapshotted = false;
        BuildTestStage stage = BuildTestStage.Preflight;

        try
        {
            Stage(request, result, ref stage, BuildTestStage.Preflight, "preflight");
            BuildTestFixtures.EnsurePermanentFixtures();

            targets = BuildTestState.FreezeTargets(request.Backend, request.TargetIds, request.ExternalConfirmIds);
            result.TargetSnapshots = targets;

            runRoot = string.IsNullOrEmpty(request.ResultRootOverride)
                ? BuildTestPaths.CreateRunRoot(request.Backend, request.Mode)
                : request.ResultRootOverride;
            FileHelper.EnsureDirectory(runRoot);
            result.RunRoot = runRoot;
            result.RunId = Path.GetFileName(runRoot);

            recovery = BuildTestState.WriteRecovery(runRoot, request, targets);

            Stage(request, result, ref stage, BuildTestStage.Snapshot, "snapshot project + targets");
            BuildTestState.SnapshotProject(runRoot, request.Backend);
            projectSnapshotted = true;
            BuildTestState.SnapshotTargets(runRoot, targets);
            targetsSnapshotted = true;

            switch (request.Mode)
            {
                case BuildTestMode.Full:
                    RunFull(request, result, runRoot, targets, ref stage);
                    break;
                case BuildTestMode.Hotfix:
                    RunHotfix(request, result, runRoot, targets, ref stage, ref mutatedFixture);
                    break;
                case BuildTestMode.Chain:
                    RunChain(request, result, runRoot, targets, ref stage, ref mutatedFixture);
                    break;
                case BuildTestMode.Standalone:
                    throw new InvalidOperationException(
                        "Standalone mode is E2E-only. Use: python CommandLine/fyasset_test.py ab e2e standalone");
                default:
                    throw new InvalidOperationException("Unknown mode: " + request.Mode);
            }

            Stage(request, result, ref stage, BuildTestStage.Restore, "restore project + targets");
            RestoreAll(request, result, runRoot, targets, mutatedFixture, projectSnapshotted, targetsSnapshotted);
            result.RestorationSucceeded = true;
            result.Passed = true;
            result.ExitCode = BuildTestExitCodes.Passed;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FirstFailure = string.IsNullOrEmpty(result.FirstFailure) ? ex.Message : result.FirstFailure;
            result.FailedStage = stage.ToString();
            if (result.ExitCode == BuildTestExitCodes.Passed || result.ExitCode == BuildTestExitCodes.PreconditionFailed)
                result.ExitCode = ClassifyExitCode(stage, ex);

            Debug.LogError($"[BuildTestEngine] FAIL stage={stage}: {result.FirstFailure}\n{ex}");

            try
            {
                if (runRoot != null)
                {
                    Stage(request, result, ref stage, BuildTestStage.Restore, "restore after failure");
                    RestoreAll(request, result, runRoot, targets, mutatedFixture, projectSnapshotted, targetsSnapshotted);
                    result.RestorationSucceeded = true;
                }
            }
            catch (Exception restoreEx)
            {
                result.RestorationSucceeded = false;
                result.ExitCode = BuildTestExitCodes.RestoreFailed;
                result.FirstFailure = (result.FirstFailure ?? string.Empty) + " | restore failed: " + restoreEx.Message;
            }
        }
        finally
        {
            if (runRoot != null)
            {
                Stage(request, result, ref stage, BuildTestStage.PersistResult, "write result.json");
                FileHelper.WriteAllTextAtomic(
                    BuildTestPaths.ResultJson(runRoot),
                    SerializationUtility.SerializeToJson(result, true));
                if (recovery != null)
                    BuildTestState.MarkRecoveryCompleted(runRoot, recovery, result.RestorationSucceeded);
                if (!request.SkipRetentionCleanup)
                    BuildTestPaths.RetainLatest(request.Backend, request.Mode, 20);
            }
        }

        return result;
    }

    private static void RunFull(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        List<BuildTestTargetSnapshot> targets,
        ref BuildTestStage stage)
    {
        Stage(request, result, ref stage, BuildTestStage.PrepareProject, "prepare isolated Full");
        BuildTestState.PrepareIsolatedFullProject(request.Backend);

        Stage(request, result, ref stage, BuildTestStage.BuildFull, "build Full");
        InvokeBuild(request.Backend, false, result);

        Stage(request, result, ref stage, BuildTestStage.AcceptFull, "accept Full disk");
        var ctx = new BuildTestAcceptance.AcceptanceContext
        {
            Backend = request.Backend,
            IsHotfix = false,
            ExpectedVersion = "2.0.0"
        };
        BuildTestAcceptance.AcceptFull(ctx, result);

        PublishAndProbeAll(request, result, runRoot, targets, isHotfix: false, ref stage);
    }

    private static void RunHotfix(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        List<BuildTestTargetSnapshot> targets,
        ref BuildTestStage stage,
        ref bool mutatedFixture)
    {
        Stage(request, result, ref stage, BuildTestStage.Preflight, "require local + target Full");
        BuildBaseline fullHead;
        if (!TryRequireHotfixBaseline(request, targets, out fullHead))
        {
            // clean-slate：事务内 seed Full（非产品静默 Full）；与 Chain 前半一致，结束统一 Restore
            Stage(request, result, ref stage, BuildTestStage.PrepareProject, "seed Full for Hotfix baseline");
            BuildTestState.PrepareIsolatedFullProject(request.Backend);

            Stage(request, result, ref stage, BuildTestStage.BuildFull, "seed build Full");
            InvokeBuild(request.Backend, false, result);

            Stage(request, result, ref stage, BuildTestStage.AcceptFull, "seed accept Full disk");
            var seedCtx = new BuildTestAcceptance.AcceptanceContext
            {
                Backend = request.Backend,
                ExpectedVersion = "2.0.0"
            };
            BuildTestAcceptance.AcceptFull(seedCtx, result);

            bool seedOk = PublishAndProbeAll(
                request, result, runRoot, targets, isHotfix: false, ref stage, keepTargets: true);
            if (!seedOk)
            {
                result.ExitCode = BuildTestExitCodes.PublishOrProbeFailed;
                throw new InvalidOperationException("Hotfix seed Full target phase failed.");
            }

            BuildTestAcceptance.RequireLocalFullIdentity(request.Backend, out fullHead);
            for (int i = 0; i < targets.Count; i++)
                BuildTestAcceptance.RequireTargetFullIdentity(targets[i], request.Backend, fullHead);
        }

        string fullVersion = fullHead.Version.GetReleaseVersionString();
        result.StreamingAssetsBaselineHash = HashGenerator.GenerateFileHash(
            FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUILD_INDEX_FILENAME));

        Stage(request, result, ref stage, BuildTestStage.MutateFixture, "mutate fixture");
        BuildTestFixtures.MutateHotfixFixture(request.Backend);
        mutatedFixture = true;

        Stage(request, result, ref stage, BuildTestStage.BuildHotfix, "build Hotfix");
        InvokeBuild(request.Backend, true, result);

        VersionRecord versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        string expectedHotfix = versionData.CurrentVersion.GetReleaseVersionString();

        Stage(request, result, ref stage, BuildTestStage.AcceptHotfix, "accept Hotfix disk");
        var ctx = new BuildTestAcceptance.AcceptanceContext
        {
            Backend = request.Backend,
            IsHotfix = true,
            ExpectedVersion = expectedHotfix,
            ExpectedParentVersion = fullVersion
        };
        BuildTestAcceptance.AcceptHotfix(ctx, result);

        PublishAndProbeAll(request, result, runRoot, targets, isHotfix: true, ref stage, fullHead);
    }

    /// <summary>
    /// 若本地 + 全部 Target 已具备同一 Full 身份则成功；否则返回 false 供 seed。
    /// </summary>
    private static bool TryRequireHotfixBaseline(
        BuildTestRequest request,
        List<BuildTestTargetSnapshot> targets,
        out BuildBaseline fullHead)
    {
        fullHead = null;
        try
        {
            BuildTestAcceptance.RequireLocalFullIdentity(request.Backend, out fullHead);
            for (int i = 0; i < targets.Count; i++)
                BuildTestAcceptance.RequireTargetFullIdentity(targets[i], request.Backend, fullHead);
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"[BuildTestEngine] Hotfix baseline 不可用，将 seed Full: {ex.Message}");
            fullHead = null;
            return false;
        }
    }

    private static void RunChain(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        List<BuildTestTargetSnapshot> targets,
        ref BuildTestStage stage,
        ref bool mutatedFixture)
    {
        Stage(request, result, ref stage, BuildTestStage.PrepareProject, "prepare isolated Chain Full");
        BuildTestState.PrepareIsolatedFullProject(request.Backend);

        Stage(request, result, ref stage, BuildTestStage.BuildFull, "build Full");
        InvokeBuild(request.Backend, false, result);

        Stage(request, result, ref stage, BuildTestStage.AcceptFull, "accept Full disk");
        var fullCtx = new BuildTestAcceptance.AcceptanceContext
        {
            Backend = request.Backend,
            ExpectedVersion = "2.0.0"
        };
        BuildTestAcceptance.AcceptFull(fullCtx, result);

        bool allFullOk = PublishAndProbeAll(request, result, runRoot, targets, isHotfix: false, ref stage, keepTargets: true);
        if (!allFullOk)
        {
            result.ExitCode = BuildTestExitCodes.PublishOrProbeFailed;
            throw new InvalidOperationException("Chain Full target phase failed; Hotfix skipped.");
        }

        BuildTestAcceptance.RequireLocalFullIdentity(request.Backend, out BuildBaseline fullHead);

        Stage(request, result, ref stage, BuildTestStage.MutateFixture, "mutate fixture");
        BuildTestFixtures.MutateHotfixFixture(request.Backend);
        mutatedFixture = true;

        Stage(request, result, ref stage, BuildTestStage.BuildHotfix, "build Hotfix");
        InvokeBuild(request.Backend, true, result);

        VersionRecord versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        string expectedHotfix = versionData.CurrentVersion.GetReleaseVersionString();

        Stage(request, result, ref stage, BuildTestStage.AcceptHotfix, "accept Hotfix disk");
        var hotfixCtx = new BuildTestAcceptance.AcceptanceContext
        {
            Backend = request.Backend,
            IsHotfix = true,
            ExpectedVersion = expectedHotfix,
            ExpectedParentVersion = "2.0.0"
        };
        BuildTestAcceptance.AcceptHotfix(hotfixCtx, result);

        PublishAndProbeAll(request, result, runRoot, targets, isHotfix: true, ref stage, fullHead);
    }

    private static bool PublishAndProbeAll(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        List<BuildTestTargetSnapshot> targets,
        bool isHotfix,
        ref BuildTestStage stage,
        BuildBaseline fullHead = null,
        bool keepTargets = false)
    {
        stage = isHotfix ? BuildTestStage.PublishHotfix : BuildTestStage.PublishFull;
        bool allOk = true;
        string firstFailure = null;

        BackendMode mode = BuildTestState.ToBackendMode(request.Backend);
        string channelKey = BuildBaselineStore.GetChannelKey(string.Empty, mode);
        BuildBaseline head = BuildBaselineStore.LoadLatest(channelKey);
        string version = head.Version.GetReleaseVersionString();
        string backendName = BuildTestPaths.BackendSegment(request.Backend);

        for (int i = 0; i < targets.Count; i++)
        {
            BuildTestTargetSnapshot target = targets[i];
            var outcome = FindOrAddOutcome(result, target.TargetId);
            string targetDir = BuildTestPaths.TargetDir(runRoot, target.TargetId);
            FileHelper.EnsureDirectory(targetDir);

            try
            {
                Stage(request, result, ref stage, isHotfix ? BuildTestStage.PublishHotfix : BuildTestStage.PublishFull, "publish " + target.TargetId);
                string publishPath = FYAssetPathUtility.JoinFilePath(
                    targetDir,
                    isHotfix ? "publish-hotfix.json" : "publish.json");
                BuildTestState.PublishHeadToTarget(request.Backend, target, publishPath);
                outcome.PublishSuccess = true;
                outcome.PublishedPackage = head.PackageName;
                outcome.PublishedVersion = version;

                Stage(request, result, ref stage, isHotfix ? BuildTestStage.ProbeHotfix : BuildTestStage.ProbeFull, "probe " + target.TargetId);
                string probePath = FYAssetPathUtility.JoinFilePath(
                    targetDir,
                    isHotfix ? "probe-after-publish.json" : "probe-after-publish.json");
                BuildTestState.ProbeTargetIdentity(
                    target,
                    backendName,
                    head.PackageName,
                    version,
                    true,
                    probePath);

                if (isHotfix && fullHead != null)
                {
                    string fullPackageDir = FYAssetPathUtility.JoinFilePath(
                        target.BackendPublishRoot,
                        FYAssetSettings.Instance.BuildPackagesFolderName,
                        fullHead.PackageName);
                    if (!FileHelper.DirectoryExists(fullPackageDir))
                        throw new InvalidOperationException("Full package missing after Hotfix publish: " + fullPackageDir);
                }

                outcome.ProbeSuccess = true;

                if (!keepTargets && !isHotfix)
                {
                    // Focused Full restores each target immediately after probe.
                    RestoreOneTarget(request, result, runRoot, target, outcome);
                }
                else if (isHotfix)
                {
                    RestoreOneTarget(request, result, runRoot, target, outcome);
                }
            }
            catch (Exception ex)
            {
                allOk = false;
                outcome.ProbeSuccess = false;
                outcome.Failure = ex.Message;
                firstFailure ??= ex.Message;
                result.ExitCode = BuildTestExitCodes.PublishOrProbeFailed;

                try
                {
                    RestoreOneTarget(request, result, runRoot, target, outcome);
                }
                catch (Exception restoreEx)
                {
                    outcome.RestoreSuccess = false;
                    outcome.Failure = (outcome.Failure ?? string.Empty) + " | restore: " + restoreEx.Message;
                    result.ExitCode = BuildTestExitCodes.RestoreFailed;
                    result.FirstFailure = firstFailure + " | restore failed: " + restoreEx.Message;
                    throw;
                }
            }
        }

        if (!allOk)
        {
            result.FirstFailure = firstFailure;
            if (keepTargets)
                return false;
            throw new InvalidOperationException(firstFailure ?? "Target publish/probe failed.");
        }

        return true;
    }

    private static void RestoreOneTarget(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        BuildTestTargetSnapshot target,
        BuildTestTargetOutcome outcome)
    {
        BuildTestState.RestoreTarget(runRoot, target);
        string probePath = FYAssetPathUtility.JoinFilePath(
            BuildTestPaths.TargetDir(runRoot, target.TargetId),
            "probe-after-restore.json");
        // After restore, PackageIndex may be absent; record presence only.
        PackageIndex index = BuildTestState.ReadPackageIndex(target);
        FileHelper.WriteAllTextAtomic(
            probePath,
            SerializationUtility.SerializeToJson(new
            {
                target.TargetId,
                Restored = true,
                HasPackageIndex = index != null,
                Package = index?.LatestPackage,
                Version = index?.LatestVersion != null ? index.LatestVersion.GetReleaseVersionString() : null
            }, true));
        outcome.RestoreSuccess = true;
    }

    private static void RestoreAll(
        BuildTestRequest request,
        BuildTestResult result,
        string runRoot,
        List<BuildTestTargetSnapshot> targets,
        bool mutatedFixture,
        bool projectSnapshotted,
        bool targetsSnapshotted)
    {
        if (mutatedFixture)
            BuildTestFixtures.RestoreHotfixFixture(request.Backend);

        if (targetsSnapshotted && targets != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                try
                {
                    BuildTestState.RestoreTarget(runRoot, targets[i]);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Target restore failed for " + targets[i].TargetId + ": " + ex.Message, ex);
                }
            }
        }

        if (projectSnapshotted)
            BuildTestState.RestoreProject(runRoot, request.Backend);
    }

    private static void InvokeBuild(BuildTestBackend backend, bool hotfix, BuildTestResult result)
    {
        // Do not route through UseABBackend; call concrete managers directly.
        if (backend == BuildTestBackend.AA)
        {
            if (hotfix)
                AABuildProjectManager.BuildHotfix();
            else
                AABuildProjectManager.BuildFullPackage();
            if (!AABuildProjectManager.LastBuildSuccess)
            {
                result.ExitCode = BuildTestExitCodes.BuildFailed;
                throw new InvalidOperationException("AA build reported failure.");
            }
        }
        else
        {
            if (hotfix)
                ABBuildProjectManager.BuildHotfix();
            else
                ABBuildProjectManager.BuildFullPackage();
            if (!ABBuildProjectManager.LastBuildSuccess)
            {
                result.ExitCode = BuildTestExitCodes.BuildFailed;
                throw new InvalidOperationException("AB build reported failure.");
            }
        }
    }

    private static BuildTestTargetOutcome FindOrAddOutcome(BuildTestResult result, string targetId)
    {
        for (int i = 0; i < result.TargetOutcomes.Count; i++)
        {
            if (string.Equals(result.TargetOutcomes[i].TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                return result.TargetOutcomes[i];
        }

        var outcome = new BuildTestTargetOutcome { TargetId = targetId };
        result.TargetOutcomes.Add(outcome);
        return outcome;
    }

    private static void Stage(
        BuildTestRequest request,
        BuildTestResult result,
        ref BuildTestStage stage,
        BuildTestStage next,
        string message)
    {
        stage = next;
        request.Progress?.Invoke(stage, message);
        Debug.Log($"[BuildTestEngine] [{stage}] {message}");
        result.Stages.Add(new BuildTestStageTiming
        {
            Stage = stage.ToString(),
            Seconds = 0
        });
    }

    private static int ClassifyExitCode(BuildTestStage stage, Exception ex)
    {
        if (stage == BuildTestStage.Preflight || stage == BuildTestStage.Snapshot)
            return BuildTestExitCodes.TargetPreflightFailed;
        if (stage == BuildTestStage.BuildFull || stage == BuildTestStage.BuildHotfix)
            return BuildTestExitCodes.BuildFailed;
        if (stage == BuildTestStage.AcceptFull || stage == BuildTestStage.AcceptHotfix)
            return BuildTestExitCodes.DiskAcceptanceFailed;
        if (stage == BuildTestStage.PublishFull || stage == BuildTestStage.PublishHotfix
            || stage == BuildTestStage.ProbeFull || stage == BuildTestStage.ProbeHotfix)
            return BuildTestExitCodes.PublishOrProbeFailed;
        if (stage == BuildTestStage.Restore)
            return BuildTestExitCodes.RestoreFailed;
        if (ex is InvalidOperationException && ex.Message.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0)
            return BuildTestExitCodes.TargetPreflightFailed;
        return BuildTestExitCodes.PreconditionFailed;
    }
}
#endif

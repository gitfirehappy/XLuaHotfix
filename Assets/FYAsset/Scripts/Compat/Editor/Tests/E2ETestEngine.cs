#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// E2E 编排：复用 Build Test 的 Target/恢复/验收，并追加 Player 构建与运行时冒烟。
/// </summary>
public static class E2ETestEngine
{
    public const string CoordinatorDefine = "FYASSET_E2E_COORDINATOR";

    public static BuildTestResult Run(BuildTestRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Full: Build+发布+Player smoke；Hotfix/Chain: retained Player 前向热更。
        if (BuildTestState.TryRecoverStaleRun(out BuildTestResult recovery))
            return recovery;

        string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string runRoot = FYAssetPathUtility.JoinFilePath(
            BuildTestPaths.TestRunsRoot,
            BuildTestPaths.BackendSegment(request.Backend),
            "e2e",
            BuildTestPaths.ModeSegment(request.Mode),
            runId);
        FileHelper.EnsureDirectory(runRoot);
        request.ResultRootOverride = runRoot;

        BuildTestResult buildResult;
        try
        {
            buildResult = request.Mode switch
            {
                BuildTestMode.Full => RunFullE2E(request, runRoot),
                BuildTestMode.Hotfix => RunHotfixE2E(request, runRoot),
                BuildTestMode.Chain => RunChainE2E(request, runRoot),
                BuildTestMode.Standalone => RunStandaloneE2E(request, runRoot),
                _ => throw new InvalidOperationException("未知 E2E mode: " + request.Mode)
            };
        }
        catch (Exception ex)
        {
            buildResult = new BuildTestResult
            {
                Passed = false,
                ExitCode = ClassifyE2EExit(ex.Message),
                Backend = request.Backend.ToString(),
                Mode = request.Mode.ToString(),
                RunRoot = runRoot,
                RunId = runId,
                FirstFailure = ex.Message,
                FailedStage = "E2E"
            };
            Debug.LogError("[E2ETestEngine] 失败 / FAIL: " + ex);
        }

        FileHelper.WriteAllTextAtomic(
            BuildTestPaths.ResultJson(runRoot),
            SerializationUtility.SerializeToJson(buildResult, true));
        BuildTestPaths.RetainLatest(request.Backend, request.Mode, 20);
        RetainE2E(request.Backend, request.Mode, 20);
        return buildResult;
    }

    private static int ClassifyE2EExit(string msg)
    {
        msg ??= string.Empty;
        if (msg.IndexOf("build failed", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("管线构建失败", StringComparison.OrdinalIgnoreCase) >= 0)
            return BuildTestExitCodes.BuildFailed;
        if (msg.IndexOf("Publish", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("probe", StringComparison.OrdinalIgnoreCase) >= 0)
            return BuildTestExitCodes.PublishOrProbeFailed;
        if (msg.IndexOf("restore", StringComparison.OrdinalIgnoreCase) >= 0)
            return BuildTestExitCodes.RestoreFailed;
        if (msg.IndexOf("HEAD", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("preflight", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("Full package", StringComparison.OrdinalIgnoreCase) >= 0)
            return BuildTestExitCodes.PreconditionFailed;
        return BuildTestExitCodes.RuntimeFailed;
    }

    private static BuildTestResult RunFullE2E(BuildTestRequest request, string runRoot)
    {
        var result = new BuildTestResult
        {
            Backend = request.Backend.ToString(),
            Mode = "E2E-" + request.Mode,
            RunRoot = runRoot,
            RunId = Path.GetFileName(runRoot),
            ExitCode = BuildTestExitCodes.PreconditionFailed
        };

        BuildTestFixtures.EnsurePermanentFixtures();
        var targets = BuildTestState.FreezeTargets(request.Backend, request.TargetIds, request.ExternalConfirmIds);
        result.TargetSnapshots = targets;
        var recovery = BuildTestState.WriteRecovery(runRoot, request, targets);
        bool mutated = false;
        try
        {
            BuildTestState.SnapshotProject(runRoot, request.Backend);
            BuildTestState.SnapshotTargets(runRoot, targets);
            BuildTestState.PrepareIsolatedFullProject(request.Backend);

            InvokeBuild(request.Backend, false);
            var accept = new BuildTestAcceptance.AcceptanceContext
            {
                Backend = request.Backend,
                ExpectedVersion = "2.0.0"
            };
            BuildTestAcceptance.AcceptFull(accept, result);

            BackendMode mode = BuildTestState.ToBackendMode(request.Backend);
            string channelKey = BuildBaselineStore.GetChannelKey(string.Empty, mode);
            BuildBaseline head = BuildBaselineStore.LoadLatest(channelKey);

            bool allOk = true;
            string firstFail = null;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var outcome = new BuildTestTargetOutcome { TargetId = target.TargetId };
                result.TargetOutcomes.Add(outcome);
                string tdir = BuildTestPaths.TargetDir(runRoot, target.TargetId);
                FileHelper.EnsureDirectory(tdir);
                try
                {
                    BuildTestState.PublishHeadToTarget(
                        request.Backend,
                        target,
                        FYAssetPathUtility.JoinFilePath(tdir, "publish-full.json"));
                    outcome.PublishSuccess = true;
                    BuildTestState.ProbeTargetIdentity(
                        target,
                        BuildTestPaths.BackendSegment(request.Backend),
                        head.PackageName,
                        head.Version.GetReleaseVersionString(),
                        true,
                        FYAssetPathUtility.JoinFilePath(tdir, "probe-full.json"));
                    outcome.ProbeSuccess = true;

                    RunPlayerSmoke(
                        request.Backend,
                        target,
                        tdir,
                        "full",
                        expectHotfixContent: false,
                        retain: false);

                    BuildTestState.RestoreTarget(runRoot, target);
                    outcome.RestoreSuccess = true;
                }
                catch (Exception ex)
                {
                    allOk = false;
                    firstFail ??= ex.Message;
                    outcome.Failure = ex.Message;
                    try
                    {
                        BuildTestState.RestoreTarget(runRoot, target);
                        outcome.RestoreSuccess = true;
                    }
                    catch (Exception rex)
                    {
                        outcome.RestoreSuccess = false;
                        result.ExitCode = BuildTestExitCodes.RestoreFailed;
                        throw new InvalidOperationException(ex.Message + " | restore: " + rex.Message, rex);
                    }
                }
            }

            BuildTestState.RestoreProject(runRoot, request.Backend);
            result.RestorationSucceeded = true;
            if (!allOk)
            {
                result.Passed = false;
                result.FirstFailure = firstFail;
                result.ExitCode = BuildTestExitCodes.RuntimeFailed;
            }
            else
            {
                result.Passed = true;
                result.ExitCode = BuildTestExitCodes.Passed;
            }
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FirstFailure = ex.Message;
            if (result.ExitCode == BuildTestExitCodes.Passed || result.ExitCode == BuildTestExitCodes.PreconditionFailed)
                result.ExitCode = BuildTestExitCodes.RuntimeFailed;
            try
            {
                if (mutated)
                    BuildTestFixtures.RestoreHotfixFixture(request.Backend);
                for (int i = 0; targets != null && i < targets.Count; i++)
                    BuildTestState.RestoreTarget(runRoot, targets[i]);
                BuildTestState.RestoreProject(runRoot, request.Backend);
                result.RestorationSucceeded = true;
            }
            catch (Exception rex)
            {
                result.RestorationSucceeded = false;
                result.ExitCode = BuildTestExitCodes.RestoreFailed;
                result.FirstFailure += " | restore: " + rex.Message;
            }
        }
        finally
        {
            BuildTestState.MarkRecoveryCompleted(runRoot, recovery, result.RestorationSucceeded);
        }

        return result;
    }

    /// <summary>retained Player：Full 建一次，Hotfix 仅 relaunch 同 exe + 隔离 persistent。</summary>
    private sealed class PlayerSession
    {
        public string TargetId;
        public string TargetDir;
        public string PlayerDir;
        public string ExePath;
        public string IsolatedProjectName;
        public string IsolatedPersistentRoot;
        public BuildTestBackend Backend;
        public PushTargetType TargetType;
        public string PublicBaseUrl;
    }

    private static BuildTestResult RunStandaloneE2E(BuildTestRequest request, string runRoot)
    {
        // 一级验证：BuildStandalone -> bake StandaloneBuild=true 的 Player -> exit 0
        var result = new BuildTestResult
        {
            Backend = request.Backend.ToString(),
            Mode = "E2E-Standalone",
            RunRoot = runRoot,
            RunId = Path.GetFileName(runRoot),
            ExitCode = BuildTestExitCodes.PreconditionFailed
        };

        if (request.Backend != BuildTestBackend.AB)
        {
            result.Passed = false;
            result.FirstFailure = "Standalone E2E 仅支持 AB backend。";
            result.ExitCode = BuildTestExitCodes.InvalidUsage;
            return result;
        }

        string targetDir = FYAssetPathUtility.JoinFilePath(runRoot, "targets", "standalone");
        FileHelper.EnsureDirectory(targetDir);
        bool oldStandalone = FYAssetSettings.Instance.StandaloneBuild;

        try
        {
            BuildTestFixtures.EnsurePermanentFixtures();
            BuildTestState.SnapshotProject(runRoot, request.Backend);

            ABBuildProjectManager.BuildStandalonePackage();
            if (!ABBuildProjectManager.LastBuildSuccess)
                throw new InvalidOperationException("Standalone build failed in E2E.");

            string buildIndexPath = FYAssetPathUtility.JoinFilePath(
                Application.streamingAssetsPath,
                FYAssetSettings.BUILD_INDEX_FILENAME);
            BuildIndexData buildIndex = SerializationUtility.DeserializeJson<BuildIndexData>(
                File.ReadAllText(buildIndexPath));
            if (buildIndex == null || string.IsNullOrEmpty(buildIndex.BuildGUID))
                throw new InvalidOperationException("Standalone BuildIndex is missing or invalid: " + buildIndexPath);
            string redundantPackageDir = BuildPathManager.GetPackageDir(buildIndex.BuildGUID);
            if (FileHelper.DirectoryExists(redundantPackageDir))
                throw new InvalidOperationException("Standalone must not create a HotfixOutput package: " + redundantPackageDir);

            result.ExitCode = BuildTestExitCodes.BuildFailed;

            // bake StandaloneBuild=true into Player, then restore after build
            FYAssetSettings.Instance.StandaloneBuild = true;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();

            var target = new BuildTestTargetSnapshot
            {
                TargetId = "standalone",
                TargetType = PushTargetType.LocalDirectory,
                PublicBaseUrl = "http://127.0.0.1:0",
                RuntimeUrl = "http://127.0.0.1:0"
            };

            PlayerSession session = BuildPlayerSession(request.Backend, target, targetDir);
            try
            {
                // standalone 不启本地热更服，直接跑 Coordinator smoke
                LaunchStandalonePlayer(session);
            }
            finally
            {
                CleanupSessions(new List<PlayerSession> { session });
            }

            result.Passed = true;
            result.ExitCode = BuildTestExitCodes.Passed;
            result.RestorationSucceeded = true;
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FirstFailure = ex.Message;
            if (result.ExitCode == BuildTestExitCodes.Passed || result.ExitCode == BuildTestExitCodes.PreconditionFailed)
                result.ExitCode = BuildTestExitCodes.RuntimeFailed;
            Debug.LogError("[E2ETestEngine] Standalone 失败 / FAIL: " + ex);
            try
            {
                BuildTestState.RestoreProject(runRoot, request.Backend);
                result.RestorationSucceeded = true;
            }
            catch (Exception rex)
            {
                result.RestorationSucceeded = false;
                result.ExitCode = BuildTestExitCodes.RestoreFailed;
                result.FirstFailure += " | restore: " + rex.Message;
            }
        }
        finally
        {
            FYAssetSettings.Instance.StandaloneBuild = oldStandalone;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();
            try
            {
                BuildTestState.RestoreProject(runRoot, request.Backend);
                result.RestorationSucceeded = true;
            }
            catch
            {
                // best-effort; already reported above if primary path failed
            }
        }

        return result;
    }

    private static void LaunchStandalonePlayer(PlayerSession session)
    {
        if (session == null || string.IsNullOrEmpty(session.ExePath) || !FileHelper.Exists(session.ExePath))
            throw new InvalidOperationException("Standalone Player exe missing.");

        FileHelper.TryDeleteDirectory(session.IsolatedPersistentRoot, true);

        string resultJson = FYAssetPathUtility.JoinFilePath(session.TargetDir, "player-runtime-standalone-result.json");
        string logPath = FYAssetPathUtility.JoinFilePath(session.TargetDir, "player-runtime-standalone.log");
        FileHelper.TryDelete(resultJson);

        var psi = new ProcessStartInfo
        {
            FileName = session.ExePath,
            Arguments =
                $"-batchmode -logFile \"{logPath}\" " +
                $"-fyassetE2EResult \"{resultJson}\" " +
                $"-fyassetE2EBackend AB " +
                $"-fyassetE2EExpectHotfix 0 " +
                $"-screen-width 640 -screen-height 360",
            WorkingDirectory = session.PlayerDir,
            UseShellExecute = false
        };

        using Process proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Failed to start Standalone Player.");
        if (!proc.WaitForExit(300000))
        {
            try { proc.Kill(); } catch { /* ignore */ }
            throw new InvalidOperationException("Standalone Player timed out. log=" + logPath);
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                "Standalone Player exit code " + proc.ExitCode + " log=" + logPath);
        if (!FileHelper.Exists(resultJson))
            throw new InvalidOperationException("Standalone Player result missing: " + resultJson);
    }

    private static BuildTestResult RunHotfixE2E(BuildTestRequest request, string runRoot)
    {
        // retained Player：Full smoke -> Hotfix 发布 -> relaunch
        return RunForwardUpdateE2E(request, runRoot, "E2E-Hotfix", seedFullIfMissing: true);
    }

    private static BuildTestResult RunChainE2E(BuildTestRequest request, string runRoot)
    {
        // Chain 强制自建 Full，再走同一 retained-Player 前向热更节奏
        return RunForwardUpdateE2E(request, runRoot, "E2E-Chain", seedFullIfMissing: true, forceSeedFull: true);
    }

    private static BuildTestResult RunForwardUpdateE2E(
        BuildTestRequest request,
        string runRoot,
        string modeLabel,
        bool seedFullIfMissing,
        bool forceSeedFull = false)
    {
        var result = new BuildTestResult
        {
            Backend = request.Backend.ToString(),
            Mode = modeLabel,
            RunRoot = runRoot,
            RunId = Path.GetFileName(runRoot),
            ExitCode = BuildTestExitCodes.PreconditionFailed
        };

        BuildTestFixtures.EnsurePermanentFixtures();
        var targets = BuildTestState.FreezeTargets(request.Backend, request.TargetIds, request.ExternalConfirmIds);
        result.TargetSnapshots = targets;
        var recovery = BuildTestState.WriteRecovery(runRoot, request, targets);
        bool mutated = false;
        var sessions = new List<PlayerSession>();

        try
        {
            BuildTestState.SnapshotProject(runRoot, request.Backend);
            BuildTestState.SnapshotTargets(runRoot, targets);

            BuildBaseline fullHead;
            bool needSeed = forceSeedFull
                || (seedFullIfMissing && !TryGetHotfixBaseline(request, targets, out fullHead));
            if (!needSeed)
            {
                BuildTestAcceptance.RequireLocalFullIdentity(request.Backend, out fullHead);
                for (int i = 0; i < targets.Count; i++)
                    BuildTestAcceptance.RequireTargetFullIdentity(targets[i], request.Backend, fullHead);
            }
            else
            {
                BuildTestState.PrepareIsolatedFullProject(request.Backend);
                InvokeBuild(request.Backend, false);
                var fullAccept = new BuildTestAcceptance.AcceptanceContext
                {
                    Backend = request.Backend,
                    ExpectedVersion = "2.0.0"
                };
                BuildTestAcceptance.AcceptFull(fullAccept, result);
                BuildTestAcceptance.RequireLocalFullIdentity(request.Backend, out fullHead);
            }

            string fullVersion = fullHead.Version.GetReleaseVersionString();
            string backendName = BuildTestPaths.BackendSegment(request.Backend);

            // Full 发布 + Player Full smoke（保留 Player）
            bool allFullRuntimeOk = true;
            string firstFail = null;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var outcome = FindOrAddOutcome(result, target.TargetId);
                string tdir = BuildTestPaths.TargetDir(runRoot, target.TargetId);
                FileHelper.EnsureDirectory(tdir);
                try
                {
                    // Player 启动前 Target 必须暴露 Full 身份
                    BuildTestState.PublishHeadToTarget(
                        request.Backend,
                        target,
                        FYAssetPathUtility.JoinFilePath(tdir, "publish-full.json"));
                    outcome.PublishSuccess = true;
                    BuildTestState.ProbeTargetIdentity(
                        target,
                        backendName,
                        fullHead.PackageName,
                        fullVersion,
                        true,
                        FYAssetPathUtility.JoinFilePath(tdir, "probe-full.json"));
                    outcome.ProbeSuccess = true;

                    PlayerSession session = BuildPlayerSession(request.Backend, target, tdir);
                    sessions.Add(session);
                    LaunchPlayer(session, "full", expectHotfixContent: false, wipePersistent: true);
                }
                catch (Exception ex)
                {
                    allFullRuntimeOk = false;
                    firstFail ??= ex.Message;
                    outcome.Failure = ex.Message;
                    outcome.ProbeSuccess = false;
                }
            }

            if (!allFullRuntimeOk)
            {
                result.Passed = false;
                result.FirstFailure = firstFail ?? "Full Player phase failed";
                result.ExitCode = BuildTestExitCodes.RuntimeFailed;
                CleanupSessions(sessions);
                for (int i = 0; i < targets.Count; i++)
                    BuildTestState.RestoreTarget(runRoot, targets[i]);
                BuildTestState.RestoreProject(runRoot, request.Backend);
                result.RestorationSucceeded = true;
                return result;
            }

            // Hotfix 构建一次
            BuildTestFixtures.MutateHotfixFixture(request.Backend);
            mutated = true;
            InvokeBuild(request.Backend, true);

            VersionRecord versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(
                FYAssetSettings.Instance.VersionRecordPath);
            string expectedHotfix = versionData.CurrentVersion.GetReleaseVersionString();
            var hotfixAccept = new BuildTestAcceptance.AcceptanceContext
            {
                Backend = request.Backend,
                IsHotfix = true,
                ExpectedVersion = expectedHotfix,
                ExpectedParentVersion = fullVersion
            };
            BuildTestAcceptance.AcceptHotfix(hotfixAccept, result);

            BackendMode mode = BuildTestState.ToBackendMode(request.Backend);
            string channelKey = BuildBaselineStore.GetChannelKey(string.Empty, mode);
            BuildBaseline hotfixHead = BuildBaselineStore.LoadLatest(channelKey);
            string hotfixVersion = hotfixHead.Version.GetReleaseVersionString();

            // 发布 Hotfix + relaunch 同 Player/persistent
            bool allHotfixOk = true;
            for (int i = 0; i < sessions.Count; i++)
            {
                PlayerSession session = sessions[i];
                BuildTestTargetSnapshot target = targets.Find(t => t.TargetId == session.TargetId)
                    ?? targets[i];
                var outcome = FindOrAddOutcome(result, session.TargetId);
                try
                {
                    BuildTestState.PublishHeadToTarget(
                        request.Backend,
                        target,
                        FYAssetPathUtility.JoinFilePath(session.TargetDir, "publish-hotfix.json"));
                    outcome.PublishSuccess = true;
                    outcome.PublishedPackage = hotfixHead.PackageName;
                    outcome.PublishedVersion = hotfixVersion;
                    BuildTestState.ProbeTargetIdentity(
                        target,
                        backendName,
                        hotfixHead.PackageName,
                        hotfixVersion,
                        true,
                        FYAssetPathUtility.JoinFilePath(session.TargetDir, "probe-hotfix.json"));
                    outcome.ProbeSuccess = true;

                    // 同 Player 二进制 + 同隔离 persistent：验证前向热更
                    LaunchPlayer(session, "hotfix", expectHotfixContent: true, wipePersistent: false);

                    BuildTestState.RestoreTarget(runRoot, target);
                    outcome.RestoreSuccess = true;
                }
                catch (Exception ex)
                {
                    allHotfixOk = false;
                    firstFail ??= ex.Message;
                    outcome.Failure = ex.Message;
                    try
                    {
                        BuildTestState.RestoreTarget(runRoot, target);
                        outcome.RestoreSuccess = true;
                    }
                    catch (Exception rex)
                    {
                        outcome.RestoreSuccess = false;
                        result.ExitCode = BuildTestExitCodes.RestoreFailed;
                        throw new InvalidOperationException(ex.Message + " | restore: " + rex.Message, rex);
                    }
                }
            }

            CleanupSessions(sessions);
            if (mutated)
            {
                BuildTestFixtures.RestoreHotfixFixture(request.Backend);
                mutated = false;
            }
            BuildTestState.RestoreProject(runRoot, request.Backend);
            result.RestorationSucceeded = true;

            if (!allHotfixOk)
            {
                result.Passed = false;
                result.FirstFailure = firstFail;
                if (result.ExitCode == BuildTestExitCodes.Passed || result.ExitCode == BuildTestExitCodes.PreconditionFailed)
                    result.ExitCode = BuildTestExitCodes.RuntimeFailed;
            }
            else
            {
                result.Passed = true;
                result.ExitCode = BuildTestExitCodes.Passed;
            }
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FirstFailure = ex.Message;
            if (result.ExitCode == BuildTestExitCodes.Passed || result.ExitCode == BuildTestExitCodes.PreconditionFailed)
                result.ExitCode = BuildTestExitCodes.RuntimeFailed;
            try
            {
                CleanupSessions(sessions);
                if (mutated)
                    BuildTestFixtures.RestoreHotfixFixture(request.Backend);
                for (int i = 0; targets != null && i < targets.Count; i++)
                    BuildTestState.RestoreTarget(runRoot, targets[i]);
                BuildTestState.RestoreProject(runRoot, request.Backend);
                result.RestorationSucceeded = true;
            }
            catch (Exception rex)
            {
                result.RestorationSucceeded = false;
                result.ExitCode = BuildTestExitCodes.RestoreFailed;
                result.FirstFailure += " | restore: " + rex.Message;
            }
        }
        finally
        {
            BuildTestState.MarkRecoveryCompleted(runRoot, recovery, result.RestorationSucceeded);
        }

        return result;
    }

    private static bool TryGetHotfixBaseline(
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
        catch
        {
            fullHead = null;
            return false;
        }
    }

    private static BuildTestTargetOutcome FindOrAddOutcome(BuildTestResult result, string targetId)
    {
        if (result.TargetOutcomes == null)
            result.TargetOutcomes = new List<BuildTestTargetOutcome>();
        for (int i = 0; i < result.TargetOutcomes.Count; i++)
        {
            if (string.Equals(result.TargetOutcomes[i].TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                return result.TargetOutcomes[i];
        }

        var outcome = new BuildTestTargetOutcome { TargetId = targetId };
        result.TargetOutcomes.Add(outcome);
        return outcome;
    }

    private static void RunPlayerSmoke(
        BuildTestBackend backend,
        BuildTestTargetSnapshot target,
        string targetDir,
        string phase,
        bool expectHotfixContent,
        bool retain)
    {
        // Full-only：构建 + 启动 + 可选清理
        PlayerSession session = BuildPlayerSession(backend, target, targetDir);
        try
        {
            LaunchPlayer(session, phase, expectHotfixContent, wipePersistent: true);
        }
        finally
        {
            if (!retain)
                CleanupSessions(new List<PlayerSession> { session });
        }
    }

    private static PlayerSession BuildPlayerSession(
        BuildTestBackend backend,
        BuildTestTargetSnapshot target,
        string targetDir)
    {
        string playerDir = FYAssetPathUtility.JoinFilePath(targetDir, "player");
        FileHelper.EnsureDirectory(playerDir);
        string isolatedProjectName = "fyasset_e2e_" + Math.Abs(targetDir.GetHashCode()).ToString("x");
        string isolatedPersistentRoot = ResolveLocalLowProjectRoot(isolatedProjectName);
        string exe = FYAssetPathUtility.JoinFilePath(playerDir, "FYAssetE2E.exe");

        bool oldUseAb = FYAssetSettings.Instance.UseABBackend;
        bool oldStandalone = FYAssetSettings.Instance.StandaloneBuild;
        string oldAaUrl = FYAssetAASettings.Instance.HotfixUrl;
        string oldAbUrl = FYAssetABSettings.Instance.HotfixUrl;
        string oldProjectName = FYAssetSettings.Instance.ProjectName;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
        bool added = false;
        try
        {
            FYAssetSettings.Instance.UseABBackend = backend == BuildTestBackend.AB;
            // 保留调用方已设置的 StandaloneBuild（Standalone E2E bake 时为 true）
            FYAssetSettings.Instance.ProjectName = isolatedProjectName;
            if (backend == BuildTestBackend.AB)
                FYAssetABSettings.Instance.HotfixUrl = target.RuntimeUrl;
            else
                FYAssetAASettings.Instance.HotfixUrl = target.RuntimeUrl;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            EditorUtility.SetDirty(FYAssetAASettings.Instance);
            EditorUtility.SetDirty(FYAssetABSettings.Instance);
            AssetDatabase.SaveAssets();

            if (defines.IndexOf(CoordinatorDefine, StringComparison.Ordinal) < 0)
            {
                string next = string.IsNullOrEmpty(defines)
                    ? CoordinatorDefine
                    : defines + ";" + CoordinatorDefine;
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, next);
                added = true;
            }

            string[] scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);
            if (scenes == null || scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes in Build Settings for E2E Player.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result);
        }
        finally
        {
            if (added)
            {
                string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
                current = current.Replace(CoordinatorDefine, string.Empty)
                    .Replace(";;", ";")
                    .Trim(';');
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, current);
            }

            FYAssetSettings.Instance.UseABBackend = oldUseAb;
            FYAssetSettings.Instance.StandaloneBuild = oldStandalone;
            FYAssetSettings.Instance.ProjectName = oldProjectName;
            FYAssetAASettings.Instance.HotfixUrl = oldAaUrl;
            FYAssetABSettings.Instance.HotfixUrl = oldAbUrl;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            EditorUtility.SetDirty(FYAssetAASettings.Instance);
            EditorUtility.SetDirty(FYAssetABSettings.Instance);
            AssetDatabase.SaveAssets();
        }

        return new PlayerSession
        {
            TargetId = target.TargetId,
            TargetDir = targetDir,
            PlayerDir = playerDir,
            ExePath = exe,
            IsolatedProjectName = isolatedProjectName,
            IsolatedPersistentRoot = isolatedPersistentRoot,
            Backend = backend,
            TargetType = target.TargetType,
            PublicBaseUrl = target.PublicBaseUrl
        };
    }

    private static void LaunchPlayer(
        PlayerSession session,
        string phase,
        bool expectHotfixContent,
        bool wipePersistent)
    {
        if (session == null || string.IsNullOrEmpty(session.ExePath) || !FileHelper.Exists(session.ExePath))
            throw new InvalidOperationException("Player exe missing for phase " + phase);

        if (wipePersistent)
            FileHelper.TryDeleteDirectory(session.IsolatedPersistentRoot, true);

        if (session.TargetType == PushTargetType.LocalDirectory)
        {
            if (Uri.TryCreate(session.PublicBaseUrl, UriKind.Absolute, out Uri pub) && pub.Port > 0)
                LocalHotfixServerController.Port = pub.Port;
            var status = LocalHotfixServerController.Start();
            if (!status.IsRunning)
                throw new InvalidOperationException("Local server not running: " + status.Message);
        }

        string resultJson = FYAssetPathUtility.JoinFilePath(
            session.TargetDir, $"player-runtime-{phase}-result.json");
        string logPath = FYAssetPathUtility.JoinFilePath(
            session.TargetDir, $"player-runtime-{phase}.log");
        FileHelper.TryDelete(resultJson);

        var psi = new ProcessStartInfo
        {
            FileName = session.ExePath,
            Arguments =
                $"-batchmode -logFile \"{logPath}\" " +
                $"-fyassetE2EResult \"{resultJson}\" " +
                $"-fyassetE2EBackend {(session.Backend == BuildTestBackend.AB ? "AB" : "AA")} " +
                $"-fyassetE2EExpectHotfix {(expectHotfixContent ? "1" : "0")} " +
                $"-screen-width 640 -screen-height 360",
            WorkingDirectory = session.PlayerDir,
            UseShellExecute = false
        };

        using Process proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Failed to start Player phase=" + phase);
        if (!proc.WaitForExit(300000))
        {
            try { proc.Kill(); } catch { /* ignore */ }
            throw new InvalidOperationException("Player timed out phase=" + phase + " log=" + logPath);
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                "Player exit code " + proc.ExitCode + " phase=" + phase + " log=" + logPath);
        if (!FileHelper.Exists(resultJson))
            throw new InvalidOperationException("Player result missing phase=" + phase + ": " + resultJson);
    }

    private static void CleanupSessions(List<PlayerSession> sessions)
    {
        if (sessions == null)
            return;
        for (int i = 0; i < sessions.Count; i++)
        {
            PlayerSession s = sessions[i];
            if (s == null)
                continue;
            FileHelper.TryDeleteDirectory(s.PlayerDir, true);
            FileHelper.TryDeleteDirectory(s.IsolatedPersistentRoot, true);
        }
        sessions.Clear();
    }

    /// <summary>
    /// Windows LocalLow/{company}/{product}/{projectName}；与 RuntimePathManager.PersistentRoot 对齐。
    /// </summary>
    private static string ResolveLocalLowProjectRoot(string projectName)
    {
        string localLow = FYAssetPathUtility.JoinFilePath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow",
            PlayerSettings.companyName,
            PlayerSettings.productName);
        return FYAssetPathUtility.JoinFilePath(localLow, projectName);
    }

    private static void InvokeBuild(BuildTestBackend backend, bool hotfix)
    {
        if (backend == BuildTestBackend.AA)
        {
            if (hotfix) AABuildProjectManager.BuildHotfix();
            else AABuildProjectManager.BuildFullPackage();
            if (!AABuildProjectManager.LastBuildSuccess)
                throw new InvalidOperationException("AA build failed in E2E.");
        }
        else
        {
            if (hotfix) ABBuildProjectManager.BuildHotfix();
            else ABBuildProjectManager.BuildFullPackage();
            if (!ABBuildProjectManager.LastBuildSuccess)
                throw new InvalidOperationException("AB build failed in E2E.");
        }
    }

    private static void RetainE2E(BuildTestBackend backend, BuildTestMode mode, int keep)
    {
        string parent = FYAssetPathUtility.JoinFilePath(
            BuildTestPaths.TestRunsRoot,
            BuildTestPaths.BackendSegment(backend),
            "e2e",
            BuildTestPaths.ModeSegment(mode));
        if (!FileHelper.DirectoryExists(parent))
            return;
        string[] dirs = FileHelper.GetDirectories(parent);
        Array.Sort(dirs, (a, b) => Directory.GetCreationTimeUtc(b).CompareTo(Directory.GetCreationTimeUtc(a)));
        for (int i = keep; i < dirs.Length; i++)
        {
            if (BuildTestPaths.IsInsideTestRuns(dirs[i]))
                FileHelper.TryDeleteDirectory(dirs[i], true);
        }
    }
}
#endif

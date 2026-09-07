#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 恢复范围覆盖的纯判定自检：验证 Pending/Complete/AbsentBefore/失败重试 四类路径的恢复决策。
/// 不涉及任何文件写入，测量的仅是 BuildTestState.ClassifyScopeRestore 的决策表。
/// </summary>
public static class BuildTestRecoveryManifestSelfCheck
{
    [MenuItem("FYAsset/Tests/Build Recovery Manifest Self Check")]
    public static void Run()
    {
        Expect(
            BuildTestRecoveryScopeStates.SnapshotComplete,
            BuildTestRecoveryScopeStates.Restored,
            backupContentExists: true,
            BuildTestScopeRestoreAction.SkipAlreadyRestored,
            "已恢复范围必须跳过");

        Expect(
            BuildTestRecoveryScopeStates.Pending,
            BuildTestRecoveryScopeStates.None,
            backupContentExists: true,
            BuildTestScopeRestoreAction.FailRefuseDestructive,
            "快照不完整范围必须拒绝任何破坏性恢复");

        Expect(
            BuildTestRecoveryScopeStates.Pending,
            BuildTestRecoveryScopeStates.None,
            backupContentExists: false,
            BuildTestScopeRestoreAction.FailRefuseDestructive,
            "快照不完整即使无备份也必须拒绝（不可把缺备份当成原不存在）");

        Expect(
            BuildTestRecoveryScopeStates.SnapshotComplete,
            BuildTestRecoveryScopeStates.None,
            backupContentExists: false,
            BuildTestScopeRestoreAction.FailBackupMissing,
            "声明完整但备份缺失必须明确失败而非当作原不存在");

        Expect(
            BuildTestRecoveryScopeStates.SnapshotComplete,
            BuildTestRecoveryScopeStates.None,
            backupContentExists: true,
            BuildTestScopeRestoreAction.RestoreFromBackup,
            "完整快照走备份恢复");

        Expect(
            BuildTestRecoveryScopeStates.AbsentBefore,
            BuildTestRecoveryScopeStates.None,
            backupContentExists: false,
            BuildTestScopeRestoreAction.DeleteForAbsent,
            "快照前不存在 => 恢复 = 删除运行期新建内容");

        Expect(
            BuildTestRecoveryScopeStates.SnapshotComplete,
            BuildTestRecoveryScopeStates.RestoreFailed,
            backupContentExists: true,
            BuildTestScopeRestoreAction.RestoreFromBackup,
            "恢复失败范围必须允许按原快照状态重试");

        Debug.Log("[BuildTestRecoveryManifestSelfCheck] PASS");
    }

    private static void Expect(string snapshotState, string restoreState, bool backupContentExists, BuildTestScopeRestoreAction expected, string message)
    {
        var actual = BuildTestState.ClassifyScopeRestore(
            snapshotState,
            restoreState,
            backupContentExists);
        if (actual != expected)
            throw new InvalidOperationException(
                $"[BuildTestRecoveryManifestSelfCheck] {message}: expected={expected}, actual={actual}, snapshotState={snapshotState}, restoreState={restoreState}, backupContentExists={backupContentExists}");
    }
}
#endif

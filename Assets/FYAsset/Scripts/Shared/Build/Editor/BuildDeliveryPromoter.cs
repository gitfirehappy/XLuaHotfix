#if UNITY_EDITOR
using System;
using System.IO;

/// <summary>
/// attempt → 交付目录的原子 promote 工具（依赖同卷 Directory.Move）。
/// 唯一允许把任务产物移入 live 出口的机制；归 BuildProjectRunner 在交付 commit 阶段调用。
/// 两种用法：
/// - 新包目录（HotfixOutput/Packages/Build_*）：目标必须不存在，move 后仅需在失败时删除；
/// - 共享目录（StreamingAssets/Standalone）：先原子换出旧目录再换入新目录，Rollback 恢复旧目录。
/// 只负责目录交换本身；PackageIndex / baseline / VersionRecord 的补偿由调用方负责。
/// </summary>
public sealed class BuildDeliveryPromoteToken
{
    private readonly string _deliveryDir;
    private readonly string _recoveryDir;
    private readonly bool _hasRecovery;
    private readonly string _attemptRoot;
    private bool _settled;

    internal BuildDeliveryPromoteToken(string deliveryDir, string recoveryDir, bool hasRecovery, string attemptRoot)
    {
        _deliveryDir = deliveryDir;
        _recoveryDir = recoveryDir;
        _hasRecovery = hasRecovery;
        _attemptRoot = attemptRoot;
    }

    /// <summary>失败补偿：把交付目录恢复回 promote 前的状态，随后 token 失效。</summary>
    public void Rollback()
    {
        if (_settled)
            return;
        _settled = true;
        if (_hasRecovery)
        {
            if (Directory.Exists(_deliveryDir))
                Directory.Delete(_deliveryDir, true);
            Directory.Move(_recoveryDir, _deliveryDir);
            PruneAttemptRootIfEmpty();
            return;
        }

        // 新包场景：promote 前目标不存在，回滚即删除 promote 出来的目录。
        if (Directory.Exists(_deliveryDir))
            Directory.Delete(_deliveryDir, true);
        PruneAttemptRootIfEmpty();
    }

    /// <summary>交付成功：丢弃旧内容备份，token 失效。</summary>
    public void Commit()
    {
        if (_settled)
            return;
        _settled = true;
        if (_hasRecovery && Directory.Exists(_recoveryDir))
            Directory.Delete(_recoveryDir, true);
        PruneAttemptRootIfEmpty();
    }

    /// <summary>事务结束后清理空 attempt 根，避免空目录累积（有残留 attempt 时保留供人工检查）。</summary>
    private void PruneAttemptRootIfEmpty()
    {
        try
        {
            if (!string.IsNullOrEmpty(_attemptRoot)
                && Directory.Exists(_attemptRoot)
                && Directory.GetFileSystemEntries(_attemptRoot).Length == 0)
                Directory.Delete(_attemptRoot, true);
        }
        catch (Exception)
        {
            // best-effort：空的 attempt 根不影响正确性，留给下次清理。
        }
    }
}

public static class BuildDeliveryPromoter
{
    /// <summary>
    /// 把 attempt 目录原子移动到交付目录。attempt 必须位于 attempt 根之下，
    /// 避免把 live 目录误当 attempt 移走；两者必须同卷（项目目录约束下成立）。
    /// </summary>
    public static BuildDeliveryPromoteToken Promote(string attemptDir, string deliveryDir, string attemptRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(attemptDir))
            throw new InvalidOperationException("attempt 目录为空。");
        if (string.IsNullOrWhiteSpace(deliveryDir))
            throw new InvalidOperationException("交付目录为空。");

        string normalizedAttempt = Normalize(attemptDir);
        string normalizedDelivery = Normalize(deliveryDir);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedAttempt, normalizedDelivery, comparison))
            throw new InvalidOperationException("attempt 目录与交付目录不能相同: " + normalizedAttempt);

        string attemptRoot = Normalize(string.IsNullOrWhiteSpace(attemptRootOverride)
            ? BuildPathManager.AttemptPackagesRoot
            : attemptRootOverride);
        if (string.Equals(normalizedAttempt, attemptRoot, comparison)
            || !normalizedAttempt.StartsWith(attemptRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException(
                $"attempt 目录必须位于 AttemptPackagesRoot 之下。Root: {attemptRoot}, Actual: {normalizedAttempt}");
        if (!Directory.Exists(normalizedAttempt))
            throw new InvalidOperationException("attempt 目录不存在: " + normalizedAttempt);

        EnsureParent(normalizedDelivery);

        // 共享目录场景：旧目录原子换出到 attempt 根旁边的唯一 rollback 槽位。
        if (Directory.Exists(normalizedDelivery))
        {
            string recoveryDir = Normalize(FYAssetPathUtility.JoinFilePath(
                attemptRoot,
                "_rollback-" + Path.GetFileName(normalizedDelivery) + "-" + Guid.NewGuid().ToString("N")));
            Directory.Move(normalizedDelivery, recoveryDir);
            try
            {
                Directory.Move(normalizedAttempt, normalizedDelivery);
            }
            catch
            {
                // 新目录未能就位时先还原旧目录，避免中间态长期存在。
                Directory.Move(recoveryDir, normalizedDelivery);
                throw;
            }
            return new BuildDeliveryPromoteToken(normalizedDelivery, recoveryDir, hasRecovery: true, attemptRoot);
        }

        Directory.Move(normalizedAttempt, normalizedDelivery);
        return new BuildDeliveryPromoteToken(normalizedDelivery, recoveryDir: null, hasRecovery: false, attemptRoot);
    }

    private static void EnsureParent(string dir)
    {
        string parent = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(parent))
            FileHelper.EnsureDirectory(parent);
    }

    private static string Normalize(string path) =>
        FYAssetPathUtility.NormalizePath(Path.GetFullPath(path)).TrimEnd('/', '\\');
}
#endif

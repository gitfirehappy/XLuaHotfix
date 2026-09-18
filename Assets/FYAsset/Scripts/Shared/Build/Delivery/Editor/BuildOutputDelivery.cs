#if UNITY_EDITOR
using System;
using System.IO;

/// <summary>
/// 一次成功的临时输出到最终输出目录的目录迁移。调用方必须显式 Commit 或 Rollback。
/// </summary>
public sealed class BuildDeliveryResult
{
    private readonly string _finalOutputDir;
    private readonly string _recoveryDir;
    private readonly bool _hasRecovery;
    private bool _settled;

    internal BuildDeliveryResult(string finalOutputDir, string recoveryDir, bool hasRecovery)
    {
        _finalOutputDir = finalOutputDir;
        _recoveryDir = recoveryDir;
        _hasRecovery = hasRecovery;
    }

    public void Commit()
    {
        if (_settled)
            return;
        _settled = true;
        if (_hasRecovery && Directory.Exists(_recoveryDir))
            Directory.Delete(_recoveryDir, true);
    }

    public void Rollback()
    {
        if (_settled)
            return;
        _settled = true;
        if (_hasRecovery)
        {
            if (Directory.Exists(_finalOutputDir))
                Directory.Delete(_finalOutputDir, true);
            Directory.Move(_recoveryDir, _finalOutputDir);
            return;
        }

        if (Directory.Exists(_finalOutputDir))
            Directory.Delete(_finalOutputDir, true);
    }
}

public static class BuildOutputDelivery
{
    public static BuildDeliveryResult Deliver(string temporaryOutputDir, string finalOutputDir)
    {
        if (string.IsNullOrWhiteSpace(temporaryOutputDir))
            throw new InvalidOperationException("临时构建输出目录为空。");
        if (string.IsNullOrWhiteSpace(finalOutputDir))
            throw new InvalidOperationException("最终构建输出目录为空。");

        string temporary = Normalize(temporaryOutputDir);
        string final = Normalize(finalOutputDir);
        if (string.Equals(temporary, final, PathComparison()))
            throw new InvalidOperationException("临时输出目录与最终输出目录不能相同: " + temporary);
        EnsureTemporaryOutput(temporary);
        if (!Directory.Exists(temporary))
            throw new DirectoryNotFoundException("临时构建输出目录不存在: " + temporary);

        string temporaryRoot = Normalize(BuildPathManager.TemporaryPackagesRoot);
        string recoveryRoot = FYAssetPathUtility.JoinFilePath(temporaryRoot, "_recovery");
        string recovery = Normalize(FYAssetPathUtility.JoinFilePath(recoveryRoot, Path.GetFileName(final)));
        if (Directory.Exists(recovery))
            throw new InvalidOperationException("发现未结算的构建输出恢复目录，拒绝覆盖: " + recovery);

        string parent = Path.GetDirectoryName(final);
        if (!string.IsNullOrEmpty(parent))
            FileHelper.EnsureDirectory(parent);

        bool hasRecovery = Directory.Exists(final);
        if (hasRecovery)
        {
            FileHelper.EnsureDirectory(recoveryRoot);
            Directory.Move(final, recovery);
        }

        try
        {
            Directory.Move(temporary, final);
        }
        catch
        {
            if (hasRecovery && Directory.Exists(recovery) && !Directory.Exists(final))
                Directory.Move(recovery, final);
            throw;
        }

        return new BuildDeliveryResult(final, recovery, hasRecovery);
    }

    private static void EnsureTemporaryOutput(string temporary)
    {
        string root = Normalize(BuildPathManager.TemporaryPackagesRoot);
        string prefix = root.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        if (string.Equals(temporary, root, PathComparison())
            || !temporary.StartsWith(prefix, PathComparison()))
            throw new InvalidOperationException(
                $"临时输出目录必须位于 TemporaryPackagesRoot 之下。Root={root}, Actual={temporary}");
    }

    private static StringComparison PathComparison()
    {
        return Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string Normalize(string path) =>
        FYAssetPathUtility.NormalizePath(Path.GetFullPath(path)).TrimEnd('/', '\\');
}
#endif

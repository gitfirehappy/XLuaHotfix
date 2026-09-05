#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;

/// <summary>
/// Build Test 路径与结果目录管理。
/// </summary>
public static class BuildTestPaths
{
    public static string ProjectRoot => BuildPathManager.ProjectRoot;

    public static string TestRunsRoot =>
        FYAssetPathUtility.JoinFilePath(BuildPathManager.OutputRoot, "TestRuns");

    public static string BackendSegment(BuildTestBackend backend) =>
        backend == BuildTestBackend.AB ? "AB" : "AA";

    public static string ModeSegment(BuildTestMode mode) => mode.ToString().ToLowerInvariant();

    public static string CreateRunRoot(BuildTestBackend backend, BuildTestMode mode, string runId = null)
    {
        runId ??= DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string root = FYAssetPathUtility.JoinFilePath(
            TestRunsRoot,
            BackendSegment(backend),
            "build",
            ModeSegment(mode),
            runId);
        FileHelper.EnsureDirectory(root);
        FileHelper.EnsureDirectory(FYAssetPathUtility.JoinFilePath(root, "targets"));
        FileHelper.EnsureDirectory(FYAssetPathUtility.JoinFilePath(root, "backup", "project"));
        FileHelper.EnsureDirectory(FYAssetPathUtility.JoinFilePath(root, "backup", "targets"));
        return root;
    }

    public static string ResultJson(string runRoot) =>
        FYAssetPathUtility.JoinFilePath(runRoot, "result.json");

    public static string RecoveryJson(string runRoot) =>
        FYAssetPathUtility.JoinFilePath(runRoot, "recovery.json");

    public static string TargetDir(string runRoot, string targetId) =>
        FYAssetPathUtility.JoinFilePath(runRoot, "targets", Sanitize(targetId));

    public static string ProjectBackupRoot(string runRoot) =>
        FYAssetPathUtility.JoinFilePath(runRoot, "backup", "project");

    public static string TargetsBackupRoot(string runRoot) =>
        FYAssetPathUtility.JoinFilePath(runRoot, "backup", "targets");

    public static void RetainLatest(BuildTestBackend backend, BuildTestMode mode, int keep = 20)
    {
        string parent = FYAssetPathUtility.JoinFilePath(
            TestRunsRoot,
            BackendSegment(backend),
            "build",
            ModeSegment(mode));
        if (!FileHelper.DirectoryExists(parent))
            return;

        string[] dirs = FileHelper.GetDirectories(parent)
            .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
            .ToArray();
        for (int i = keep; i < dirs.Length; i++)
        {
            if (!IsInsideTestRuns(dirs[i]))
                continue;
            FileHelper.TryDeleteDirectory(dirs[i], true);
        }
    }

    public static bool IsInsideTestRuns(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        string normalized = FYAssetPathUtility.NormalizePath(path);
        string root = FYAssetPathUtility.NormalizePath(TestRunsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   FYAssetPathUtility.NormalizePath(TestRunsRoot),
                   normalized,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureOwnedPath(string path, string ownedRoot)
    {
        string normalized = FYAssetPathUtility.NormalizePath(path);
        string root = FYAssetPathUtility.NormalizePath(ownedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                FYAssetPathUtility.NormalizePath(ownedRoot),
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsafe path outside owned root. Path={path}, Root={ownedRoot}");
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "target";
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }
}
#endif

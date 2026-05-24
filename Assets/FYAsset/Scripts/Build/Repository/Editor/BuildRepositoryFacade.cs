#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build Repository 的入口门面。
/// 统一管理 channelKey、git 元数据和 repository 调用，避免上层直接碰文件布局。
/// </summary>
public static class BuildRepositoryFacade
{
    private static readonly IBuildRepository Repository = new FileBuildRepository();

    public static string GetChannelKey(VersionNumber version, BackendMode backendMode)
    {
        string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        string channelRoot = string.IsNullOrEmpty(version?.Channel)
            ? buildTarget
            : $"{buildTarget}-{version.Channel}";
        return $"{channelRoot}/{GetBackendSegment(backendMode)}";
    }

    public static string GetChannelKey(string channel, BackendMode backendMode)
    {
        string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        string channelRoot = string.IsNullOrEmpty(channel)
            ? buildTarget
            : $"{buildTarget}-{channel}";
        return $"{channelRoot}/{GetBackendSegment(backendMode)}";
    }

    public static RepositoryStatus GetStatus(string channelKey)
    {
        return Repository.GetStatus(channelKey);
    }

    public static RepositoryStatus GetStatus(BuildPackageRequest request)
    {
        return GetStatus(GetChannelKey(request));
    }

    public static string GetChannelKey(BuildPackageRequest request)
    {
        return GetChannelKey(request?.Version, request != null ? request.BackendMode : BackendMode.AA);
    }

    public static RepositoryCommit GetHeadCommit(string channelKey)
    {
        return Repository.GetHeadCommit(channelKey);
    }

    public static RepositoryCommit GetHeadCommit(VersionNumber version, BackendMode backendMode)
    {
        return Repository.GetHeadCommit(GetChannelKey(version, backendMode));
    }

    public static ArtifactDelta DiffHead(string channelKey, IArtifactScanner scanner)
    {
        return Repository.DiffHead(channelKey, scanner?.Scan() ?? new List<ArtifactDigest>());
    }

    public static ArtifactDelta DiffHead(BuildPackageRequest request, IArtifactScanner scanner)
    {
        return DiffHead(GetChannelKey(request), scanner);
    }

    public static ArtifactDelta DiffCommits(string channelKey, VersionNumber fromVersion, VersionNumber toVersion)
    {
        return Repository.DiffCommits(channelKey, fromVersion, toVersion);
    }

    public static List<RepositoryCommit> ListCommits(string channelKey)
    {
        return Repository.ListCommits(channelKey);
    }

    public static void Commit(BuildPackageRequest request, IArtifactScanner scanner)
    {
        Commit(request, scanner, null);
    }

    public static void Commit(BuildPackageRequest request, IArtifactScanner scanner, string backendMode)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var gitInfo = GetGitInfo(BuildPathManager.ProjectRoot);
        var commit = new RepositoryCommit
        {
            Version = request.Version,
            ChannelKey = GetChannelKey(request),
            BackendMode = backendMode ?? GetBackendSegment(request.BackendMode),
            BuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            PackageName = request.PackageName,
            CreatedAtUtc = request.CreatedAt.ToUniversalTime().ToString("o"),
            GitCommitHash = gitInfo.commitHash,
            IsDirty = gitInfo.isDirty,
            PackageRootDir = request.OutputDir,
            Artifacts = scanner?.Scan() ?? new List<ArtifactDigest>()
        };

        Repository.Commit(commit);
    }

    public static List<RepositoryCommit> ListCommits(BuildPackageRequest request)
    {
        return Repository.ListCommits(GetChannelKey(request));
    }

    public static PushReceipt Push(string channelKey, VersionNumber fromVersion, VersionNumber toVersion, IPushTarget target)
    {
        return Repository.Push(channelKey, fromVersion, toVersion, target);
    }

    public static List<PushHistoryEntry> ListPushHistory(string channelKey)
    {
        return Repository.ListPushHistory(channelKey);
    }

    private static string GetBackendSegment(BackendMode backendMode)
    {
        return backendMode == BackendMode.ABManifest ? "AB" : "AA";
    }

    private static (string commitHash, bool isDirty) GetGitInfo(string workingDirectory)
    {
        try
        {
            string commitHash = RunGit(workingDirectory, "rev-parse", "HEAD").Trim();
            string status = RunGit(workingDirectory, "status", "--porcelain");
            return (commitHash, !string.IsNullOrWhiteSpace(status));
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[BuildRepositoryFacade] git metadata unavailable: {ex.Message}");
            return (string.Empty, false);
        }
    }

    private static string RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Arguments = string.Join(" ", args);

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("git process failed to start.");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "git command failed." : error.Trim());
        return output;
    }
}
#endif

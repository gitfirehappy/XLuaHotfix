#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build Repository 的入口门面。
/// 统一管理 channelKey、git metadata 和 repository 调用，避免上层直接碰 BuildData/Snapshots 文件布局。
/// </summary>
public static class BuildRepositoryFacade
{
    private static readonly IBuildRepository Repository = new FileBuildRepository();

    public static string GetChannelKey(VersionNumber version, BackendMode backendMode)
    {
        // ChannelKey 同时隔离 BuildTarget、业务 channel 和 backend，避免 AA/AB HEAD 串线。
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

    public static List<RepositoryCommit> ListCommits(string channelKey)
    {
        return Repository.ListCommits(channelKey);
    }

    public static void Commit(BuildPackageRequest request, System.Collections.Generic.IReadOnlyList<ArtifactDigest> artifacts)
    {
        Commit(request, artifacts, null);
    }

    public static void Commit(BuildPackageRequest request, System.Collections.Generic.IReadOnlyList<ArtifactDigest> artifacts, string backendMode)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var gitInfo = GetGitInfo(BuildPathManager.ProjectRoot);
        var commit = new RepositoryCommit
        {
            Version = request.Version,
            ChannelKey = GetChannelKey(request),
            BackendMode = backendMode ?? GetBackendSegment(request.BackendMode),
            BuildType = request.BuildType.ToString(),
            BuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            PackageName = request.PackageName,
            CreatedAtUtc = request.CreatedAt.ToUniversalTime().ToString("o"),
            GitCommitHash = gitInfo.commitHash,
            IsDirty = gitInfo.isDirty,
            PackageRootDir = request.OutputDir,
            Artifacts = artifacts != null ? new List<ArtifactDigest>(artifacts) : new List<ArtifactDigest>()
        };

        UnityEngine.Debug.Log($"[{nameof(BuildRepositoryFacade)}] 写入 Repository commit: Channel={commit.ChannelKey}, Version={commit.Version.GetFullVersionString()}, Backend={commit.BackendMode}, BuildType={commit.BuildType}, Artifacts={commit.Artifacts.Count}, Dirty={commit.IsDirty}");
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
            UnityEngine.Debug.LogWarning($"[{nameof(BuildRepositoryFacade)}] 读取 git metadata 失败，Repository commit 会继续写入但 GitCommitHash 为空。原因: {ex.Message}");
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

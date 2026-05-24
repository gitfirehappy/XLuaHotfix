#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// Build Repository 统一接口。
/// 当前只保留文件系统实现，AA/AB 差异通过扫描器输入。
/// </summary>
public interface IBuildRepository
{
    RepositoryStatus GetStatus(string channelKey);
    RepositoryCommit GetHeadCommit(string channelKey);
    List<RepositoryCommit> ListCommits(string channelKey);
    ArtifactDelta DiffHead(string channelKey, IReadOnlyList<ArtifactDigest> artifacts);
    ArtifactDelta DiffCommits(string channelKey, VersionNumber fromVersion, VersionNumber toVersion);
    void Commit(RepositoryCommit commit);
    PushReceipt Push(string channelKey, VersionNumber fromVersion, VersionNumber toVersion, IPushTarget target);
    List<PushHistoryEntry> ListPushHistory(string channelKey);
}
#endif

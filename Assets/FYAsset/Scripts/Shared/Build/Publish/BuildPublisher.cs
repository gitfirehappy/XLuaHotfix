#if UNITY_EDITOR
using System;

/// <summary>
/// 最新交付的发布器：从 baseline 组装负载并调用 push target。
/// Push 的唯一职责：把已构建完成的包体目录发布到远端镜像（CDN / 本地目录 / 云端服务器）。
/// </summary>
public static class BuildPublisher
{
    public static PushReceipt PushLatest(string channelKey, IPushTarget target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        BuildBaseline release = BuildBaselineStore.LoadLatest(channelKey);
        if (release == null)
        {
            return new PushReceipt
            {
                Success = false,
                TargetId = target.Id,
                FailureReason = $"baseline 不存在，先成功完成一次构建: {channelKey}"
            };
        }

        if (string.IsNullOrEmpty(release.PackageRootDir))
        {
            return new PushReceipt
            {
                Success = false,
                TargetId = target.Id,
                FailureReason = $"baseline 缺少 PackageRootDir: {channelKey}"
            };
        }

        return target.Push(new PushPayload { Release = release });
    }
}
#endif

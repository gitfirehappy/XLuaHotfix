#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// 将完整包发布到 {service root}/{AA|AB}。
/// </summary>
public sealed class LocalDirectoryPushTarget : IPushTarget
{
    private readonly PushTargetConfig _config;

    public string Id => string.IsNullOrEmpty(_config.Id) ? "local" : _config.Id;

    public LocalDirectoryPushTarget(PushTargetConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public PushReceipt Push(PushPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Release == null)
            throw new ArgumentException("Release 不能为空。", nameof(payload));
        if (string.IsNullOrEmpty(payload.Release.PackageRootDir))
            return Fail("PackageRootDir is empty.", string.Empty);

        string publishRoot;
        try
        {
            publishRoot = PushTargetUtility.ResolveBackendRoot(_config, payload.Release.BackendMode);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, string.Empty);
        }

        try
        {
            using var transaction = new PackagePublishTransaction(
                payload.Release,
                payload.Release.PackageRootDir,
                publishRoot);
            transaction.Apply();
            transaction.Commit();

            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = publishRoot,
                PushedAtUtc = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LocalDirectoryPushTarget] Push 失败：{ex}");
            return Fail(ex.Message, publishRoot);
        }
    }

    private PushReceipt Fail(string reason, string location)
    {
        return new PushReceipt
        {
            Success = false,
            TargetId = Id,
            TargetLocation = location,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }
}
#endif

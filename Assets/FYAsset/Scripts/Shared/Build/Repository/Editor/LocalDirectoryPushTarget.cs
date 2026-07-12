#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// Publishes a complete package under {service root}/{AA|AB}.
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
        if (payload.ToCommit == null)
            throw new ArgumentException("ToCommit 不能为空。", nameof(payload));
        if (string.IsNullOrEmpty(payload.ToCommit.PackageRootDir))
            return Fail("PackageRootDir is empty.", string.Empty);

        string publishRoot;
        try
        {
            publishRoot = PushTargetUtility.ResolveBackendRoot(_config, payload.ToCommit.BackendMode);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, string.Empty);
        }

        try
        {
            using var transaction = new PackagePublishTransaction(
                payload.ToCommit,
                payload.ToCommit.PackageRootDir,
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
            Debug.LogError($"[LocalDirectoryPushTarget] Push failed: {ex}");
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

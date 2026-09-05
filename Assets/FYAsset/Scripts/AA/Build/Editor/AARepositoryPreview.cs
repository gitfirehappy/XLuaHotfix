#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AA Diff Preview 入口：运行 whitelist 管线到 AA diff Task，读取 ArtifactDelta。
/// 不写 baseline 或 PackageIndex。
/// </summary>
public static class AARepositoryPreview
{
    public static ArtifactDelta Run(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetAASettings.Instance.BuildPipelineConfigPath);
        if (config == null)
            throw new InvalidOperationException("AA BuildPipelineConfig 为 null。");

        Debug.Log($"[{nameof(AARepositoryPreview)}] 开始 AA Diff Preview，Pipeline stop-after={config.HotfixDiffTaskName}。Package={request.PackageName}");
        ArtifactDelta delta = BuildPreviewRunner.Run(config, request, null);
        Debug.Log($"[{nameof(AARepositoryPreview)}] AA Diff Preview 完成：Added={(delta != null ? delta.Added.Count : 0)}, Modified={(delta != null ? delta.Modified.Count : 0)}, Removed={(delta != null ? delta.Removed.Count : 0)}");
        return delta;
    }
}

/// <summary>AA preview 数据源 adapter：供共享 Repository 面板注入。</summary>
public sealed class AARepositoryPreviewProvider : IRepositoryPreviewProvider
{
    public bool SupportsDeliveryPreview => false;

    public ArtifactDelta RunChangesPreview(BuildPackageRequest request)
    {
        return AARepositoryPreview.Run(request);
    }

    public RepositoryDeliveryPreview RunDeliveryPreview(BuildPackageRequest request)
    {
        return null;
    }
}
#endif

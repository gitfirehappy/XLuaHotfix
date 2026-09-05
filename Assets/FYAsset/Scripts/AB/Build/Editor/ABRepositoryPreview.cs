#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AB preview 的组合结果。
/// HeadDelta 表示 current-vs-latest-baseline；DeliveryBundles 表示 current-vs-Full-baseline 的 Hotfix 交付列表。
/// </summary>
[Serializable]
public sealed class ABRepositoryPreviewResult
{
    public ArtifactDelta HeadDelta = new ArtifactDelta();
    public List<ManifestBundleEntry> DeliveryBundles = new();
    public long DeliverySizeBytes;
    public bool DeliveryAvailable;
    public string DeliveryMessage;
}

/// <summary>
/// AB Diff/Delivery Preview 入口：临时输出目录运行 whitelist 管线到 AB diff Task。
/// 不写 baseline 或 PackageIndex，临时目录在 finally 清理。
/// </summary>
public static class ABRepositoryPreview
{
    public static ABRepositoryPreviewResult RunDiffPreview(BuildPackageRequest request)
    {
        return RunInternal(request, deliveryPreview: false);
    }

    public static ABRepositoryPreviewResult RunDeliveryPreview(BuildPackageRequest request)
    {
        return RunInternal(request, deliveryPreview: true);
    }

    private static ABRepositoryPreviewResult RunInternal(BuildPackageRequest request, bool deliveryPreview)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string previewRoot = FYAssetPathUtility.JoinFilePath(
            BuildPathManager.ProjectRoot,
            "Temp",
            deliveryPreview ? "BuildRepositoryDeliveryPreview" : "BuildRepositoryPreview",
            Guid.NewGuid().ToString("N"));
        string previewBuildRoot = FYAssetPathUtility.JoinFilePath(previewRoot, "build");
        string label = deliveryPreview ? "AB Delivery Preview" : "AB Diff Preview";

        try
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
            FileHelper.EnsureDirectory(previewBuildRoot);

            var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
                FYAssetABSettings.Instance.BuildPipelineConfigPath);
            if (config == null)
                throw new InvalidOperationException("BuildPipelineConfig 为 null。");

            Debug.Log($"[{nameof(ABRepositoryPreview)}] {label} start，临时输出目录: {previewBuildRoot}");
            List<ManifestBundleEntry> deliveryBundles = null;
            ArtifactDelta delta = BuildPreviewRunner.Run(config, request,
                ctx =>
                {
                    ctx.Set(BuildContextKeys.RepositoryPreviewOutput, previewBuildRoot);
                    if (deliveryPreview)
                        ctx.Set(ABBuildContextKeys.ABDeliveryPreviewMode, true);
                },
                ctx =>
                {
                    deliveryBundles = ctx.Get<List<ManifestBundleEntry>>(ABBuildContextKeys.ABDeliveryBundles)
                        ?? new List<ManifestBundleEntry>();
                });

            Debug.Log($"[{nameof(ABRepositoryPreview)}] {label} 完成：Added={(delta != null ? delta.Added.Count : 0)}, Modified={(delta != null ? delta.Modified.Count : 0)}, Removed={(delta != null ? delta.Removed.Count : 0)}, Delivery={deliveryBundles.Count}");
            return new ABRepositoryPreviewResult
            {
                HeadDelta = delta,
                DeliveryBundles = deliveryBundles,
                DeliverySizeBytes = SumDeliverySize(deliveryBundles),
                DeliveryAvailable = deliveryPreview,
                DeliveryMessage = deliveryPreview
                    ? "Hotfix Delivery is loaded."
                    : "Hotfix Delivery is not loaded. Use Preview Delivery to calculate current output vs Full baseline."
            };
        }
        finally
        {
            FileHelper.TryDeleteDirectory(previewRoot, true);
            Debug.Log($"[{nameof(ABRepositoryPreview)}] {label} 临时目录已清理: {previewRoot}");
        }
    }

    private static long SumDeliverySize(IReadOnlyList<ManifestBundleEntry> deliveryBundles)
    {
        long total = 0;
        if (deliveryBundles == null)
            return total;
        for (int i = 0; i < deliveryBundles.Count; i++)
            total += deliveryBundles[i] != null ? deliveryBundles[i].FileSize : 0;
        return total;
    }
}

/// <summary>AB preview 数据源 adapter：供共享 Repository 面板注入。</summary>
public sealed class ABRepositoryPreviewProvider : IRepositoryPreviewProvider
{
    public bool SupportsDeliveryPreview => true;

    public ArtifactDelta RunChangesPreview(BuildPackageRequest request)
    {
        ABRepositoryPreviewResult r = ABRepositoryPreview.RunDiffPreview(request);
        return r != null ? r.HeadDelta : new ArtifactDelta();
    }

    public RepositoryDeliveryPreview RunDeliveryPreview(BuildPackageRequest request)
    {
        ABRepositoryPreviewResult r = ABRepositoryPreview.RunDeliveryPreview(request);
        if (r == null)
            return null;
        return new RepositoryDeliveryPreview
        {
            HeadDelta = r.HeadDelta,
            DeliveryBundleCount = r.DeliveryBundles != null ? r.DeliveryBundles.Count : 0,
            DeliverySizeBytes = r.DeliverySizeBytes,
            DeliveryAvailable = r.DeliveryAvailable,
            DeliveryMessage = r.DeliveryMessage
        };
    }
}
#endif

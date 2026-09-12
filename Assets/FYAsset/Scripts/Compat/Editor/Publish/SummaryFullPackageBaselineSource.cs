#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// 编辑器侧基准 Full 解析：按发布包身份读取正式 Summary，再按 BaseFullSummaryId → ArtifactRelativePath 定位包目录。
/// </summary>
/// <remarks>
/// 按发布包身份读取正式 Summary，并通过基准 Summary 的 ArtifactRelativePath 定位目录；不扫描目录猜测包身份。
/// </remarks>
public sealed class SummaryFullPackageBaselineSource : IFullPackageBaselineSource
{
    /// <summary>无状态实现，可直接共享。</summary>
    public static readonly SummaryFullPackageBaselineSource Instance = new SummaryFullPackageBaselineSource();

    /// <summary>编辑器加载时注册为默认解析入口。</summary>
    [InitializeOnLoadMethod]
    private static void RegisterOnLoad()
    {
        FullPackageBaselineSourceRegistry.Register(Instance);
    }

    public bool TryResolveBaselinePackageDir(PublishRequest request, out string packageDir, out string error)
    {
        packageDir = string.Empty;
        error = string.Empty;

        if (request?.Identity == null || string.IsNullOrEmpty(request.Identity.PackageName))
        {
            error = "发布身份缺失，无法定位基准 Full 摘要";
            return false;
        }

        string backend = string.IsNullOrEmpty(request.Identity.BackendId) ? request.BackendKey : request.Identity.BackendId;
        if (string.IsNullOrEmpty(backend))
        {
            error = "后端标识缺失，无法定位基准 Full 摘要";
            return false;
        }

        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        string baseFullSummaryId = request.BaseFullSummaryId;
        if (string.IsNullOrEmpty(baseFullSummaryId))
        {
            if (!store.TryReadSummaryDocument(
                    backend, request.Identity.PackageName, out CompleteBuildSummary.SummaryDocument hotfix, out string hotfixError))
            {
                error = $"本次 Hotfix 的正式摘要不可读: {hotfixError}";
                return false;
            }

            baseFullSummaryId = hotfix?.BaseFullSummaryId;
            if (string.IsNullOrEmpty(baseFullSummaryId))
            {
                error = $"正式摘要未记录基准 Full: {request.Identity.PackageName}";
                return false;
            }
        }

        if (!store.TryReadSummaryDocument(
                backend, baseFullSummaryId, out CompleteBuildSummary.SummaryDocument full, out string fullError))
        {
            error = $"基准 Full 摘要不可读: {fullError}";
            return false;
        }

        if (string.IsNullOrEmpty(full?.ArtifactRelativePath))
        {
            error = $"基准 Full 摘要缺少制品路径: {baseFullSummaryId}";
            return false;
        }

        string candidate = FYAssetPathUtility.ResolveFilePath(
            FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot),
            full.ArtifactRelativePath);
        if (!FileHelper.DirectoryExists(candidate))
        {
            error = $"基准 Full 包目录不存在: {candidate}";
            return false;
        }

        packageDir = candidate;
        return true;
    }
}
#endif

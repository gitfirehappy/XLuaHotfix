#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// 编辑器侧基准 Full 解析：按发布包身份读取正式 Summary，再按 BaseFullSummaryId → ArtifactRelativePath 定位包目录。
/// </summary>
/// <remarks>
/// 计划 T7 的本地 Full fallback 事实链（只读，不扫描目录猜测身份）：
/// 1. 正式摘要的 BuildId 即包名，因此用发布包身份就能读到本次 Hotfix 的摘要；
/// 2. Hotfix 摘要的 BaseFullSummaryId 指向基准 Full 摘要（请求显式提供时优先使用该事实）；
/// 3. 基准 Full 摘要的 ArtifactRelativePath 是项目根下的制品路径，摘要/路径/目录任一不可用即视为来源不足；
/// 4. 编辑器加载时自注册到 <see cref="FullPackageBaselineSourceRegistry"/>，发布入口无需改动即可获得补齐能力。
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

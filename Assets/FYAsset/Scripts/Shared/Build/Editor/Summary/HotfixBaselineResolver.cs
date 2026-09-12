#if UNITY_EDITOR
using System;
using System.IO;

/// <summary>
/// Hotfix 的基准 Full 解析：从 Summary Index 的作用域定位最近成功 Full，并给出其包目录。
/// </summary>
/// <remarks>
/// 只读取构建事实（Summary/Index）与制品位置，不扫描目录猜测身份；
/// 基准缺失、摘要不可读或制品不存在时返回失败，由调用方阻断构建。
/// </remarks>
public static class HotfixBaselineResolver
{
    public static bool TryResolve(
        string backendKey,
        string platform,
        string channel,
        out string packageDir,
        out CompleteBuildSummary.SummaryDocument fullSummary,
        out string error)
    {
        packageDir = string.Empty;
        fullSummary = null;
        error = string.Empty;

        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        if (!store.TryReadIndex(out BuildSummaryIndex index, out string indexError))
        {
            index = store.RebuildIndex();
            if (index.Scopes == null || index.Scopes.Count == 0)
            {
                error = $"Summary Index 不可用且无法重建: {indexError}";
                return false;
            }
        }

        BuildSummaryScope scope = index.FindScope(backendKey, platform, channel ?? string.Empty);
        if (scope == null || string.IsNullOrEmpty(scope.LatestFullSummaryId))
        {
            error = $"作用域 {backendKey}/{platform}/{BuildVersionPlanner.DisplayChannel(channel)} 没有成功 Full 基准。";
            return false;
        }

        if (!store.TryReadSummaryDocument(backendKey, scope.LatestFullSummaryId, out fullSummary, out string readError))
        {
            error = $"基准 Full 摘要不可读: {readError}";
            return false;
        }

        if (string.IsNullOrEmpty(fullSummary.ArtifactRelativePath))
        {
            error = $"基准 Full 摘要缺少制品路径: {scope.LatestFullSummaryId}";
            return false;
        }

        string root = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        string candidate = FYAssetPathUtility.NormalizePath(
            Path.Combine(root, fullSummary.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!Directory.Exists(candidate))
        {
            error = $"基准 Full 制品缺失: {candidate}";
            return false;
        }

        packageDir = candidate;
        return true;
    }
}
#endif

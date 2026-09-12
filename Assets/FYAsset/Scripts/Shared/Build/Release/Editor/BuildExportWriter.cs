#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 构建导出写入器：后端 Export 阶段共用的模式输出落盘逻辑。
/// </summary>
/// <remarks>
/// 模式输出契约：
/// Full 与 Standalone 在包目录写 BuildIndex（RuntimeMode 由 BuildIndex.RuntimeMode 承载）；
/// Hotfix 不写 BuildIndex，避免覆盖安装包已经交付的启动标记。
/// 构建摘要是构建事实，由 BuildProjectRunner 在交付事务中写入 BuildData/Summaries，不进入包目录。
/// </remarks>
public static class BuildExportWriter
{
    /// <summary>按 Context 建立摘要骨架：身份、类型、模式、版本、平台与开始时间。</summary>
    public static CompleteBuildSummary CreateSummary(BuildContext context, DateTime startedAtUtc)
    {
        var request = context.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        BuildConfig config = context.Require<BuildConfig>(BuildContextKeys.BuildConfig);

        return new CompleteBuildSummary
        {
            BuildId = request.PackageName,
            BackendId = config.BackendKey,
            BuildType = request.BuildType,
            RuntimeMode = CompleteBuildSummary.ResolveRuntimeMode(request.BuildType),
            Version = request.Version,
            Platform = config.TargetPlatform.ToString(),
            StartedAt = startedAtUtc,
            Duration = DateTime.UtcNow - startedAtUtc,
            Success = true,
            Statistics = new BuildStatistics()
        };
    }

    /// <summary>把校验结果转成摘要消息：Error/Warning 都保留，Success 由调用方按最终状态覆盖。</summary>
    public static void AddVerificationMessages(CompleteBuildSummary summary, BuildContext context)
    {
        BuildVerificationResult verification = context.Get<BuildVerificationResult>(BuildContextKeys.BuildVerificationResult);
        if (verification?.Issues == null)
            return;

        for (int i = 0; i < verification.Issues.Count; i++)
        {
            VerificationIssue issue = verification.Issues[i];
            if (issue == null)
                continue;

            summary.AddMessage(issue.Level == IssueLevel.Error
                ? BuildMessage.Error(issue.CheckName, issue.Message, issue.BundleName)
                : BuildMessage.Warning(issue.CheckName, issue.Message, issue.BundleName));
        }
    }

    /// <summary>
    /// 模式输出：Full/Standalone 在包目录写入 BuildIndex，Hotfix 跳过。
    /// 返回写入路径；跳过时返回 null。
    /// </summary>
    public static string WritePackageBuildIndex(BuildContext context, string outputDir)
    {
        var request = context.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        if (request.BuildType == BuildType.Hotfix)
        {
            Debug.Log($"[{nameof(BuildExportWriter)}] Hotfix 构建不写包内 BuildIndex，安装包标记保持不变。");
            return null;
        }

        BuildIndexData buildIndexData = LocalBuildDataExporter.CreateBuildIndexData(request);
        string path = FYAssetPathUtility.JoinFilePath(outputDir, FYAssetSettings.BUILD_INDEX_FILENAME);
        FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(buildIndexData, true));
        return path;
    }

    /// <summary>把内容条目（文件名/Hash/CRC/大小）追加进摘要文件清单。</summary>
    public static void AddContentFileDigests(CompleteBuildSummary summary, IReadOnlyList<SummaryContentFact> contents)
    {
        if (contents == null)
            return;

        for (int i = 0; i < contents.Count; i++)
        {
            SummaryContentFact content = contents[i];
            if (content == null || string.IsNullOrEmpty(content.FileName))
                continue;

            summary.AddFile(new FileHelper.FileDigest(content.FileName, content.FileHash, content.FileCRC, content.FileSize));
        }
    }

    /// <summary>摘要文件清单的最小事实来源；AA/AB 各自的清单条目都能映射到它。</summary>
    public sealed class SummaryContentFact
    {
        public string FileName;
        public string FileHash;
        public uint FileCRC;
        public long FileSize;
    }
}
#endif

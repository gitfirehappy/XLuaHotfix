using System;
using System.Collections.Generic;

/// <summary>
/// 一次发布请求的完整输入事实：发布哪个包、按哪份清单校验。
/// Target 能力由发布入口单独注入，服务器事实由目录事务读取。
/// </summary>
public sealed class PublishRequest
{
    /// <summary>后端标识（AA/AB）；用于目录隔离与 PackageIndex.BackendMode。</summary>
    public string BackendKey;

    /// <summary>本地待发布包目录（包根，含清单与内容文件）。</summary>
    public string SourcePackageDir;

    /// <summary>后端清单读取器；发布流程据此读取服务器与本地包的内容集合。</summary>
    public IPackageManifestReader ManifestReader;

    /// <summary>包身份；由调用方从正式 Summary 解析后注入，包目录不承载身份。</summary>
    public PackageBuildIdentity Identity;

    /// <summary>
    /// 稀疏 Hotfix 的基准 Full 包解析入口。服务器事实不可用且目标清单缺少本地内容时，
    /// 发布方必须用它取回未变化内容的字节。
    /// </summary>
    public IFullPackageBaselineSource FullPackageBaselineSource;

    /// <summary>本次 Hotfix 的基准 Full 摘要标识；为空时由解析入口按包身份读取 Summary。</summary>
    public string BaseFullSummaryId;

    /// <summary>服务器包目录集合名，必须与运行时下载布局一致。</summary>
    public string PackagesFolderName = "Packages";

    /// <summary>解析发布身份：只能来自调用方注入的正式 Summary 事实。</summary>
    public bool TryResolveIdentity(out PackageBuildIdentity identity, out string error)
    {
        error = string.Empty;
        identity = Identity;
        if (identity == null || string.IsNullOrEmpty(identity.PackageName))
        {
            error = "发布必须由正式 Summary 提供包身份。";
            return false;
        }

        if (!PackageBuildIdentity.TryParsePackageName(identity.PackageName, out _, out _, out string packageNameError))
        {
            error = packageNameError;
            return false;
        }

        if (!string.IsNullOrEmpty(BackendKey) && !identity.MatchesBackend(BackendKey))
        {
            error = $"发布身份后端不匹配。请求={BackendKey}, 摘要={identity.BackendId}";
            return false;
        }

        return true;
    }

    /// <summary>请求的必填项是否齐全；<paramref name="error"/> 说明缺失项。</summary>
    public bool Validate(out string error)
    {
        var problems = new List<string>();
        if (string.IsNullOrEmpty(BackendKey))
            problems.Add(nameof(BackendKey));
        if (string.IsNullOrEmpty(SourcePackageDir))
            problems.Add(nameof(SourcePackageDir));
        if (ManifestReader == null)
            problems.Add(nameof(ManifestReader));
        if (!PublishPathGuard.IsSafeSegment(PackagesFolderName))
            problems.Add($"PackagesFolderName（{PackagesFolderName}）必须是单个安全目录段");

        if (problems.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        error = "发布请求无效: " + string.Join(", ", problems);
        return false;
    }
}

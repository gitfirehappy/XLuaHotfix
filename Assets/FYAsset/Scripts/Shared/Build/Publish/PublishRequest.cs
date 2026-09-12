using System;
using System.Collections.Generic;

/// <summary>
/// 一次发布请求的完整输入事实：发布哪个包、发布到哪、按哪份清单校验。
/// </summary>
/// <remarks>
/// 计划 T6 要求发布只依赖显式事实：
/// 1. <see cref="SourcePackageDir"/> 由调用方给出（来自所选正式 Summary 的制品路径）；
/// 2. 包身份由调用方从正式 Summary 解析后注入，不在包目录或源目录中推断；
/// 3. 服务器事实（PackageIndex 与其指向的 Manifest）由发布事务自己读取，本类型只携带访问入口。
/// </remarks>
public sealed class PublishRequest
{
    /// <summary>后端标识（AA/AB）；用于目录隔离与 PackageIndex.BackendMode</summary>
    public string BackendKey;

    /// <summary>本地待发布包目录（包根，含清单与内容文件）</summary>
    public string SourcePackageDir;

    /// <summary>发布目标 Id，仅用于回执与发布缓存隔离</summary>
    public string TargetId;

    /// <summary>后端清单读取器；发布流程据此读取服务器与本地包的内容集合</summary>
    public IPackageManifestReader ManifestReader;

    /// <summary>包身份；由发布 UI 从正式 Summary 解析后注入，包目录不承载身份。</summary>
    public PackageBuildIdentity Identity;

    /// <summary>本地发布缓存文件路径；为空表示不启用发布缓存</summary>
    public string PublishCachePath;

    /// <summary>
    /// 稀疏 Hotfix 的基准 Full 包解析入口；为空时用编辑器侧注册的实现。
    /// 服务器事实不可用且目标清单声明的内容不在本地包内时，发布方必须用它取回未变化内容的字节。
    /// </summary>
    public IFullPackageBaselineSource FullPackageBaselineSource;

    /// <summary>
    /// 本次 Hotfix 的基准 Full 摘要标识（可直接来自 Summary）；为空时由解析入口按包身份读取 Summary。
    /// </summary>
    public string BaseFullSummaryId;

    /// <summary>
    /// 服务器包目录集合名，必须与运行时读取远端包时使用的
    /// <c>FYAssetSettings.BuildPackagesFolderName</c> 一致：发布布局与下载布局是同一份契约。
    /// </summary>
    public string PackagesFolderName = "Packages";

    /// <summary>
    /// 解析本次发布的包身份：只能来自调用方注入的正式 Summary 事实。
    /// </summary>
    public bool TryResolveIdentity(out PackageBuildIdentity identity, out string error)
    {
        error = string.Empty;
        identity = Identity;
        if (identity == null || string.IsNullOrEmpty(identity.PackageName))
        {
            error = "发布必须由正式 Summary 提供包身份。";
            return false;
        }

        if (!identity.IsSafePackageName())
        {
            error = $"包名不是合法目录名: '{identity.PackageName}'";
            return false;
        }

        if (!string.IsNullOrEmpty(BackendKey) && !identity.MatchesBackend(BackendKey))
        {
            error = $"发布身份后端不匹配。请求={BackendKey}, 摘要={identity.BackendId}";
            return false;
        }

        return true;
    }

    /// <summary>构造源的只读快照，避免发布过程中被调用方改写。</summary>
    public PublishRequest Clone()
    {
        return new PublishRequest
        {
            BackendKey = BackendKey,
            SourcePackageDir = SourcePackageDir,
            TargetId = TargetId,
            ManifestReader = ManifestReader,
            Identity = Identity,
            PublishCachePath = PublishCachePath,
            FullPackageBaselineSource = FullPackageBaselineSource,
            BaseFullSummaryId = BaseFullSummaryId,
            PackagesFolderName = PackagesFolderName
        };
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

    /// <summary>目标解析出的服务器后端根目录；目录型目标为空时发布退化为完整上传。</summary>
    public string ResolveServerRootOrNull()
    {
        return string.IsNullOrWhiteSpace(_serverBackendRoot) ? null : _serverBackendRoot;
    }

    private string _serverBackendRoot;

    /// <summary>由目录型目标在发布前写入服务器后端根；不由调用方直接赋值。</summary>
    public void UseServerRoot(string serverBackendRoot)
    {
        _serverBackendRoot = serverBackendRoot;
    }

    /// <summary>本地发布缓存的默认目录（项目内，独立于构建缓存与构建摘要）。</summary>
    public const string PublishCacheFolderName = "BuildData/PublishCache";

    /// <summary>按后端与目标推导发布缓存路径；路径片段非法时返回 null。</summary>
    public static string ResolvePublishCachePath(string projectRoot, string backendKey, string targetId)
    {
        if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(backendKey) || string.IsNullOrEmpty(targetId))
            return null;

        if (IsUnsafeSegment(backendKey) || IsUnsafeSegment(targetId))
            return null;

        return FYAssetPathUtility.JoinFilePath(
            projectRoot,
            PublishCacheFolderName.Replace('/', System.IO.Path.DirectorySeparatorChar),
            backendKey.ToUpperInvariant(),
            targetId + ".json");
    }

    private static bool IsUnsafeSegment(string value)
    {
        return value.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '&', '*', '"', '<', '>', '|' }) >= 0;
    }
}

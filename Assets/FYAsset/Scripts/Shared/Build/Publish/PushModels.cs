#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// 已组装完成、等待上传的发布负载。
/// </summary>
/// <remarks>
/// 发布事务负责组装与校验，上传目标只负责搬运：
/// <see cref="StagedPackageDir"/> 是校验通过的新包目录，
/// <see cref="PackageIndexJson"/> 是必须**最后**上传的新 PackageIndex 内容。
/// </remarks>
[Serializable]
public sealed class PushPayload
{
    /// <summary>本次发布的请求事实（包身份、源目录、后端）</summary>
    public PublishRequest Request;

    /// <summary>校验通过的待上传包目录</summary>
    public string StagedPackageDir;

    /// <summary>新 PackageIndex 的 JSON 内容；非空时必须在包内容上传完成后写入</summary>
    public string PackageIndexJson;

    /// <summary>服务器根下的包目录集合名，运行时按同一名字读取远端包</summary>
    public string PackagesFolderName = "Packages";

    /// <summary>已知的服务器包目录集合绝对路径（目录型目标提供，可为空）</summary>
    public string ServerPackagesRoot;
}

/// <summary>
/// 发布执行结果。
/// </summary>
[Serializable]
public sealed class PushReceipt
{
    public bool Success;
    public string TargetId;
    public string TargetLocation;
    public string PushedAtUtc;
    public string FailureReason;

    /// <summary>发布过程说明（服务器事实、复用统计、退化原因等）</summary>
    public List<string> Messages = new();

    /// <summary>本次是否退化为完整上传。</summary>
    public bool DegradedToFullUpload;

    /// <summary>本次是否由本地基准 Full 包补齐目标内容（稀疏 Hotfix 被组装成完整目标包）。</summary>
    public bool AssembledFromBaselineFull;

    /// <summary>参与补齐的本地基准 Full 包目录；未使用时为空。</summary>
    public string BaselineFullPackageDir = string.Empty;

    /// <summary>本地新增/修改并写入服务器的文件数量。</summary>
    public int UploadedCount;

    /// <summary>复用服务器已有内容的文件数量。</summary>
    public int ReusedCount;
}

/// <summary>
/// Push 目标抽象。
/// </summary>
public interface IPushTarget
{
    string Id { get; }

    /// <summary>把已组装好的完整包目录上传到目标；不支持目录级事实访问的目标走这条路径。</summary>
    PushReceipt Push(PushPayload payload);
}

/// <summary>
/// 目录型目标能力：服务器内容可以用本地目录表达。
/// </summary>
/// <remarks>
/// 计划 T7 的发布流程需要读取服务器 PackageIndex 与其指向的 Manifest，并能在服务器根下组装隔离目录。
/// 只有具备该能力的目录型目标（本地目录镜像、挂载盘等）才能执行完整发布事务；
/// 其他目标（例如 HTTP/CLI 型部署）无法读取服务器事实，发布方按“完整上传”退化，
/// 但仍然保证 PackageIndex 在最后上传。
/// </remarks>
public interface IDirectoryPushTarget : IPushTarget
{
    /// <summary>解析该后端的服务器根目录（可读写）；无法解析时返回 null。</summary>
    string ResolveBackendRoot(string backendKey);
}
#endif

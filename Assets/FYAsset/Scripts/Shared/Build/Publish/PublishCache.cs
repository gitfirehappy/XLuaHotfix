using System;
using System.Collections.Generic;

/// <summary>
/// 发布缓存：本地上一次发布过的文件摘要集合，按后端与目标隔离，独立于构建缓存与构建摘要。
/// </summary>
/// <remarks>
/// 定位（计划 T7）：辅助与双重检测，**不能替代服务器事实**。
/// 发布流程以服务器 PackageIndex + Manifest 为唯一事实来源；
/// 本缓存只用于解释“本次为什么上传了这些文件”以及提示缓存与服务器事实的漂移，
/// 因此缓存缺失、损坏或过期都不会改变发布结果。
/// </remarks>
[Serializable]
public sealed class PublishCache
{
    /// <summary>后端标识（AA/AB）</summary>
    public string BackendId;

    /// <summary>发布目标 Id</summary>
    public string TargetId;

    /// <summary>上一次发布完成后服务器包目录内的文件摘要</summary>
    public List<FileDigest> Files = new();

    /// <summary>上一次发布写入的包名，仅用于日志对比</summary>
    public string LastPackageName;

    /// <summary>上一次发布完成时间（UTC，ISO 8601）</summary>
    public string PublishedAtUtc;
}

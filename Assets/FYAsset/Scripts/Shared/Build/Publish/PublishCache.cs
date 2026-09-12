using System;
using System.Collections.Generic;

/// <summary>
/// 发布缓存：本地上一次发布过的文件摘要集合，按后端与目标隔离，独立于构建缓存与构建摘要。
/// </summary>
/// <remarks>
/// 发布结果以服务器 PackageIndex 和 Manifest 为准；缓存只用于解释上传内容和提示漂移，不改变发布结果。
/// </remarks>
[Serializable]
public sealed class PublishCache
{
    /// <summary>后端标识（AA/AB）</summary>
    public string BackendId;

    /// <summary>发布目标 Id</summary>
    public string TargetId;

    /// <summary>上一次发布完成后服务器包目录内的文件摘要</summary>
    public List<FileHelper.FileDigest> Files = new();

    /// <summary>上一次发布写入的包名，仅用于日志对比</summary>
    public string LastPackageName;

    /// <summary>上一次发布完成时间（UTC，ISO 8601）</summary>
    public string PublishedAtUtc;
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bundle 构建压缩模式。
/// </summary>
public enum BundleCompression
{
    /// <summary>ChunkBasedCompression — 默认，运行时加载最快</summary>
    LZ4 = 0,

    /// <summary>LZMA — 文件最小，但需整包解压</summary>
    LZMA = 1,

    /// <summary>无压缩 — 构建最快但体积大</summary>
    Uncompressed = 2
}

/// <summary>
/// Bundle 文件名的生成风格。
/// </summary>
public enum BundleFileNameStyle
{
    /// <summary>pkg_group_packKey.bundle</summary>
    BundleName = 0,

    /// <summary>{MD5}.bundle</summary>
    HashName = 1,

    /// <summary>pkg_group_packKey_{MD5}.bundle（默认）</summary>
    BundleName_HashName = 2
}

/// <summary>
/// 构建管线配置 ScriptableObject —— 定义 Task 编排、文件名风格等管线执行选项。
/// 后端模式由 concrete build manager 的 BuildPackageRequest 决定；UseABBackend 只保留为兼容/UI glue。
/// 存储路径：Assets/Build/BuildPipelineConfig.asset。
/// </summary>
public class BuildPipelineConfig : ScriptableObject
{
    /// <summary>Bundle 文件名格式</summary>
    public BundleFileNameStyle FileNameStyle = BundleFileNameStyle.BundleName_HashName;

    /// <summary>Bundle 构建压缩模式（默认 LZ4）</summary>
    public BundleCompression BundleCompression = BundleCompression.LZ4;

    /// <summary>Task 顺序列表；列表顺序就是执行顺序</summary>
    public List<TaskEntry> Tasks = new();

    /// <summary>本后端 Hotfix Diff Task 名（Repository 预览管线的 stop-after 目标）。</summary>
    public string HotfixDiffTaskName;
}

/// <summary>
/// SO 中存储的 Task 顺序条目。TaskName 作为索引键，
/// BuildTaskResolver 按名查找对应 IBuildTask 实现并实例化。
/// 类名变更不影响已有 SO 数据（名称来自程序集扫描，非硬编码 ClassName）。
/// </summary>
[Serializable]
public class TaskEntry
{
    /// <summary>Task 唯一标识，匹配 IBuildTask.TaskName</summary>
    public string TaskName;
}

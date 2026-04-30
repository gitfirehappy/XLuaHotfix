using System;
using System.Collections.Generic;
using UnityEngine;

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
/// 构建管线配置 ScriptableObject —— 定义 Task 编排、后端模式、文件名风格等全局选项。
/// 存储路径：Assets/Build/BuildPipelineConfig.asset。
/// </summary>
[CreateAssetMenu(fileName = "BuildPipelineConfig", menuName = "XLua/BuildPipelineConfig")]
public class BuildPipelineConfig : ScriptableObject
{
    /// <summary>默认后端模式</summary>
    public BackendMode DefaultBackendMode = BackendMode.ABManifest;

    /// <summary>Bundle 文件名格式</summary>
    public BundleFileNameStyle FileNameStyle = BundleFileNameStyle.BundleName_HashName;

    /// <summary>Debug 回退模式：true → 忽略批并发，按拓扑序逐个串行执行</summary>
    public bool SequentialMode;

    /// <summary>Task 编排列表，调度器仅执行 Enabled=true 的条目</summary>
    public List<TaskEntry> Tasks = new();
}

/// <summary>
/// SO 中存储的 Task 编排条目。TaskName 作为索引键，
/// BuildTaskResolver 按名查找对应 IBuildTask 实现并实例化。
/// 类名变更不影响已有 SO 数据（名称来自程序集扫描，非硬编码 ClassName）。
/// </summary>
[Serializable]
public class TaskEntry
{
    /// <summary>Task 唯一标识，匹配 IBuildTask.TaskName</summary>
    public string TaskName;

    /// <summary>是否启用：骨干节点强制 true；扩展节点默认 false</summary>
    public bool Enabled = true;

    /// <summary>前置依赖的 TaskName 列表</summary>
    public List<string> DependsOn;
}

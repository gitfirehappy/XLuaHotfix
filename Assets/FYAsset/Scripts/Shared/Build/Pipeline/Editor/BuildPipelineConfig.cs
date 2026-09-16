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
/// 构建管线配置 ScriptableObject —— 只保存构建选项与自定义 Task 的插入位置。
/// 主干阶段由各后端 PipelineBackbone 固定定义，配置不能增删主干。
/// 后端键由 concrete build manager 的 BuildRequest 决定；Shared 不读取项目级后端选择配置。
/// 存储路径：Assets/Build/BuildPipelineConfig.asset 与 Assets/Build/AABuildPipelineConfig.asset。
/// </summary>
public class BuildPipelineConfig : ScriptableObject
{
    /// <summary>Bundle 构建压缩模式（默认 LZ4）</summary>
    public BundleCompression BundleCompression = BundleCompression.LZ4;

    /// <summary>自定义 Task 列表；每条声明 TaskName 与插入槽位 Slot，列表顺序即同槽内的执行顺序</summary>
    public List<CustomTaskEntry> Tasks = new();

    /// <summary>
    /// 追加一条自定义 Task 配置。参数写入 TaskName 与 Slot 两个序列化字段，
    /// 供编辑器面板、配置升级与测试夹具构造合法条目。
    /// </summary>
    public void AddCustomTask(string taskName, string slot)
    {
        Tasks.Add(new CustomTaskEntry
        {
            TaskName = taskName,
            Slot = slot
        });
    }
}

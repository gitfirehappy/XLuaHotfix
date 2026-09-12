using System;

/// <summary>
/// 管线配置中的自定义 Task 条目：TaskName 是要解析的 IBuildTask 名称，
/// Slot 是它插入的主干阶段名（语义为“插入到该槽主干任务之前”）。
/// 主干阶段本身不由配置声明，只由各后端 PipelineBackbone 定义。
/// </summary>
/// <remarks>
/// 字段声明顺序是 TaskName 在前、Slot 在后，与既有 .asset 的 YAML 形状保持一致（旧数据只有 TaskName）。
/// 字段必须可写：Unity 序列化不处理 readonly 字段，声明为 readonly 会导致既有配置升级后丢失槽位。
/// </remarks>
[Serializable]
public struct CustomTaskEntry
{
    /// <summary>自定义 Task 唯一标识，匹配 IBuildTask.TaskName</summary>
    public string TaskName;

    /// <summary>插入位置：目标主干阶段名，或 BuildPipelineComposer 的 Input/Output 槽</summary>
    public string Slot;

    public override string ToString()
    {
        return (TaskName ?? "<empty>") + "@" + (Slot ?? "<empty>");
    }
}

using UnityEngine.UIElements;

/// <summary>
/// 构建执行面板。DAG 可视化已归属 PipelinePanel，本面板预留给后续构建触发与状态展示。
/// </summary>
public class BuilderPanel : BuildPipelineUIToolkitPanel
{
    public override string PanelName => "Build";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateBody("构建入口预留。"));
    }
}

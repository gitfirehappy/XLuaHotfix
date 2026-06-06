using UnityEngine.UIElements;

/// <summary>
/// AB Pipeline 构建结果占位面板。
/// 真实报告数据模型、读取与展示由后续专项计划实现。
/// </summary>
public sealed class ABReportPanel : BuildPipelineUIToolkitPanel
{
    public override string PanelName => "AB 构建结果";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateTitle("AB 构建结果预留"));
        panel.Add(CreateBody("构建结果报告将在后续专项计划中补充。"));
    }
}

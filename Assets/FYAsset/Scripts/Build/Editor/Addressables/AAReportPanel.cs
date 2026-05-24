using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline 报告占位面板。
/// </summary>
public sealed class AAReportPanel : BuildPipelineUIToolkitPanel
{
    public override string PanelName => "AA 报告";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateTitle("AA 报告预留"));
        panel.Add(CreateBody("Diff 与报告详情后续补充。"));
    }
}

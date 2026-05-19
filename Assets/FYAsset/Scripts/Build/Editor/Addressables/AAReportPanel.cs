using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline 报告占位面板。
/// </summary>
public sealed class AAReportPanel : BuildPipelineUIToolkitPanel
{
    public override string PanelName => "AA Report";

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateTitle("AA report view is reserved."));
        panel.Add(CreateBody("Diff and report details will be added after E7 lands."));
    }
}

/// <summary>
/// AA Pipeline 构建结果面板。
/// </summary>
public sealed class AAReportPanel : BuildPipelineUIToolkitPanel
{
    private readonly BuildPackageResultsView _packageResults = new();

    public override string PanelName => "AA Build Results";

    public override void SetVisible(bool visible)
    {
        if (visible)
            _packageResults.Refresh();
    }

    protected override void BuildContent(UnityEngine.UIElements.VisualElement root)
    {
        _packageResults.Build(root);
    }
}

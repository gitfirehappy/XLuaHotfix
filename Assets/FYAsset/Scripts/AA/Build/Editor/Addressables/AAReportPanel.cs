using UnityEditor;
using UnityEngine.UIElements;

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

    protected override void BuildContent(VisualElement root)
    {
        _packageResults.Build(root);
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton(
            "Open Addressables Report",
            () => EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Addressables Report"),
            176f));
        root.Insert(0, toolbar);
    }
}

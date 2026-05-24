using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// 占位面板，用于尚未实现的 Pipeline / Builder / Inspector / Settings 区域。
/// </summary>
public sealed class PlaceholderPanel : BuildPipelineUIToolkitPanel
{
    private readonly string _panelName;

    public PlaceholderPanel(string panelName)
    {
        _panelName = panelName;
    }

    public override string PanelName => _panelName;

    public override void OnEnable(EditorWindow window)
    {
        base.OnEnable(window);
    }

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateTitle(_panelName + " - 预留"));
    }
}

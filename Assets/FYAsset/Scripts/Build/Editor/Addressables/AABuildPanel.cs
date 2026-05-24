using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline Task 图验收面板。
/// </summary>
public sealed class AABuildPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly PipelinePanel _pipelinePanel = new PipelinePanel(
        "AA 构建",
        () => FYAssetSettings.Instance.AAPipelineConfigPath,
        BuildPipelineBackbone.CreateAATasks,
        "AABuildPanel",
        false,
        true);

    public string PanelName => "AA 构建";

    public void OnEnable(EditorWindow window)
    {
        _pipelinePanel.OnEnable(window);
    }

    public VisualElement CreateContent()
    {
        return _pipelinePanel.CreateContent();
    }

    public void OnDisable()
    {
        _pipelinePanel.OnDisable();
    }

    public void SetVisible(bool visible)
    {
        _pipelinePanel.SetVisible(visible);
    }
}

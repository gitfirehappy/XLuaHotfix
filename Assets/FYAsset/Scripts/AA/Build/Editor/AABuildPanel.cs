using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline Task 图验收面板。
/// </summary>
public sealed class AABuildPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly PipelinePanel _pipelinePanel = new(
        "AA Build",
        () => FYAssetAASettings.Instance.BuildPipelineConfigPath,
        AAPipelineBackbone.CreateDefaultTasks,
        "AABuildPanel",
        false,
        true,
        new BuildPanelActions
        {
            BuildFull = AABuildProjectManager.BuildFullPackage,
            BuildHotfix = AABuildProjectManager.BuildHotfix,
            LastBuildSuccess = () => AABuildProjectManager.LastBuildSuccess,
        });

    public string PanelName => "AA Build";

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

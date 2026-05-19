using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline 构建占位面板。
/// </summary>
public sealed class AABuildPanel : BuildPipelineUIToolkitPanel
{
    private string _lastBuildTime = string.Empty;
    private Label _lastBuildTimeLabel;

    public override string PanelName => "AA Build";

    public override void OnEnable(EditorWindow window)
    {
        LoadLastBuildTime();
        base.OnEnable(window);
        RefreshLabels();
    }

    protected override void BuildContent(VisualElement root)
    {
        VisualElement panel = CreateCenteredPanel(root);
        panel.Add(CreateTitle("AA build entry is reserved."));
        _lastBuildTimeLabel = CreateBody(string.Empty);
        panel.Add(_lastBuildTimeLabel);
        panel.Add(CreateBody("Build trigger will be added in a later sub-plan."));
    }

    private void LoadLastBuildTime()
    {
        VersionDataBase versionData = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        _lastBuildTime = versionData != null ? versionData.LastBuildTime : string.Empty;
    }

    private void RefreshLabels()
    {
        if (_lastBuildTimeLabel == null)
            return;

        _lastBuildTimeLabel.text = string.IsNullOrEmpty(_lastBuildTime)
            ? "Last build time: (not available)"
            : "Last build time: " + _lastBuildTime;
    }
}

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// AB backend build settings panel.
/// </summary>
public sealed class ABConfigPanel : IBuildPipelinePanel
{
    private ABBuildSettings _buildSettings;
    private SerializedObject _buildSettingsSo;
    private VisualElement _root;

    public string PanelName => "AB 配置";

    public void OnEnable(EditorWindow window)
    {
        LoadBuildSettings();
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;
        Rebuild();
        return _root;
    }

    public void OnDisable()
    {
        _root?.Unbind();
        _root = null;
        _buildSettings = null;
        _buildSettingsSo = null;
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarLabel("AB"));
        toolbar.Add(BuildPipelineUI.ToolbarButton("刷新", () =>
        {
            LoadBuildSettings();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(ABBuildSettings.DEFAULT_ASSET_PATH));
        _root.Add(toolbar);

        DrawBuildSettings();
    }

    private void LoadBuildSettings()
    {
        _buildSettings = FYAssetBuildSettingsProvider.AB;
        _buildSettingsSo = _buildSettings != null ? new SerializedObject(_buildSettings) : null;
    }

    private void DrawBuildSettings()
    {
        if (_buildSettingsSo == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("AB Build Settings"));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.BuildPipelineConfigPath)), "Pipeline Config Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.ManifestOutputFormat))));
        card.Add(BuildPipelineUI.ByteSizeField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.MaxHotfixSizeBytes)), "Max Hotfix Size"));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.AssetCollectionDataFolder)), "Asset Collection Data Folder", BuildPipelineUI.PathPickerMode.AssetFolder));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.AssetCollectionSettingPath)), "AssetCollectionSetting Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(ABBuildSettings.DependencyFilterExtensions)), "Dependency Filter Extensions"));
        card.Bind(_buildSettingsSo);
        _root.Add(card);
    }
}

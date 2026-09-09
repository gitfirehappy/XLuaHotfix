using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>
/// AB backend 构建设置面板。
/// </summary>
public sealed class ABConfigPanel : IBuildPipelinePanel
{
    private FYAssetABSettings _buildSettings;
    private SerializedObject _buildSettingsSo;
    private VisualElement _root;

    public string PanelName => "AB Config";

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
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
        {
            LoadBuildSettings();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(FYAssetABSettings.DEFAULT_ASSET_PATH));
        _root.Add(toolbar);

        DrawBuildSettings();
    }

    private void LoadBuildSettings()
    {
        _buildSettings = FYAssetABSettings.Instance;
        _buildSettingsSo = _buildSettings != null ? new SerializedObject(_buildSettings) : null;
    }

    private void DrawBuildSettings()
    {
        if (_buildSettingsSo == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("AB Settings"));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.HotfixUrl))));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.HotfixMaxRetryCount))));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.HotfixRetryBaseDelaySeconds))));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.BuildPipelineConfigPath)), "Pipeline Config Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.ManifestOutputFormat))));
        card.Add(BuildPipelineUI.ByteSizeField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.MaxHotfixSizeBytes)), "Max Hotfix Size"));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.AssetCollectionDataFolder)), "Asset Collection Data Folder", BuildPipelineUI.PathPickerMode.AssetFolder));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.AssetCollectionSettingPath)), "AssetCollectionSetting Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetABSettings.DependencyFilterExtensions)), "Dependency Filter Extensions"));
        card.Bind(_buildSettingsSo);
        _root.Add(card);

        DrawPlayMode();
    }

    private void DrawPlayMode()
    {
        if (_buildSettings == null || _buildSettingsSo == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("AB Editor PlayMode"));
        SerializedProperty playModeProp = _buildSettingsSo.FindProperty(nameof(FYAssetABSettings.PlayMode));
        if (playModeProp != null)
            card.Add(new PropertyField(playModeProp));

        string note = _buildSettings.PlayMode == EPlayMode.Simulate
            ? "Simulate 未实现，当前与 Runtime 行为一致。"
            : _buildSettings.PlayMode == EPlayMode.Editor
                ? "Editor：Collector 扫描 + AssetDatabase 直读，无需构建 AB。"
                : "Runtime：读 ABManifest 并经 AssetBundle 加载。";
        var noteLabel = new Label(note);
        noteLabel.style.marginTop = 4;
        noteLabel.style.whiteSpace = WhiteSpace.Normal;
        card.Add(noteLabel);
        card.Bind(_buildSettingsSo);
        _root.Add(card);
    }
}

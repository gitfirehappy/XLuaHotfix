using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// FYAsset 全局设置面板。
/// 用于编辑构建、版本与新旧管线的路径配置。
/// </summary>
public class SettingsPanel : IBuildPipelinePanel
{
    private BuildPipelineWindow _window;
    private FYAssetSettings _settings;
    private SerializedObject _so;
    private VisualElement _root;
    private ScrollView _scrollView;

    public string PanelName => "设置";

    public void OnEnable(EditorWindow window)
    {
        _window = window as BuildPipelineWindow;
        LoadSettings();
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
    }

    /// <summary>
    /// 重建设置面板内容，并在 UseABBackend 变化时刷新窗口壳层禁用状态。
    /// </summary>
    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("刷新", () =>
        {
            LoadSettings();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(FYAssetSettings.DEFAULT_ASSET_PATH));
        _root.Add(toolbar);

        if (_settings == null || _so == null)
        {
            DrawNoSettings();
            return;
        }

        _scrollView = new ScrollView();
        _scrollView.style.flexGrow = 1f;
        _scrollView.Bind(_so);

        DrawSection("Project", "ProjectName", "HotfixUrl");
        DrawSection("Backend", "UseABBackend");
        DrawSection("Hotfix", "MaxHotfixSizeBytes", "HotfixMaxRetryCount", "HotfixRetryBaseDelaySeconds");
        DrawSection("Manifest", "ManifestOutputFormat");
        DrawSection("Version", "VersionDataBasePath");
        DrawSection("AA Path", "LuaScriptsIndexPath", "BuildIndexJsonPath");
        DrawSection("New Path", "BuildOutputRoot", "BuildPackagesFolderName", "CollectorDataFolder", "CollectorSettingPath", "PipelineConfigPath", "AAPipelineConfigPath");
        DrawPushTargetsSection();

        SerializedProperty useAb = _so.FindProperty("UseABBackend");
        if (useAb != null)
        {
            _scrollView.TrackPropertyValue(useAb, _ =>
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                _window?.RefreshShell();
            });
        }

        _root.Add(_scrollView);
    }

    /// <summary>
    /// 以 Card 形式绘制一个配置分组。
    /// </summary>
    private void DrawSection(string header, params string[] propertyNames)
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header(header));

        for (int i = 0; i < propertyNames.Length; i++)
        {
            SerializedProperty prop = _so.FindProperty(propertyNames[i]);
            if (prop == null)
                continue;

            card.Add(new PropertyField(prop));
        }

        _scrollView.Add(card);
    }

    /// <summary>
    /// 当 FYAssetSettings 资产不存在时显示创建入口。
    /// </summary>
    private void DrawNoSettings()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 360f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateTitle("未找到 FYAssetSettings"));
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody(FYAssetSettings.DEFAULT_ASSET_PATH));
        panel.Add(new Button(() =>
        {
            _ = FYAssetSettings.Instance;
            LoadSettings();
            Rebuild();
        })
        {
            text = "创建"
        });
    }

    /// <summary>
    /// 从默认路径或 Resources 加载 FYAssetSettings。
    /// </summary>
    private void LoadSettings()
    {
        _settings = AssetDatabase.LoadAssetAtPath<FYAssetSettings>(FYAssetSettings.DEFAULT_ASSET_PATH)
                    ?? Resources.Load<FYAssetSettings>("FYAssetSettings");
        _so = _settings != null ? new SerializedObject(_settings) : null;
    }

    private void DrawPushTargetsSection()
    {
        if (_settings == null)
            return;

        SerializedProperty list = _so.FindProperty("PushTargets");
        if (list == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Push"));
        card.Add(new PropertyField(list, "Targets"));
        _scrollView.Add(card);
    }
}

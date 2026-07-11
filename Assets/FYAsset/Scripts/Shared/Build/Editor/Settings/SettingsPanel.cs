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
    private FYAssetSettings _settings;
    private SerializedObject _so;
    private VisualElement _root;
    private ScrollView _scrollView;

    public string PanelName => "Settings";

    public void OnEnable(EditorWindow window)
    {
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
    /// 重建设置面板内容。
    /// </summary>
    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
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

        DrawSection(_so, "Project", "ProjectName", "UseABBackend");
        DrawSection(_so, "Build", "BuildOutputRoot", "BuildPackagesFolderName", "VersionDataBasePath", "BuildIndexJsonPath");

        SerializedProperty useAb = _so.FindProperty("UseABBackend");
        if (useAb != null)
        {
            _scrollView.TrackPropertyValue(useAb, _ =>
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            });
        }

        _root.Add(_scrollView);
    }

    /// <summary>
    /// 以 Card 形式绘制一个配置分组。
    /// </summary>
    private void DrawSection(SerializedObject serializedObject, string header, params string[] propertyNames)
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header(header));

        for (int i = 0; i < propertyNames.Length; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(propertyNames[i]);
            if (prop == null)
                continue;

            card.Add(new PropertyField(prop));
        }

        card.Bind(serializedObject);
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
            text = "Create"
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

}

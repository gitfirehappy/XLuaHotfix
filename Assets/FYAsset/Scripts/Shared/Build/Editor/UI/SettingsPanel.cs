using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// FYAsset 全局设置面板：项目名、构建输出、Standalone 与 VersionRecord 路径。
/// 后端专属字段由 AA/AB Config 面板编辑。
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

        DrawSection(_so, "Project", "ProjectName");
        DrawSection(_so, "Build", "BuildOutputRoot", "BuildPackagesFolderName", "StandaloneBuild", "VersionRecordPath", "BuildIndexJsonPath");

        DrawPackageModeSection();



        _root.Add(_scrollView);
    }

    /// <summary>
    /// 离线包 / 在线热更快切（与 AB Editor PlayMode 正交）。
    /// </summary>
    private void DrawPackageModeSection()
    {
        if (_settings == null) return;

        bool isStandalone = _settings.StandaloneBuild;
        string modeLabel = isStandalone ? "● Standalone (离线)" : "● Online (热更)";

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Package Mode"));
        var modeLabelEl = new Label(modeLabel);
        modeLabelEl.style.marginBottom = 4;
        modeLabelEl.style.unityFontStyleAndWeight = FontStyle.Bold;
        card.Add(modeLabelEl);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginTop = 4;

        var btnStandalone = new Button(() =>
        {
            _settings.StandaloneBuild = true;
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            EditorApplication.isPlaying = true;
        })
        {
            text = "▶ Run as Standalone"
        };
        btnStandalone.style.flexGrow = 1;
        btnStandalone.style.marginRight = 4;
        btnStandalone.SetEnabled(!isStandalone);

        var btnOnline = new Button(() =>
        {
            _settings.StandaloneBuild = false;
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            EditorApplication.isPlaying = true;
        })
        {
            text = "▶ Run Online"
        };
        btnOnline.style.flexGrow = 1;
        btnOnline.SetEnabled(isStandalone);

        row.Add(btnStandalone);
        row.Add(btnOnline);
        card.Add(row);
        _scrollView.Add(card);
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

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// FYAsset 全局设置面板。
/// 用于编辑构建、版本与AA，AB管线的路径配置。
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
        DrawSection(_so, "Build", "BuildOutputRoot", "BuildPackagesFolderName", "StandaloneBuild", "VersionRecordPath", "BuildIndexJsonPath");

        DrawAbEditorPlayModeSection();
        DrawPackageModeSection();

        SerializedProperty useAb = _so.FindProperty("UseABBackend");
        if (useAb != null)
        {
            _scrollView.TrackPropertyValue(useAb, _ =>
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                Rebuild();
            });
        }

        _root.Add(_scrollView);
    }

    /// <summary>
    /// AB Editor 加载路径：Editor / Simulate(未实现) / Runtime。
    /// </summary>
    private void DrawAbEditorPlayModeSection()
    {
        if (_settings == null || _so == null) return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("AB Editor PlayMode"));

        if (!_settings.UseABBackend)
        {
            card.Add(new Label("仅在 UseABBackend=true 时生效。当前走 Addressables。"));
            _scrollView.Add(card);
            return;
        }

        SerializedProperty playModeProp = _so.FindProperty("PlayMode");
        if (playModeProp != null)
            card.Add(new PropertyField(playModeProp));

        string note = _settings.PlayMode == EPlayMode.Simulate
            ? "Simulate 本期未实现，运行时按 Runtime 处理。"
            : _settings.PlayMode == EPlayMode.Editor
                ? "Editor：Collector 扫描 + AssetDatabase 直读，无需打 AB。"
                : "Runtime：读 ABManifest + AssetBundle。";
        var noteLabel = new Label(note);
        noteLabel.style.marginTop = 4;
        noteLabel.style.whiteSpace = WhiteSpace.Normal;
        card.Add(noteLabel);
        card.Bind(_so);
        _scrollView.Add(card);
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

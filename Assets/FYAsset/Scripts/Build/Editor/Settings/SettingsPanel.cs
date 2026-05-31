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
    private SharedBuildSettings _sharedSettings;
    private SerializedObject _so;
    private SerializedObject _sharedSo;
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

        if (_settings == null || _so == null || _sharedSettings == null || _sharedSo == null)
        {
            DrawNoSettings();
            return;
        }

        _scrollView = new ScrollView();
        _scrollView.style.flexGrow = 1f;

        DrawSection(_so, "Global", "ProjectName", "HotfixUrl", "UseABBackend", "BuildPackagesFolderName", "HotfixMaxRetryCount", "HotfixRetryBaseDelaySeconds");
        DrawSharedBuildSection();
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

    private void DrawSharedBuildSection()
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Shared Build"));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.BuildOutputRoot)), "Build Output Root", BuildPipelineUI.PathPickerMode.ProjectFolder));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.VersionDataBasePath)), "VersionDataBase Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.BuildIndexJsonPath)), "BuildIndex Json Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.AssetCollectionDataFolder)), "Asset Collection Data Folder", BuildPipelineUI.PathPickerMode.AssetFolder));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.AssetCollectionSettingPath)), "AssetCollectionSetting Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_sharedSo.FindProperty(nameof(SharedBuildSettings.DependencyFilterExtensions)), "Dependency Filter Extensions"));
        card.Add(BuildPipelineUI.PathField(_sharedSo.FindProperty(nameof(SharedBuildSettings.LuaScriptsIndexPath)), "LuaScriptsIndex Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Bind(_sharedSo);
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
        _sharedSettings = FYAssetBuildSettingsProvider.Shared;
        _so = _settings != null ? new SerializedObject(_settings) : null;
        _sharedSo = _sharedSettings != null ? new SerializedObject(_sharedSettings) : null;
    }

    private void DrawPushTargetsSection()
    {
        if (_sharedSettings == null || _sharedSo == null)
            return;

        SerializedProperty list = _sharedSo.FindProperty("PushTargets");
        if (list == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Push"));
        card.Add(BuildPipelineUI.SmallText("Targets"));

        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            card.Add(DrawPushTargetItem(item, i));
        }

        card.Add(new Button(() =>
        {
            AddPushTarget(list);
            _sharedSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(_sharedSettings);
            AssetDatabase.SaveAssets();
            Rebuild();
        })
        {
            text = "+ Target"
        });
        card.Bind(_sharedSo);
        _scrollView.Add(card);
    }

    private VisualElement DrawPushTargetItem(SerializedProperty item, int index)
    {
        var row = new VisualElement();
        row.style.marginBottom = 4f;
        row.style.paddingLeft = 4f;
        row.style.paddingRight = 4f;
        row.style.paddingTop = 4f;
        row.style.paddingBottom = 4f;
        row.style.borderTopWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderBottomWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderTopColor = BuildPipelineUI.BorderColor;
        row.style.borderRightColor = BuildPipelineUI.BorderColor;
        row.style.borderBottomColor = BuildPipelineUI.BorderColor;
        row.style.borderLeftColor = BuildPipelineUI.BorderColor;

        row.Add(BuildPipelineUI.PathField(item.FindPropertyRelative("Path"), $"Target {index + 1} Path", BuildPipelineUI.PathPickerMode.ProjectFolder));
        return row;
    }

    private static void AddPushTarget(SerializedProperty list)
    {
        int index = list.arraySize;
        list.arraySize++;
        SerializedProperty item = list.GetArrayElementAtIndex(index);
        item.FindPropertyRelative("Id").stringValue = "target" + (index + 1);
        item.FindPropertyRelative("Type").enumValueIndex = (int)PushTargetType.LocalDirectory;
        item.FindPropertyRelative("Path").stringValue = string.Empty;
    }
}

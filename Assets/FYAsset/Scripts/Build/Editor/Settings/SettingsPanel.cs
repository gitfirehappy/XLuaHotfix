using UnityEditor;
using UnityEngine;

/// <summary>
/// FYAsset 全局设置面板 —— 编辑 FYAssetSettings SO，UseABBackend 切换时刷新窗口。
/// </summary>
public class SettingsPanel : IBuildPipelinePanel
{
    private FYAssetSettings _settings;
    private SerializedObject _so;
    private EditorWindow _window;
    private Vector2 _scrollPos;

    public string PanelName => "Settings";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        LoadSettings();
    }

    public void OnDisable()
    {
    }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
            LoadSettings();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(FYAssetSettings.DEFAULT_ASSET_PATH, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        if (_settings == null || _so == null)
        {
            DrawNoSettings();
            GUILayout.EndArea();
            return;
        }

        _scrollPos = GUILayout.BeginScrollView(_scrollPos);
        _so.Update();

        bool prevUseAB = _settings.UseABBackend;

        DrawSection("Project", new[] { "ProjectName", "HotfixUrl" });
        DrawSection("Backend", new[] { "UseABBackend" });
        DrawSection("Version", new[] { "VersionDataBasePath" });
        DrawSection("Legacy Pipeline Paths", new[]
        {
            "LuaScriptsIndexPath",
            "SnapshotAssetPath",
            "BuildIndexJsonPath"
        });
        DrawSection("New Pipeline Paths", new[]
        {
            "CollectorDataFolder",
            "CollectorSettingPath",
            "PipelineConfigPath"
        });

        if (_so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();

            if (_settings.UseABBackend != prevUseAB)
                _window?.Repaint();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSection(string header, string[] propertyNames)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        for (int i = 0; i < propertyNames.Length; i++)
        {
            SerializedProperty prop = _so.FindProperty(propertyNames[i]);
            if (prop != null)
                EditorGUILayout.PropertyField(prop);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawNoSettings()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical("box", GUILayout.Width(360f));
        GUILayout.Space(10f);
        GUILayout.Label("FYAssetSettings not found", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label(FYAssetSettings.DEFAULT_ASSET_PATH, EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(10f);
        if (GUILayout.Button("Create FYAssetSettings", GUILayout.Height(36f)))
        {
            _ = FYAssetSettings.Instance;
            LoadSettings();
        }

        GUILayout.Space(10f);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void LoadSettings()
    {
        _settings = AssetDatabase.LoadAssetAtPath<FYAssetSettings>(FYAssetSettings.DEFAULT_ASSET_PATH)
                    ?? Resources.Load<FYAssetSettings>("FYAssetSettings");
        _so = _settings != null ? new SerializedObject(_settings) : null;
    }
}
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pipeline 配置面板 —— 编辑 BuildPipelineConfig SO，查看/修改管线 Task 编排。
/// </summary>
public class PipelinePanel : IBuildPipelinePanel
{
    private BuildPipelineConfig _config;
    private Editor _configEditor;
    private Vector2 _scrollPos;

    public string PanelName => "Pipeline";

    public void OnEnable(EditorWindow window)
    {
        LoadConfig();
    }

    public void OnDisable()
    {
        if (_configEditor != null)
        {
            Object.DestroyImmediate(_configEditor);
            _configEditor = null;
        }
    }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);
        
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            LoadConfig();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (_config == null)
        {
            DrawNoConfig();
        }
        else
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            if (_configEditor != null)
            {
                _configEditor.OnInspectorGUI();
            }
            GUILayout.EndScrollView();
        }
        
        GUILayout.EndArea();
    }

    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH);
        if (_config != null)
        {
            if (_configEditor != null) Object.DestroyImmediate(_configEditor);
            _configEditor = Editor.CreateEditor(_config);
        }
    }

    private void DrawNoConfig()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("No BuildPipelineConfig found at " + FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH, EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create BuildPipelineConfig", GUILayout.Width(200), GUILayout.Height(36)))
        {
            CreateConfig();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void CreateConfig()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Build"))
        {
            AssetDatabase.CreateFolder("Assets", "Build");
        }
        var config = ScriptableObject.CreateInstance<BuildPipelineConfig>();
        AssetDatabase.CreateAsset(config, FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadConfig();
    }
}

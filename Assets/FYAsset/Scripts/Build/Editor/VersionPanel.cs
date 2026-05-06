using UnityEditor;
using UnityEngine;

public class VersionPanel : IBuildPipelinePanel
{
    private const string VersionAssetPath = "Assets/Build/VersionDataBase.asset";

    private VersionDataBase _versionDB;
    private Editor _versionEditor;
    private Vector2 _scrollPos;

    public string PanelName => "Version";

    public void OnEnable(EditorWindow window)
    {
        LoadVersionDB();
    }

    public void OnDisable()
    {
        if (_versionEditor != null)
        {
            Object.DestroyImmediate(_versionEditor);
            _versionEditor = null;
        }
    }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
            LoadVersionDB();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (_versionDB == null)
        {
            DrawNoVersionDB();
        }
        else
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            if (_versionEditor != null)
                _versionEditor.OnInspectorGUI();
            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();
    }

    private void LoadVersionDB()
    {
        _versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(VersionAssetPath);
        if (_versionDB != null)
        {
            if (_versionEditor != null) Object.DestroyImmediate(_versionEditor);
            _versionEditor = Editor.CreateEditor(_versionDB);
        }
    }

    private void DrawNoVersionDB()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("No VersionDataBase found at " + VersionAssetPath,
            EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create VersionDataBase", GUILayout.Width(200), GUILayout.Height(36)))
            CreateVersionDB();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void CreateVersionDB()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Build"))
            AssetDatabase.CreateFolder("Assets", "Build");
        var asset = ScriptableObject.CreateInstance<VersionDataBase>();
        AssetDatabase.CreateAsset(asset, VersionAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadVersionDB();
    }
}

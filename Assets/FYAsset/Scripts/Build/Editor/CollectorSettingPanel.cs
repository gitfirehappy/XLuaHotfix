using UnityEditor;
using UnityEngine;

public class CollectorSettingPanel : IBuildPipelinePanel
{
    private CollectorSetting _setting;
    private SerializedObject _so;
    private Vector2 _scrollPos;

    public string PanelName => "Collect Config";

    public void OnEnable(EditorWindow window)
    {
        LoadSetting();
    }

    public void OnDisable() { }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
            LoadSetting();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (_setting == null)
        {
            DrawNoSetting();
        }
        else
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            DrawSettingFields();
            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();
    }

    private void LoadSetting()
    {
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        _so = _setting != null ? new SerializedObject(_setting) : null;
    }

    private void DrawSettingFields()
    {
        if (_so == null) return;
        _so.Update();
        SerializedProperty prop = _so.GetIterator();
        prop.NextVisible(true);
        while (prop.NextVisible(false))
            EditorGUILayout.PropertyField(prop, true);
        _so.ApplyModifiedProperties();
    }

    private void DrawNoSetting()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("No CollectorSetting found at " + FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH,
            EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create CollectorSetting", GUILayout.Width(200), GUILayout.Height(36)))
            CreateSetting();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void CreateSetting()
    {
        string dir = System.IO.Path.GetDirectoryName(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            string parent = System.IO.Path.GetDirectoryName(dir);
            string folderName = System.IO.Path.GetFileName(dir);
            AssetDatabase.CreateFolder(parent, folderName);
        }
        var asset = ScriptableObject.CreateInstance<CollectorSetting>();
        AssetDatabase.CreateAsset(asset, FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadSetting();
    }
}

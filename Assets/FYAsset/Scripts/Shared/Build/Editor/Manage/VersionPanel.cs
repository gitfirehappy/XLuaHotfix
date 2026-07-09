using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// VersionDataBase 编辑面板。
/// 当前以通用 SerializedObject 方式暴露全部可见字段。
/// </summary>
public class VersionPanel : IBuildPipelinePanel
{
    private static string VersionAssetPath => FYAssetSettings.Instance.VersionDataBasePath;

    private VersionDataBase _versionDB;
    private SerializedObject _so;
    private VisualElement _root;

    public string PanelName => "Version";

    public void OnEnable(EditorWindow window)
    {
        LoadVersionDB();
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
    /// 重建 VersionDataBase 面板内容。
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
            LoadVersionDB();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Reset to 1.0.0 (Test)", ResetVersionToTest, 150f));
        toolbar.Add(BuildPipelineUI.Spacer());
        _root.Add(toolbar);

        if (_versionDB == null || _so == null)
        {
            DrawNoVersionDB();
            return;
        }

        var scrollView = new ScrollView();
        scrollView.style.flexGrow = 1f;

        SerializedProperty iterator = _so.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script")
                continue;

            PropertyField field = new PropertyField(iterator.Copy());
            if (IsBuildMetadataField(iterator.propertyPath))
                field.SetEnabled(false);
            scrollView.Add(field);
        }

        scrollView.Bind(_so);
        _root.Add(scrollView);
    }

    private void ResetVersionToTest()
    {
        if (_versionDB == null)
            return;

        if (!EditorUtility.DisplayDialog(
                "Reset Version",
                "Reset VersionDataBase to 1.0.0 for testing?\n\nThis clears Channel, LastBuildTime, and DailyBuildCount.",
                "Reset",
                "Cancel"))
            return;

        Undo.RecordObject(_versionDB, "Reset VersionDataBase");
        _versionDB.CurrentVersion = new VersionNumber { Major = 1, Minor = 0, Patch = 0, Build = 0, Channel = string.Empty };
        _versionDB.LastBuildTime = string.Empty;
        _versionDB.DailyBuildCount = 0;
        EditorUtility.SetDirty(_versionDB);
        AssetDatabase.SaveAssets();
        LoadVersionDB();
        Rebuild();
    }

    private static bool IsBuildMetadataField(string propertyPath)
    {
        return propertyPath == nameof(VersionDataBase.LastBuildTime)
            || propertyPath == nameof(VersionDataBase.DailyBuildCount);
    }

    /// <summary>
    /// 当 VersionDataBase 缺失时显示创建入口。
    /// </summary>
    private void DrawNoVersionDB()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 420f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody("未找到 VersionDataBase: " + VersionAssetPath));
        panel.Add(new Button(CreateVersionDB)
        {
            text = "Create"
        });
    }

    /// <summary>
    /// 按当前设置中的路径加载 VersionDataBase。
    /// </summary>
    private void LoadVersionDB()
    {
        _versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(VersionAssetPath);
        _so = _versionDB != null ? new SerializedObject(_versionDB) : null;
    }

    /// <summary>
    /// 创建新的 VersionDataBase 资产。
    /// </summary>
    private void CreateVersionDB()
    {
        BuildPipelineUI.EnsureAssetParentFolder(VersionAssetPath);

        var asset = ScriptableObject.CreateInstance<VersionDataBase>();
        AssetDatabase.CreateAsset(asset, VersionAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadVersionDB();
        Rebuild();
    }
}

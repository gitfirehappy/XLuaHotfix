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
    private static string VersionAssetPath => FYAssetBuildSettingsProvider.Shared.VersionDataBasePath;

    private VersionDataBase _versionDB;
    private SerializedObject _so;
    private VisualElement _root;

    public string PanelName => "版本";

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
        toolbar.Add(BuildPipelineUI.ToolbarButton("刷新", () =>
        {
            LoadVersionDB();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.Spacer());
        _root.Add(toolbar);

        if (_versionDB == null || _so == null)
        {
            DrawNoVersionDB();
            return;
        }

        var scrollView = new ScrollView();
        scrollView.style.flexGrow = 1f;
        scrollView.Bind(_so);

        SerializedProperty iterator = _so.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script")
                continue;

            scrollView.Add(new PropertyField(iterator.Copy()));
        }

        _root.Add(scrollView);
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
            text = "创建"
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

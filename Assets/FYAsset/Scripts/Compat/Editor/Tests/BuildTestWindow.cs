#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Compat 层的构建测试矩阵宿主窗口：test 面板属于双后端胶水，
/// 不嵌入后端构建窗口（保证 {AA∪Shared}/{AB∪Shared} 导出独立性）。
/// 菜单：FYAsset/Build/Test Matrix。
/// </summary>
public sealed class BuildTestWindow : EditorWindow
{
    private readonly List<IBuildPipelinePanel> _panels = new();

    [MenuItem("FYAsset/Build/Test Matrix")]
    public static void Open()
    {
        var window = GetWindow<BuildTestWindow>();
        window.titleContent = new GUIContent("Build Test Matrix");
        window.minSize = new Vector2(420f, 320f);
        window.Show();
    }

    private void CreateGUI()
    {
        rootVisualElement.Clear();
        foreach (IBuildPipelinePanel panel in _panels)
            panel.OnDisable();
        _panels.Clear();

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1f;
        foreach (BuildTestBackend backend in System.Enum.GetValues(typeof(BuildTestBackend)))
        {
            var panel = new BuildTestPanel(backend);
            _panels.Add(panel);
            panel.OnEnable(this);

            var header = new Label(panel.PanelName);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 8f;
            header.style.marginLeft = 6f;
            scroll.Add(header);
            scroll.Add(panel.CreateContent());
        }
        rootVisualElement.Add(scroll);
    }

    private void OnDisable()
    {
        foreach (IBuildPipelinePanel panel in _panels)
            panel.OnDisable();
    }
}
#endif

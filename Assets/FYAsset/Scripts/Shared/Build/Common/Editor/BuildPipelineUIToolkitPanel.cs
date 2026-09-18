using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 面板根节点与空状态控件的共用基类。
/// </summary>
public abstract class BuildPipelineUIToolkitPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private VisualElement _root;

    public abstract string PanelName { get; }

    public virtual void OnEnable(EditorWindow window)
    {
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement
        {
            name = GetType().Name
        };
        _root.style.flexGrow = 1f;
        _root.style.minHeight = 0f;
        _root.style.minWidth = 0f;
        _root.style.flexDirection = FlexDirection.Column;
        BuildContent(_root);
        return _root;
    }

    public virtual void OnDisable()
    {
        _root = null;
    }

    public virtual void SetVisible(bool visible)
    {
    }

    /// <summary>
    /// 由派生类填充面板主体内容。
    /// </summary>
    protected abstract void BuildContent(VisualElement root);

    /// <summary>
    /// 顶对齐的空状态容器。方法名保留给已有调用点。
    /// </summary>
    public static VisualElement CreateCenteredPanel(VisualElement root, float maxWidth = 420f)
    {
        var outer = new VisualElement();
        outer.style.flexGrow = 1f;
        outer.style.justifyContent = Justify.FlexStart;
        outer.style.alignItems = Align.FlexStart;
        outer.style.paddingLeft = 12f;
        outer.style.paddingRight = 12f;

        var panel = new VisualElement();
        panel.style.width = Length.Percent(100f);
        panel.style.maxWidth = maxWidth;
        panel.style.paddingLeft = 8f;
        panel.style.paddingRight = 8f;
        panel.style.paddingTop = 8f;
        panel.style.paddingBottom = 8f;

        outer.Add(panel);
        root.Add(outer);
        return panel;
    }

    /// <summary>
    /// 创建加粗标题文本。
    /// </summary>
    public static Label CreateTitle(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 6f;
        return label;
    }

    /// <summary>
    /// 创建正文提示文本。
    /// </summary>
    public static Label CreateBody(string text)
    {
        var label = new Label(text);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 11f;
        label.style.color = BuildPipelineUI.SecondaryTextColor;
        label.style.marginTop = 4f;
        return label;
    }
}

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 简单 BuildPipeline 面板的 UI Toolkit 基类。
/// 适用于只需要静态布局或轻量交互的子面板。
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
    /// 创建居中的 Card 容器，用于空状态或占位提示。
    /// </summary>
    public static VisualElement CreateCenteredPanel(VisualElement root, float maxWidth = 420f)
    {
        var outer = new VisualElement();
        outer.style.flexGrow = 1f;
        outer.style.justifyContent = Justify.Center;
        outer.style.alignItems = Align.Center;
        outer.style.paddingLeft = 12f;
        outer.style.paddingRight = 12f;

        var panel = new VisualElement();
        panel.style.width = Length.Percent(100f);
        panel.style.maxWidth = maxWidth;
        panel.style.paddingLeft = 14f;
        panel.style.paddingRight = 14f;
        panel.style.paddingTop = 12f;
        panel.style.paddingBottom = 12f;
        panel.style.borderTopWidth = 1f;
        panel.style.borderRightWidth = 1f;
        panel.style.borderBottomWidth = 1f;
        panel.style.borderLeftWidth = 1f;
        panel.style.borderTopLeftRadius = 4f;
        panel.style.borderTopRightRadius = 4f;
        panel.style.borderBottomLeftRadius = 4f;
        panel.style.borderBottomRightRadius = 4f;
        panel.style.borderTopColor = BuildPipelineUI.BorderColor;
        panel.style.borderRightColor = BuildPipelineUI.BorderColor;
        panel.style.borderBottomColor = BuildPipelineUI.BorderColor;
        panel.style.borderLeftColor = BuildPipelineUI.BorderColor;
        panel.style.backgroundColor = BuildPipelineUI.CardBackgroundColor;

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
        label.style.marginTop = 2f;
        return label;
    }
}

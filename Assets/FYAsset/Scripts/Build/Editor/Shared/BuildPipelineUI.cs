using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// FYAsset 构建编辑器面板共用的 UI Toolkit 样式与小型元素工厂。
/// </summary>
public static class BuildPipelineUI
{
    public static readonly Color WindowBackgroundColor = EditorGUIUtility.isProSkin
        ? new Color(0.235f, 0.235f, 0.235f)
        : new Color(0.78f, 0.78f, 0.78f);

    public static readonly Color SidebarBackgroundColor = EditorGUIUtility.isProSkin
        ? new Color(0.18f, 0.18f, 0.18f)
        : new Color(0.76f, 0.76f, 0.76f);

    public static readonly Color CardBackgroundColor = EditorGUIUtility.isProSkin
        ? new Color(0.19f, 0.19f, 0.19f)
        : new Color(0.86f, 0.86f, 0.86f);

    public static readonly Color BorderColor = EditorGUIUtility.isProSkin
        ? new Color(0.35f, 0.35f, 0.35f)
        : new Color(0.60f, 0.60f, 0.60f);

    public static readonly Color ActiveColor = new Color(0.17f, 0.36f, 0.53f, 1f);
    public static readonly Color HoverColor = new Color(0.30f, 0.30f, 0.30f, 0.50f);
    public static readonly Color SecondaryTextColor = EditorGUIUtility.isProSkin
        ? new Color(0.72f, 0.72f, 0.72f)
        : new Color(0.24f, 0.24f, 0.24f);

    /// <summary>
    /// 创建统一高度的工具栏容器。
    /// </summary>
    public static VisualElement Toolbar()
    {
        var toolbar = new Toolbar();
        toolbar.style.minHeight = 22f;
        toolbar.style.flexShrink = 0f;
        return toolbar;
    }

    /// <summary>
    /// 创建工具栏按钮；可选固定宽度。
    /// </summary>
    public static Button ToolbarButton(string text, System.Action clicked, float width = 0f)
    {
        var button = new ToolbarButton(clicked)
        {
            text = text
        };
        if (width > 0f)
            button.style.width = width;
        return button;
    }

    /// <summary>
    /// 创建工具栏弱提示文本。
    /// </summary>
    public static Label ToolbarLabel(string text)
    {
        var label = new Label(text);
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.fontSize = 10f;
        label.style.color = SecondaryTextColor;
        return label;
    }

    /// <summary>
    /// 创建可伸缩空白，占据工具栏剩余空间。
    /// </summary>
    public static VisualElement Spacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        return spacer;
    }

    /// <summary>
    /// 创建统一 Card 容器。
    /// </summary>
    public static VisualElement Card()
    {
        var card = new VisualElement();
        card.style.paddingLeft = 8f;
        card.style.paddingRight = 8f;
        card.style.paddingTop = 8f;
        card.style.paddingBottom = 8f;
        card.style.marginBottom = 8f;
        card.style.borderTopWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderLeftWidth = 1f;
        card.style.borderTopColor = BorderColor;
        card.style.borderRightColor = BorderColor;
        card.style.borderBottomColor = BorderColor;
        card.style.borderLeftColor = BorderColor;
        card.style.backgroundColor = CardBackgroundColor;
        return card;
    }

    /// <summary>
    /// 创建区块标题文本。
    /// </summary>
    public static Label Header(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 6f;
        return label;
    }

    /// <summary>
    /// 创建用于说明和弱提示的小字号文本。
    /// </summary>
    public static Label SmallText(string text)
    {
        var label = new Label(text);
        label.style.fontSize = 11f;
        label.style.color = SecondaryTextColor;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }

    /// <summary>
    /// 按属性名创建 PropertyField，便于面板快速装配 SerializedObject 字段。
    /// </summary>
    public static PropertyField Property(SerializedObject so, string propertyName, string label = null)
    {
        SerializedProperty property = so?.FindProperty(propertyName);
        var field = label == null ? new PropertyField(property) : new PropertyField(property, label);
        field.style.marginBottom = 2f;
        return field;
    }
}

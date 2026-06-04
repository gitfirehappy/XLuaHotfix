using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;

/// <summary>
/// FYAsset 构建编辑器面板共用的 UI Toolkit 样式与小型元素工厂。
/// </summary>
public static class BuildPipelineUI
{
    public enum PathPickerMode
    {
        AssetFile,
        AssetFolder,
        ProjectFile,
        ProjectFolder
    }

    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

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
    /// 创建 UI Toolkit 拖拽分隔条；不设置 cursor，避免 Unity 2022.3 API 差异。
    /// </summary>
    public static VisualElement Splitter(bool vertical)
    {
        var splitter = new VisualElement();
        splitter.style.flexShrink = 0f;
        splitter.style.backgroundColor = BorderColor;
        if (vertical)
        {
            splitter.style.width = 6f;
            splitter.style.minWidth = 6f;
            splitter.style.borderLeftWidth = 1f;
            splitter.style.borderRightWidth = 1f;
            splitter.style.borderLeftColor = new Color(0f, 0f, 0f, 0.18f);
            splitter.style.borderRightColor = new Color(1f, 1f, 1f, 0.08f);
        }
        else
        {
            splitter.style.height = 6f;
            splitter.style.minHeight = 6f;
            splitter.style.borderTopWidth = 1f;
            splitter.style.borderBottomWidth = 1f;
            splitter.style.borderTopColor = new Color(0f, 0f, 0f, 0.18f);
            splitter.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
        }

        splitter.RegisterCallback<PointerEnterEvent>(_ => splitter.style.backgroundColor = HoverColor);
        splitter.RegisterCallback<PointerLeaveEvent>(_ => splitter.style.backgroundColor = BorderColor);
        return splitter;
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

    /// <summary>
    /// 创建带路径选择按钮的字符串编辑行。
    /// </summary>
    public static VisualElement PathField(SerializedProperty property, string label, PathPickerMode mode, float labelWidth = 140f)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.width = Length.Percent(100f);
        row.style.minWidth = 0f;
        row.style.marginBottom = 2f;

        if (!string.IsNullOrEmpty(label))
        {
            Label title = SmallText(label);
            title.style.width = labelWidth;
            title.style.flexShrink = 0f;
            row.Add(title);
        }

        var field = new TextField
        {
            value = property?.stringValue ?? string.Empty,
            isDelayed = true
        };
        field.style.width = 0f;
        field.style.flexGrow = 1f;
        field.style.flexShrink = 1f;
        field.style.flexBasis = 0f;
        field.style.minWidth = 0f;
        field.style.maxWidth = Length.Percent(100f);
        field.style.marginRight = 4f;
        field.RegisterValueChangedCallback(evt => SetStringProperty(property, evt.newValue ?? string.Empty));
        row.Add(field);

        var button = new Button(() =>
        {
            string picked = BrowsePath(property, mode, label);
            if (picked == null)
                return;

            SetStringProperty(property, picked);
            field.SetValueWithoutNotify(picked);
        })
        {
            text = "..."
        };
        button.style.width = 34f;
        button.style.minWidth = 34f;
        button.style.flexShrink = 0f;
        row.Add(button);

        return row;
    }

    /// <summary>
    /// 创建带单位切换的字节大小编辑行。
    /// </summary>
    public static VisualElement ByteSizeField(SerializedProperty property, string label, float labelWidth = 140f)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.width = Length.Percent(100f);
        row.style.minWidth = 0f;
        row.style.marginBottom = 2f;

        if (!string.IsNullOrEmpty(label))
        {
            Label title = SmallText(label);
            title.style.width = labelWidth;
            title.style.flexShrink = 0f;
            row.Add(title);
        }

        long bytes = property != null ? property.longValue : 0L;
        int unitIndex = GetBestByteUnitIndex(bytes);

        var valueField = new FloatField
        {
            value = ConvertBytesToUnitValue(bytes, unitIndex),
            isDelayed = true
        };
        valueField.style.flexShrink = 0f;
        valueField.style.width = 96f;
        valueField.style.marginRight = 4f;
        row.Add(valueField);

        var unitField = new PopupField<string>(new System.Collections.Generic.List<string>(ByteUnits), unitIndex);
        unitField.style.flexShrink = 0f;
        unitField.style.width = 72f;
        unitField.style.marginRight = 6f;
        row.Add(unitField);

        Label exactLabel = SmallText(FormatBytes(bytes));
        exactLabel.style.flexGrow = 1f;
        row.Add(exactLabel);

        unitField.RegisterValueChangedCallback(evt =>
        {
            int newUnitIndex = Array.IndexOf(ByteUnits, evt.newValue);
            if (newUnitIndex < 0)
                return;

            float displayed = ConvertBytesToUnitValue(property?.longValue ?? 0L, newUnitIndex);
            valueField.SetValueWithoutNotify(displayed);
            exactLabel.text = FormatBytes(property?.longValue ?? 0L);
        });

        valueField.RegisterValueChangedCallback(evt =>
        {
            int newUnitIndex = Array.IndexOf(ByteUnits, unitField.value);
            if (newUnitIndex < 0)
                newUnitIndex = unitIndex;

            long newBytes = ConvertUnitValueToBytes(evt.newValue, newUnitIndex);
            SetLongProperty(property, newBytes);
            exactLabel.text = FormatBytes(newBytes);
        });

        return row;
    }

    /// <summary>
    /// 按 asset 路径确保父目录存在，避免面板创建资产时硬编码目录。
    /// </summary>
    public static void EnsureAssetParentFolder(string assetPath)
    {
        string folderPath = FYAssetPathUtility.NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = FYAssetPathUtility.JoinAssetPath(current, parts[i]);
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void SetStringProperty(SerializedProperty property, string value)
    {
        if (property == null)
            return;

        var so = property.serializedObject;
        Undo.RecordObject(so.targetObject, "Edit Path");
        property.stringValue = value ?? string.Empty;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(so.targetObject);
        AssetDatabase.SaveAssets();
    }

    private static void SetLongProperty(SerializedProperty property, long value)
    {
        if (property == null)
            return;

        var so = property.serializedObject;
        Undo.RecordObject(so.targetObject, "Edit Size");
        property.longValue = Math.Max(0L, value);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(so.targetObject);
        AssetDatabase.SaveAssets();
    }

    private static string BrowsePath(SerializedProperty property, PathPickerMode mode, string label)
    {
        string current = property?.stringValue ?? string.Empty;
        string title = string.IsNullOrEmpty(label) ? "Select Path" : "Select " + label;
        string initial = GetInitialDirectory(current, mode);
        string absolutePath = mode switch
        {
            PathPickerMode.AssetFile => EditorUtility.OpenFilePanel(title, initial, string.Empty),
            PathPickerMode.AssetFolder => EditorUtility.OpenFolderPanel(title, initial, string.Empty),
            PathPickerMode.ProjectFile => EditorUtility.OpenFilePanel(title, initial, string.Empty),
            PathPickerMode.ProjectFolder => EditorUtility.OpenFolderPanel(title, initial, string.Empty),
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(absolutePath))
            return null;

        return mode switch
        {
            PathPickerMode.AssetFile or PathPickerMode.AssetFolder => TryToAssetPath(absolutePath),
            PathPickerMode.ProjectFile or PathPickerMode.ProjectFolder => TryToProjectRelativePath(absolutePath),
            _ => null
        };
    }

    private static string GetInitialDirectory(string currentPath, PathPickerMode mode)
    {
        string root = GetProjectRoot();
        if (string.IsNullOrWhiteSpace(currentPath))
            return mode == PathPickerMode.AssetFile || mode == PathPickerMode.AssetFolder ? Application.dataPath : root;

        string normalized = FYAssetPathUtility.NormalizeAssetPath(currentPath);
        string absolute = mode == PathPickerMode.AssetFile || mode == PathPickerMode.AssetFolder
            ? ToAbsoluteAssetPath(normalized)
            : ToAbsoluteProjectPath(normalized);

        if (string.IsNullOrEmpty(absolute))
            return mode == PathPickerMode.AssetFile || mode == PathPickerMode.AssetFolder ? Application.dataPath : root;

        return Directory.Exists(absolute) ? absolute : Path.GetDirectoryName(absolute) ?? root;
    }

    private static string GetProjectRoot()
    {
        return FYAssetPathUtility.NormalizePath(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath);
    }

    private static string ToAbsoluteAssetPath(string assetPath)
    {
        string normalized = FYAssetPathUtility.NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (Path.IsPathRooted(normalized))
            return FYAssetPathUtility.NormalizePath(normalized);

        if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            return string.Empty;

        return FYAssetPathUtility.ResolveFilePath(GetProjectRoot(), normalized);
    }

    private static string ToAbsoluteProjectPath(string path)
    {
        string normalized = FYAssetPathUtility.NormalizeAssetPath(path);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (Path.IsPathRooted(normalized))
            return FYAssetPathUtility.NormalizePath(normalized);

        return FYAssetPathUtility.ResolveFilePath(GetProjectRoot(), normalized);
    }

    private static string TryToAssetPath(string absolutePath)
    {
        return FYAssetPathUtility.TryMakeAssetPath(absolutePath, Application.dataPath, out string assetPath)
            ? assetPath
            : null;
    }

    private static string TryToProjectRelativePath(string absolutePath)
    {
        return FYAssetPathUtility.TryMakeProjectRelativePath(absolutePath, GetProjectRoot(), out string relativePath)
            ? relativePath
            : null;
    }

    private static int GetBestByteUnitIndex(long bytes)
    {
        if (bytes <= 0)
            return 0;

        int unitIndex = 0;
        double value = bytes;
        while (unitIndex < ByteUnits.Length - 1 && value >= 1024d)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex;
    }

    private static float ConvertBytesToUnitValue(long bytes, int unitIndex)
    {
        return (float)(bytes / Math.Pow(1024d, Math.Max(0, unitIndex)));
    }

    private static long ConvertUnitValueToBytes(float value, int unitIndex)
    {
        double bytes = value * Math.Pow(1024d, Math.Max(0, unitIndex));
        return (long)Math.Round(Math.Max(0d, bytes), MidpointRounding.AwayFromZero);
    }

    private static string FormatBytes(long bytes)
    {
        return bytes.ToString("N0") + " B";
    }
}

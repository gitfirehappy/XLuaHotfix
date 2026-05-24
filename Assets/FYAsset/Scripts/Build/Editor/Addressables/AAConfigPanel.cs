using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline 配置概览面板。
/// 只读展示当前 Addressables 状态，并提供 Groups 窗口跳转入口。
/// </summary>
public sealed class AAConfigPanel : IBuildPipelinePanel
{
    private VisualElement _root;

    public string PanelName => "AA 配置";

    public void OnEnable(EditorWindow window)
    {
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
        _root = null;
    }

    /// <summary>
    /// 重建 AA 配置面板内容。
    /// </summary>
    private void Rebuild()
    {
        _root.Clear();

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarLabel("AA"));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(FYAssetSettings.Instance.VersionDataBasePath));
        _root.Add(toolbar);

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            DrawNoSettings();
            return;
        }

        DrawSummary(settings);
        DrawOpenGroupsButton();
    }

    /// <summary>
    /// Addressables Settings 缺失时显示提示和跳转入口。
    /// </summary>
    private void DrawNoSettings()
    {
        VisualElement card = BuildPipelineUI.Card();
        card.style.marginTop = 24f;
        card.Add(BuildPipelineUI.Header("未找到 Addressables Settings"));
        card.Add(BuildPipelineUI.SmallText("先创建或选择默认 Addressables Settings。"));
        card.Add(new Button(OpenGroupsWindow) { text = "Groups" });
        _root.Add(card);
    }

    /// <summary>
    /// 汇总展示 Addressables 的关键只读信息。
    /// </summary>
    private void DrawSummary(AddressableAssetSettings settings)
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Addressables 概览"));

        string profileName = settings.profileSettings != null
            ? settings.profileSettings.GetProfileName(settings.activeProfileId)
            : "(no profile)";
        string buildPath = EvaluateProfilePath(settings, AddressableAssetSettings.kBuildPath);
        string loadPath = EvaluateProfilePath(settings, AddressableAssetSettings.kLoadPath);

        card.Add(BuildPipelineUI.SmallText("Groups: " + (settings.groups != null ? settings.groups.Count.ToString() : "0")));
        card.Add(BuildPipelineUI.SmallText("Active Profile: " + (profileName ?? "(none)")));
        card.Add(BuildPipelineUI.SmallText("Build Path: " + buildPath));
        card.Add(BuildPipelineUI.SmallText("Load Path: " + loadPath));
        _root.Add(card);
    }

    /// <summary>
    /// 绘制打开 Addressables Groups 窗口的入口。
    /// </summary>
    private void DrawOpenGroupsButton()
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Groups"));
        card.Add(new Button(OpenGroupsWindow) { text = "Groups" });
        _root.Add(card);
    }

    /// <summary>
    /// 解析当前 Profile 下的 Addressables 路径变量。
    /// </summary>
    private static string EvaluateProfilePath(AddressableAssetSettings settings, string variableName)
    {
        if (settings == null || settings.profileSettings == null)
            return "(unavailable)";

        string value = settings.profileSettings.GetValueById(settings.activeProfileId, variableName);
        if (string.IsNullOrEmpty(value))
            return "(empty)";

        return settings.profileSettings.EvaluateString(settings.activeProfileId, value);
    }

    /// <summary>
    /// 通过反射打开 Unity Addressables Groups 窗口。
    /// </summary>
    private static void OpenGroupsWindow()
    {
        Type windowType = typeof(AddressableAssetSettings).Assembly.GetType("UnityEditor.AddressableAssets.GUI.AddressableAssetsWindow");
        if (windowType == null)
        {
            Debug.LogWarning("[AAConfigPanel] 未找到 Addressables Groups 窗口类型。");
            return;
        }

        MethodInfo initMethod = windowType.GetMethod("Init", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (initMethod == null)
        {
            Debug.LogWarning("[AAConfigPanel] 未找到 Addressables Groups 窗口 Init 方法。");
            return;
        }

        initMethod.Invoke(null, null);
    }
}

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AA Pipeline 配置概览面板。
/// 只读展示当前 Addressables 状态，并提供 Groups 窗口跳转入口。
/// </summary>
public sealed class AAConfigPanel : IBuildPipelinePanel
{
    private VisualElement _root;
    private FYAssetAASettings _buildSettings;
    private SerializedObject _buildSettingsSo;

    public string PanelName => "AA Config";

    public void OnEnable(EditorWindow window)
    {
        LoadBuildSettings();
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
        _buildSettings = null;
        _buildSettingsSo = null;
    }

    private void Rebuild()
    {
        _root.Clear();
        _root.Unbind();

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarLabel("AA"));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
        {
            LoadBuildSettings();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(FYAssetAASettings.DEFAULT_ASSET_PATH));
        _root.Add(toolbar);

        DrawBuildSettings();

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            DrawNoSettings();
            return;
        }

        DrawSummary(settings);
        DrawOpenGroupsButton();
    }

    private void LoadBuildSettings()
    {
        _buildSettings = FYAssetAASettings.Instance;
        _buildSettingsSo = _buildSettings != null ? new SerializedObject(_buildSettings) : null;
    }

    private void DrawBuildSettings()
    {
        if (_buildSettingsSo == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("AA Settings"));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.HotfixUrl))));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.HotfixMaxRetryCount))));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.HotfixRetryBaseDelaySeconds))));
        card.Add(BuildPipelineUI.PathField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.BuildPipelineConfigPath)), "Pipeline Config Path", BuildPipelineUI.PathPickerMode.AssetFile));
        card.Add(new PropertyField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.ManifestOutputFormat))));
        card.Add(BuildPipelineUI.ByteSizeField(_buildSettingsSo.FindProperty(nameof(FYAssetAASettings.MaxHotfixSizeBytes)), "Max Hotfix Size"));
        card.Bind(_buildSettingsSo);
        _root.Add(card);
    }

    private void DrawNoSettings()
    {
        VisualElement card = BuildPipelineUI.Card();
        card.style.marginTop = 24f;
        card.Add(BuildPipelineUI.Header("未找到 Addressables Settings"));
        card.Add(BuildPipelineUI.SmallText("先创建或选择默认 Addressables Settings。"));
        card.Add(new Button(OpenGroupsWindow) { text = "Groups" });
        _root.Add(card);
    }

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

    private void DrawOpenGroupsButton()
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Groups"));
        card.Add(new Button(OpenGroupsWindow) { text = "Groups" });
        _root.Add(card);
    }

    private static string EvaluateProfilePath(AddressableAssetSettings settings, string variableName)
    {
        if (settings == null || settings.profileSettings == null)
            return "(unavailable)";

        string value = settings.profileSettings.GetValueById(settings.activeProfileId, variableName);
        if (string.IsNullOrEmpty(value))
            return "(empty)";

        return settings.profileSettings.EvaluateString(settings.activeProfileId, value);
    }

    // Addressables Groups 窗口非公开 API，通过反射调用 Init 打开。
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

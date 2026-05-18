using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Legacy Pipeline 配置面板 —— 显示 Addressables Settings 摘要，并提供打开 Groups 窗口的入口。
/// </summary>
public sealed class LegacyConfigPanel : IBuildPipelinePanel
{
    private EditorWindow _window;

    public string PanelName => "Legacy Config";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
    }

    public void OnDisable()
    {
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);

        DrawToolbar();

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            DrawNoSettings();
            GUILayout.EndArea();
            return;
        }

        DrawSummary(settings);

        GUILayout.Space(10f);
        DrawOpenGroupsButton();

        GUILayout.EndArea();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Legacy Pipeline", EditorStyles.toolbarButton, GUILayout.Width(110f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(FYAssetSettings.Instance.VersionDataBasePath, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawNoSettings()
    {
        GUILayout.Space(24f);
        GUILayout.BeginVertical("box");
        GUILayout.Label("Addressables Settings not found", EditorStyles.boldLabel);
        GUILayout.Space(4f);
        GUILayout.Label("Create or select the default Addressables settings asset before opening Groups.", EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(8f);
        if (GUILayout.Button("Open Addressables Groups Window", GUILayout.Height(32f)))
            OpenGroupsWindow();
        GUILayout.EndVertical();
    }

    private void DrawSummary(AddressableAssetSettings settings)
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Addressables Summary", EditorStyles.boldLabel);
        GUILayout.Space(4f);

        string profileName = settings.profileSettings != null
            ? settings.profileSettings.GetProfileName(settings.activeProfileId)
            : "(no profile)";
        string buildPath = EvaluateProfilePath(settings, AddressableAssetSettings.kBuildPath);
        string loadPath = EvaluateProfilePath(settings, AddressableAssetSettings.kLoadPath);

        EditorGUILayout.LabelField("Groups", settings.groups != null ? settings.groups.Count.ToString() : "0");
        EditorGUILayout.LabelField("Active Profile", profileName ?? "(none)");
        EditorGUILayout.SelectableLabel("Build Path: " + buildPath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(16f));
        EditorGUILayout.SelectableLabel("Load Path: " + loadPath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(16f));
        GUILayout.EndVertical();
    }

    private void DrawOpenGroupsButton()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Groups Window", EditorStyles.boldLabel);
        GUILayout.Space(4f);
        if (GUILayout.Button("Open Addressables Groups Window", GUILayout.Height(34f)))
            OpenGroupsWindow();
        GUILayout.EndVertical();
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

    private static void OpenGroupsWindow()
    {
        Type windowType = typeof(AddressableAssetSettings).Assembly.GetType("UnityEditor.AddressableAssets.GUI.AddressableAssetsWindow");
        if (windowType == null)
        {
            Debug.LogWarning("[LegacyConfigPanel] Addressables Groups window type not found.");
            return;
        }

        MethodInfo initMethod = windowType.GetMethod("Init", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (initMethod == null)
        {
            Debug.LogWarning("[LegacyConfigPanel] Addressables Groups window Init method not found.");
            return;
        }

        initMethod.Invoke(null, null);
    }
}

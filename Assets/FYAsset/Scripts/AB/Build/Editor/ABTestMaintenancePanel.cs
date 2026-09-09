#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 测试维护面板：本地热更服务器、Reset Version、清空 AB 基线 Channel。
/// 只面向测试，不属于日常构建路径，执行前均要求确认。
/// </summary>
public sealed class ABTestMaintenancePanel : IBuildPipelinePanel
{
    private VisualElement _root;
    private Label _messageLabel;
    private Label _localServerStatusLabel;
    private IntegerField _localServerPortField;
    private Toggle _clearPackageIndexToggle;
    private Toggle _deletePackagesToggle;
    private Toggle _clearStartupBaselineToggle;

    public string PanelName => "Test";

    public void OnEnable(EditorWindow window)
    {
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        _root.Add(scroll);

        scroll.Add(CreateLocalServerCard());
        scroll.Add(CreateResetCard());

        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _root.Add(_messageLabel);

        RefreshLocalServerStatus(LocalHotfixServerController.GetStatus());
        return _root;
    }

    public void OnDisable()
    {
        _root = null;
    }

    private VisualElement CreateLocalServerCard()
    {
        var card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Local Hotfix Server"));

        _localServerPortField = new IntegerField("Local Port")
        {
            value = LocalHotfixServerController.Port,
            isDelayed = true
        };
        _localServerPortField.RegisterValueChangedCallback(evt =>
        {
            LocalHotfixServerController.Port = evt.newValue;
            _localServerPortField.SetValueWithoutNotify(LocalHotfixServerController.Port);
            RefreshLocalServerStatus(LocalHotfixServerController.GetStatus());
        });
        card.Add(_localServerPortField);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.marginTop = 4f;
        Button start = BuildPipelineUI.ToolbarButton("Start", () => RefreshLocalServerStatus(LocalHotfixServerController.Start()));
        Button stop = BuildPipelineUI.ToolbarButton("Stop", () => RefreshLocalServerStatus(LocalHotfixServerController.Stop()));
        Button status = BuildPipelineUI.ToolbarButton("Status", () => RefreshLocalServerStatus(LocalHotfixServerController.GetStatus()));
        start.style.flexGrow = 1f;
        stop.style.flexGrow = 1f;
        status.style.flexGrow = 1f;
        actions.Add(start);
        actions.Add(stop);
        actions.Add(status);
        card.Add(actions);

        _localServerStatusLabel = BuildPipelineUI.SmallText(string.Empty);
        _localServerStatusLabel.style.marginTop = 4f;
        card.Add(_localServerStatusLabel);
        return card;
    }

    private VisualElement CreateResetCard()
    {
        var card = BuildPipelineUI.Card();
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = BuildPipelineUI.Header("Test Reset (AB)");
        title.style.flexGrow = 1f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("Reset Version", RunResetVersionForTest, 104f));
        header.Add(BuildPipelineUI.ToolbarButton("Clear Channel", RunClearChannelForTest, 104f));
        card.Add(header);

        card.Add(BuildPipelineUI.SmallText("删除当前 Channel/AB 的 baseline.json。"));
        _clearPackageIndexToggle = new Toggle("Clear output PackageIndex.json");
        _deletePackagesToggle = new Toggle("Delete local package folders");
        _clearStartupBaselineToggle = new Toggle("Clear startup BuildIndex / StreamingAssets baseline");
        card.Add(_clearPackageIndexToggle);
        card.Add(_deletePackagesToggle);
        card.Add(_clearStartupBaselineToggle);
        return card;
    }

    private void RefreshLocalServerStatus(LocalHotfixServerStatus status)
    {
        if (_localServerStatusLabel == null)
            return;

        _localServerStatusLabel.text = $"{(status.IsRunning ? "Running" : "Stopped")} | {status.Message}";
    }

    private void RunResetVersionForTest()
    {
        VersionRecord versionDB = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        if (versionDB == null)
        {
            EditorUtility.DisplayDialog("Reset Version", "VersionRecord not found:\n\n" + FYAssetSettings.Instance.VersionRecordPath, "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Reset Version",
                "Reset VersionRecord to 1.0.0 for testing?\n\nThis clears Channel, LastBuildTime, and DailyBuildCount.",
                "Reset",
                "Cancel"))
            return;

        Undo.RecordObject(versionDB, "Reset VersionRecord");
        versionDB.CurrentVersion = new VersionNumber { Major = 1, Minor = 0, Patch = 0, Build = 0, Channel = string.Empty };
        versionDB.LastBuildTime = string.Empty;
        versionDB.DailyBuildCount = 0;
        EditorUtility.SetDirty(versionDB);
        AssetDatabase.SaveAssets();
        if (_messageLabel != null)
            _messageLabel.text = "VersionRecord 已重置为 1.0.0。";
    }

    private void RunClearChannelForTest()
    {
        bool clearPackageIndex = _clearPackageIndexToggle?.value == true;
        bool deletePackages = _deletePackagesToggle?.value == true;
        bool clearStartupBaseline = _clearStartupBaselineToggle?.value == true;

        VersionRecord versionDB = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        VersionNumber version = versionDB != null ? versionDB.CurrentVersion : default;
        string channelKey = BuildBaselineStore.GetChannelKey(version, BackendModeNames.AB);

        string message = $"Clear AB baseline channel for test?\n\nChannel: {channelKey}\n\nThis deletes the baseline.json for this channel.";
        if (clearPackageIndex)
            message += "\n- Clear output PackageIndex.json";
        if (deletePackages)
            message += "\n- Delete local package folders";
        if (clearStartupBaseline)
            message += "\n- Clear startup BuildIndex / StreamingAssets baseline";

        if (!EditorUtility.DisplayDialog("Clear Baseline Channel", message, "Clear", "Cancel"))
            return;

        try
        {
            BuildBaselineStore.ClearForTest(channelKey);
            if (clearPackageIndex)
            {
                var empty = new PackageIndex
                {
                    LatestPackage = string.Empty,
                    LatestVersion = default,
                    BackendMode = string.Empty
                };
                FileHelper.WriteAllTextAtomic(BuildPathManager.PackageIndexPath, SerializationUtility.SerializeToJson(empty, true));
            }
            if (deletePackages)
                DeleteLocalPackageFolders();
            if (clearStartupBaseline)
                ClearAbStartupBaseline();

            AssetDatabase.Refresh();
            if (_messageLabel != null)
                _messageLabel.text = $"AB baseline channel cleared: {channelKey}";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABTestMaintenancePanel)}] 清理 Channel 失败：{ex}");
            if (_messageLabel != null)
                _messageLabel.text = ex.Message;
        }
    }

    private static void DeleteLocalPackageFolders()
    {
        string root = BuildPathManager.PackagesDir;
        string[] dirs = FileHelper.GetDirectories(root, "Build_*");
        for (int i = 0; i < dirs.Length; i++)
        {
            // 删除仅限 PackagesDir 直接子目录，防止配置错误指向项目外部。
            if (!IsSafeChildPath(root, dirs[i]))
            {
                Debug.LogError($"[{nameof(ABTestMaintenancePanel)}] 拒绝删除 PackagesDir 外的路径：{dirs[i]}");
                continue;
            }

            FileHelper.TryDeleteDirectory(dirs[i], true);
        }
    }

    private static void ClearAbStartupBaseline()
    {
        FileHelper.TryDelete(ResolveProjectPath(FYAssetSettings.Instance.BuildIndexJsonPath));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUILD_INDEX_FILENAME));
        FileHelper.TryDeleteDirectory(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUNDLES_DIRECTORY_NAME), true);

        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN));
    }

    private static string ResolveProjectPath(string path)
    {
        return Path.IsPathRooted(path)
            ? FYAssetPathUtility.NormalizePath(path)
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, path);
    }

    private static bool IsSafeChildPath(string rootPath, string childPath)
    {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(childPath))
            return false;

        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return child.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(child, root, StringComparison.OrdinalIgnoreCase);
    }
}
#endif

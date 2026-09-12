#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 测试维护面板：本地热更服务器、Reset Build Facts、清空 AB 本地构建输出。
/// 只面向测试，不属于日常构建路径，执行前均要求确认。
/// </summary>
public sealed class ABTestMaintenancePanel : IBuildPipelinePanel
{
    private VisualElement _root;
    private Label _messageLabel;
    private Label _localServerStatusLabel;
    private IntegerField _localServerPortField;
    private Toggle _deletePackagesToggle;
    private Toggle _clearStartupBuiltInToggle;

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
        header.Add(BuildPipelineUI.ToolbarButton("Reset Build Facts", RunResetVersionForTest, 130f));
        header.Add(BuildPipelineUI.ToolbarButton("Clear Local State", RunClearLocalStateForTest, 120f));
        card.Add(header);

        card.Add(BuildPipelineUI.SmallText("清空 AB 本地构建输出（不影响服务器上的已发布包）。"));
        _deletePackagesToggle = new Toggle("Delete local package folders");
        _clearStartupBuiltInToggle = new Toggle("Clear startup BuildIndex / StreamingAssets built-in package");
        card.Add(_deletePackagesToggle);
        card.Add(_clearStartupBuiltInToggle);
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
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        if (!EditorUtility.DisplayDialog(
                "Reset Build Facts",
                "清空 BuildData/Summaries（全部成功摘要与索引）？\n\n项目版本将回到 1.0.0，历史包目录不会被删除。",
                "Reset",
                "Cancel"))
            return;

        if (store.TryResetAll(out string error))
        {
            if (_messageLabel != null)
                _messageLabel.text = "构建事实已清空（Summary 与 Index）。";
        }
        else
        {
            EditorUtility.DisplayDialog("Reset Build Facts", error, "OK");
        }
    }

    private void RunClearLocalStateForTest()
    {
        bool deletePackages = _deletePackagesToggle?.value == true;
        bool clearStartupBuiltIn = _clearStartupBuiltInToggle?.value == true;

        string message = "清空 AB 本地构建状态？";
        if (deletePackages)
            message += "\n- Delete local package folders";
        if (clearStartupBuiltIn)
            message += "\n- Clear startup BuildIndex / StreamingAssets built-in package";

        if (!EditorUtility.DisplayDialog("Clear Local Build State", message, "Clear", "Cancel"))
            return;

        try
        {
            if (deletePackages)
                DeleteLocalPackageFolders();
            if (clearStartupBuiltIn)
                ClearAbStartupBuiltIn();

            AssetDatabase.Refresh();
            if (_messageLabel != null)
                _messageLabel.text = "AB 本地构建状态已清理。";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABTestMaintenancePanel)}] 清理本地构建状态失败：{ex}");
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

    private static void ClearAbStartupBuiltIn()
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

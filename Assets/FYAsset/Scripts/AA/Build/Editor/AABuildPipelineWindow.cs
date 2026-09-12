using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AA 专用构建管线窗口。
/// </summary>
public sealed class AABuildPipelineWindow : BuildPipelineWindowBase
{
    [MenuItem("FYAsset/Build/AA Build Pipeline")]
    public static void Open()
    {
        AABuildPipelineWindow window = GetWindow<AABuildPipelineWindow>();
        window.titleContent = new GUIContent("AA Build Pipeline");
        window.minSize = new Vector2(800f, 500f);
        window.Show();
    }

    protected override IBuildPipelinePanel[] CreatePanels()
    {
        return new IBuildPipelinePanel[]
        {
            new SettingsPanel(),
            new AAConfigPanel(),
            new AABuildPanel(),
            new AAReportPanel(),
            new PublishTargetPanel(BackendModeNames.AA, ApplyHotfixUrl, AAPackageManifestReader.Instance),
            new AAHotfixGroupMaintenancePanel()
        };
    }

    private static void ApplyHotfixUrl(string url)
    {
        Undo.RecordObject(FYAssetAASettings.Instance, "Apply Hotfix URL");
        FYAssetAASettings.Instance.HotfixUrl = url;
        EditorUtility.SetDirty(FYAssetAASettings.Instance);
        AssetDatabase.SaveAssets();
    }
}

/// <summary>
/// 恢复被移动的 AA Hotfix Group 记录；只调用 AABuildProjectManager 中的既有命令。
/// </summary>
public sealed class AAHotfixGroupMaintenancePanel : IBuildPipelinePanel
{
    public string PanelName => "Hotfix Groups";

    public void OnEnable(EditorWindow window)
    {
    }

    public void OnDisable()
    {
        _root = null;
    }

    private VisualElement _root;
    private Label _statusLabel;
    private Label _messageLabel;
    private Button _restoreButton;
    private Button _discardButton;

    public VisualElement CreateContent()
    {
        _root = BuildPipelineUI.Card();
        _root.style.flexShrink = 0f;

        _statusLabel = BuildPipelineUI.SmallText(string.Empty);
        _statusLabel.style.marginBottom = 4f;
        _root.Add(_statusLabel);

        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _messageLabel.style.marginBottom = 6f;
        _root.Add(_messageLabel);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;

        _restoreButton = new Button(RunRestore)
        {
            text = "Restore Groups"
        };
        _restoreButton.style.flexGrow = 1f;
        _restoreButton.style.minWidth = 0f;
        _restoreButton.style.whiteSpace = WhiteSpace.Normal;
        _restoreButton.style.marginRight = 4f;
        actions.Add(_restoreButton);

        _discardButton = new Button(RunDiscard)
        {
            text = "Discard Unrestorable"
        };
        _discardButton.style.flexGrow = 1f;
        _discardButton.style.minWidth = 0f;
        _discardButton.style.whiteSpace = WhiteSpace.Normal;
        actions.Add(_discardButton);
        _root.Add(actions);

        Refresh();
        return _root;
    }

    public void Refresh()
    {
        if (_root == null)
            return;

        HotfixGroupRestoreStatus status = AABuildProjectManager.GetHotfixGroupRestoreStatus();
        if (status.PendingCount == 0)
        {
            _statusLabel.text = "No pending hotfix group moves.";
            _messageLabel.text = string.Empty;
            _restoreButton.SetEnabled(false);
            _discardButton.SetEnabled(false);
            return;
        }

        _statusLabel.text = $"Pending {status.PendingCount}  |  Restore {status.RestorableCount}  |  Default {status.DefaultGroupFallbackCount}  |  Unrestorable {status.UnrestorableCount}";
        _messageLabel.text = string.IsNullOrEmpty(status.ErrorMessage)
            ? string.Empty
            : status.ErrorMessage;
        _restoreButton.SetEnabled(status.RestorableCount > 0);
        _discardButton.SetEnabled(status.CanDiscardUnrestorableRecords);
    }

    private void RunRestore()
    {
        HotfixGroupRestoreResult result = AABuildProjectManager.RestoreGroupsToOriginal();
        Refresh();
        _messageLabel.text = result.Cancelled ? "Restore cancelled." : result.Message;
    }

    private void RunDiscard()
    {
        HotfixGroupRestoreStatus status = AABuildProjectManager.GetHotfixGroupRestoreStatus();
        if (!status.CanDiscardUnrestorableRecords)
            return;

        bool confirmed = EditorUtility.DisplayDialog(
            "Discard Unrestorable Records",
            $"Discard {status.UnrestorableCount} unrestorable hotfix group record(s)?\n\n" +
            "This only removes undo-log records. It does not move resources, delete HotfixGroup, or change build outputs.",
            "Discard Records",
            "Cancel");
        if (!confirmed)
            return;

        HotfixGroupRestoreResult result = AABuildProjectManager.DiscardUnrestorableGroupRecords();
        Refresh();
        _messageLabel.text = result.Message;
    }
}

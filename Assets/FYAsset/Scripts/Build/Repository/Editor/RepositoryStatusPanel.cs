#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Build Repository 状态与 Diff Preview 面板。
/// 只提供 status 和 diff 入口，不扩展完整 repo 操作面板。
/// </summary>
public sealed class RepositoryStatusPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private VisualElement _root;
    private Label _statusLabel;
    private TextField _diffText;
    private DropdownField _targetDropdown;
    private TextField _fromVersionField;
    private TextField _toVersionField;
    private TextField _pushHistoryText;

    public string PanelName => "仓库";

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
        _statusLabel = null;
        _diffText = null;
        _targetDropdown = null;
        _fromVersionField = null;
        _toVersionField = null;
        _pushHistoryText = null;
    }

    public void SetVisible(bool visible)
    {
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();

        var toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("刷新", Rebuild, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Diff", RunDiff, 52f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Push", RunPush, 52f));
        toolbar.Add(BuildPipelineUI.Spacer());
        _statusLabel = BuildPipelineUI.ToolbarLabel("无状态");
        toolbar.Add(_statusLabel);
        _root.Add(toolbar);

        var pushCard = BuildPipelineUI.Card();
        pushCard.Add(BuildPipelineUI.Header("Push"));
        _targetDropdown = new DropdownField("Target", GetPushTargetLabels(), 0);
        _fromVersionField = new TextField("From") { value = string.Empty };
        _toVersionField = new TextField("To") { value = string.Empty };
        pushCard.Add(_targetDropdown);
        pushCard.Add(_fromVersionField);
        pushCard.Add(_toVersionField);
        _root.Add(pushCard);

        _diffText = new TextField { multiline = true, isReadOnly = true };
        _diffText.style.flexGrow = 1f;
        _root.Add(_diffText);

        _pushHistoryText = new TextField { multiline = true, isReadOnly = true };
        _pushHistoryText.style.flexGrow = 1f;
        _root.Add(_pushHistoryText);

        RefreshStatus();
        RefreshPushHistory();
    }

    private void RefreshStatus()
    {
        var request = CreatePreviewRequest();
        var status = BuildRepositoryFacade.GetStatus(request);
        _statusLabel.text = status.HasHead
            ? $"HEAD {status.HeadVersion} | {status.PackageName} | {status.ArtifactCount} 项 | LastPush {status.LastPushTargetId} {status.LastPushAtUtc}"
            : status.HasHeadError
                ? $"HEAD 错误 | {status.HeadErrorReason}"
            : $"无 HEAD | {status.ChannelKey}";
    }

    private void RunDiff()
    {
        try
        {
            var request = CreatePreviewRequest();
            ArtifactDelta delta = FYAssetSettings.Instance.UseABBackend
                ? RepositoryPreviewRunner.RunABPreview(request)
                : RepositoryPreviewRunner.RunAAPreview(request);
            _diffText.value = FormatDelta(delta);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Diff 失败: {ex}");
            _diffText.value = ex.Message;
        }
    }

    private void RunPush()
    {
        try
        {
            var request = CreatePreviewRequest();
            var channelKey = BuildRepositoryFacade.GetChannelKey(request);
            var target = CreatePushTarget();
            var fromVersion = ParseVersion(_fromVersionField != null ? _fromVersionField.value : string.Empty);
            var toVersion = ParseVersion(_toVersionField != null && !string.IsNullOrEmpty(_toVersionField.value) ? _toVersionField.value : request.Version.GetFullVersionString());
            var receipt = BuildRepositoryFacade.Push(channelKey, fromVersion, toVersion, target);
            _pushHistoryText.value = FormatPushHistory(BuildRepositoryFacade.ListPushHistory(channelKey));
            _statusLabel.text = receipt.Success ? $"Push 成功: {receipt.TargetId}" : receipt.FailureReason;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Push 失败: {ex}");
            _statusLabel.text = ex.Message;
        }
    }

    private void RefreshPushHistory()
    {
        var request = CreatePreviewRequest();
        var history = BuildRepositoryFacade.ListPushHistory(BuildRepositoryFacade.GetChannelKey(request));
        _pushHistoryText.value = FormatPushHistory(history);
    }

    private static BuildPackageRequest CreatePreviewRequest()
    {
        var versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetBuildSettingsProvider.Shared.VersionDataBasePath);
        var version = versionDB != null && versionDB.CurrentVersion != null
            ? versionDB.CurrentVersion
            : new VersionNumber { Major = 0, Minor = 0, Patch = 0 };
        var backendMode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;
        return BuildPackageRequest.Create(version, BuildType.Full, backendMode);
    }

    private static string FormatDelta(ArtifactDelta delta)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Added: {delta.Added.Count}");
        for (int i = 0; i < delta.Added.Count; i++)
            builder.AppendLine(" + " + delta.Added[i].Name);
        builder.AppendLine($"Modified: {delta.Modified.Count}");
        for (int i = 0; i < delta.Modified.Count; i++)
            builder.AppendLine(" * " + delta.Modified[i].Name);
        builder.AppendLine($"Removed: {delta.Removed.Count}");
        for (int i = 0; i < delta.Removed.Count; i++)
            builder.AppendLine(" - " + delta.Removed[i]);
        return builder.ToString();
    }

    private static VersionNumber ParseVersion(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : VersionNumber.Parse(value);
    }

    private IPushTarget CreatePushTarget()
    {
        var settings = FYAssetBuildSettingsProvider.Shared;
        string targetId = _targetDropdown != null && !string.IsNullOrEmpty(_targetDropdown.value)
            ? _targetDropdown.value
            : (settings.PushTargets != null && settings.PushTargets.Count > 0 ? settings.PushTargets[0].Id : "local");
        for (int i = 0; i < settings.PushTargets.Count; i++)
        {
            var config = settings.PushTargets[i];
            if (config != null && string.Equals(config.Id, targetId, System.StringComparison.OrdinalIgnoreCase))
                return new LocalDirectoryPushTarget(config);
        }
        throw new System.InvalidOperationException("No push target configured.");
    }

    private static System.Collections.Generic.List<string> GetPushTargetLabels()
    {
        var labels = new System.Collections.Generic.List<string>();
        var settings = FYAssetBuildSettingsProvider.Shared;
        for (int i = 0; i < settings.PushTargets.Count; i++)
        {
            var config = settings.PushTargets[i];
            if (config != null && !string.IsNullOrEmpty(config.Id))
                labels.Add(config.Id);
        }
        if (labels.Count == 0)
            labels.Add("local");
        return labels;
    }

    private static string FormatPushHistory(System.Collections.Generic.List<PushHistoryEntry> history)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < history.Count; i++)
        {
            var item = history[i];
            builder.AppendLine($"{item.PushedAtUtc} | {item.FromVersion} -> {item.ToVersion} | {item.TargetId} | {item.TargetLocation} | {item.DeltaFileCount}");
        }
        return builder.ToString();
    }
}
#endif

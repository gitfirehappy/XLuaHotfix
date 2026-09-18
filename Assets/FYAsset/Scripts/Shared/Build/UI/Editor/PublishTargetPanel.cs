#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>发布面板：选择正式 Summary 制品和 PublishTargetConfig 后执行发布。</summary>
public sealed class PublishTargetPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _backendKey;
    private readonly Action<string> _applyHotfixUrl;
    private readonly Func<string> _getCurrentTargetId;
    private readonly Action<string> _setCurrentTargetId;
    private readonly IPackageManifestReader _manifestReader;
    private readonly Func<IFullPackageBaselineSource> _baselineSourceFactory;
    private VisualElement _root;
    private DropdownField _targetDropdown;
    private DropdownField _sourceDropdown;
    private Label _message;
    private readonly List<string> _targetIds = new();
    private readonly List<PublishSourceCandidate> _sources = new();

    public PublishTargetPanel(string backendKey, Action<string> applyHotfixUrl, IPackageManifestReader manifestReader, Func<IFullPackageBaselineSource> baselineSourceFactory = null)
    {
        _backendKey = backendKey ?? throw new ArgumentNullException(nameof(backendKey));
        _applyHotfixUrl = applyHotfixUrl ?? throw new ArgumentNullException(nameof(applyHotfixUrl));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _baselineSourceFactory = baselineSourceFactory;
    }

    public PublishTargetPanel(string backendKey, Func<string> getCurrentTargetId, Action<string> setCurrentTargetId, IPackageManifestReader manifestReader, Func<IFullPackageBaselineSource> baselineSourceFactory = null)
    {
        _backendKey = backendKey ?? throw new ArgumentNullException(nameof(backendKey));
        _getCurrentTargetId = getCurrentTargetId ?? throw new ArgumentNullException(nameof(getCurrentTargetId));
        _setCurrentTargetId = setCurrentTargetId ?? throw new ArgumentNullException(nameof(setCurrentTargetId));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _baselineSourceFactory = baselineSourceFactory;
    }

    public string PanelName => "Publish";

    public void OnEnable(EditorWindow window) { }
    public void OnDisable() => _root = null;
    public void SetVisible(bool visible) { if (_root != null) _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexDirection = FlexDirection.Column;
        _root.style.flexGrow = 1f;
        _targetDropdown = new DropdownField("Target");
        _sourceDropdown = new DropdownField("Summary");
        _message = new Label();
        var refresh = new Button(Rebuild) { text = "Refresh" };
        var publish = new Button(Publish) { text = "Publish" };
        _root.Add(_targetDropdown);
        _root.Add(_sourceDropdown);
        _root.Add(refresh);
        _root.Add(publish);
        _root.Add(_message);
        Rebuild();
        return _root;
    }

    private void Rebuild()
    {
        if (_root == null)
            return;
        _targetIds.Clear();
        var targetNames = new List<string>();
        List<PublishTargetConfig> targets = FYAssetSettings.Instance.PublishTargets;
        for (int i = 0; targets != null && i < targets.Count; i++)
        {
            PublishTargetConfig target = targets[i];
            if (target == null || string.IsNullOrWhiteSpace(target.TargetId))
                continue;
            _targetIds.Add(target.TargetId);
            targetNames.Add(string.IsNullOrWhiteSpace(target.Name) ? target.TargetId : target.Name);
        }
        _targetDropdown.choices = targetNames;
        int targetIndex = _targetIds.FindIndex(id => string.Equals(id, CurrentTargetId(), StringComparison.OrdinalIgnoreCase));
        _targetDropdown.index = targetIndex >= 0 ? targetIndex : (targetNames.Count > 0 ? 0 : -1);
        _sources.Clear();
        _sources.AddRange(PublishSourceCatalog.Read(_backendKey, _manifestReader));
        var sourceNames = new List<string>();
        for (int i = 0; i < _sources.Count; i++) sourceNames.Add(_sources[i].Label);
        _sourceDropdown.choices = sourceNames;
        _sourceDropdown.index = sourceNames.Count > 0 ? sourceNames.Count - 1 : -1;
        if (_message != null) _message.text = $"Targets={targetNames.Count}, Summaries={sourceNames.Count}";
    }

    private string CurrentTargetId()
    {
        return _getCurrentTargetId != null ? _getCurrentTargetId() : (_targetIds.Count > 0 ? _targetIds[0] : string.Empty);
    }

    private void Publish()
    {
        if (_targetDropdown.index < 0 || _targetDropdown.index >= _targetIds.Count)
        {
            SetMessage("未选择发布目标。");
            return;
        }
        if (_sourceDropdown.index < 0 || _sourceDropdown.index >= _sources.Count)
        {
            SetMessage("未选择有效构建 Summary。");
            return;
        }
        string targetId = _targetIds[_targetDropdown.index];
        if (_setCurrentTargetId != null) _setCurrentTargetId(targetId);
        if (!FYAssetSettings.Instance.TryResolvePublishTarget(targetId, out PublishTargetConfig target, out string targetError))
        {
            SetMessage(targetError);
            return;
        }
        PublishSourceCandidate source = _sources[_sourceDropdown.index];
        if (!source.IsValid)
        {
            SetMessage(source.InvalidReason);
            return;
        }
        CompleteBuildSummary.SummaryDocument document = source.Document;
        if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
        {
            SetMessage("Summary 版本无效。");
            return;
        }
        var request = new PublishRequest
        {
            BackendKey = _backendKey,
            SourcePackageDir = source.SourcePackageDir,
            ManifestReader = _manifestReader,
            PackagesFolderName = FYAssetSettings.Instance.BuildPackagesFolderName,
            Identity = new PackageBuildIdentity
            {
                PackageName = document.BuildId,
                Version = version,
                BackendId = document.BackendId,
                BuildType = document.BuildType
            },
            BaseFullSummaryId = document.BaseFullSummaryId,
            FullPackageBaselineSource = _baselineSourceFactory?.Invoke()
        };
        PublishResult result = PackagePublisher.Publish(request, target);
        SetMessage(result.Success ? $"发布完成：{result.TransferMode}" : result.Error);
        if (result.Success && !string.IsNullOrEmpty(result.TargetLocation))
            _applyHotfixUrl(target.GetHotfixUrl(_backendKey));
    }

    private void SetMessage(string message)
    {
        if (_message != null) _message.text = message ?? string.Empty;
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 发布面板：从正式 Summary 选择制品，并发布到显式选择的目标。
/// </summary>
/// <remarks>
/// 面板从正式 Summary 取得包目录；BuildPublisher 负责组装、校验和提交 PackageIndex，旧包清理是独立动作。
/// </remarks>
public sealed class PublishTargetPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _backendKey;
    private readonly Action<string> _applyHotfixUrl;
    private readonly Func<string> _getCurrentTargetId;
    private readonly Action<string> _setCurrentTargetId;
    private readonly IPackageManifestReader _manifestReader;

    private VisualElement _root;
    private Label _statusBadge;
    private Label _messageLabel;
    private Label _sourceLabel;
    private Label _identityLabel;
    private Label _cacheLabel;
    private DropdownField _targetDropdown;
    private DropdownField _sourceDropdown;
    private Button _pushButton;
    private readonly List<string> _targetIds = new List<string>();
    private readonly List<PublishSourceCandidate> _sourceCandidates = new List<PublishSourceCandidate>();

    public PublishTargetPanel(string backendKey, Action<string> applyHotfixUrl, IPackageManifestReader manifestReader)
    {
        _backendKey = RequireBackendKey(backendKey);
        _applyHotfixUrl = applyHotfixUrl ?? throw new ArgumentNullException(nameof(applyHotfixUrl));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
    }

    public PublishTargetPanel(
        string backendKey,
        Func<string> getCurrentTargetId,
        Action<string> setCurrentTargetId,
        IPackageManifestReader manifestReader)
    {
        _backendKey = RequireBackendKey(backendKey);
        _getCurrentTargetId = getCurrentTargetId ?? throw new ArgumentNullException(nameof(getCurrentTargetId));
        _setCurrentTargetId = setCurrentTargetId ?? throw new ArgumentNullException(nameof(setCurrentTargetId));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
    }

    private static string RequireBackendKey(string backendKey)
    {
        if (string.IsNullOrEmpty(backendKey))
            throw new ArgumentNullException(nameof(backendKey));
        return backendKey;
    }

    public string PanelName => "Publish";

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

    public void SetVisible(bool visible)
    {
        if (visible)
            Refresh();
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Add(CreateHeader());
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.Add(CreateSourceCard());
        scroll.Add(CreatePushCard());
        scroll.Add(CreateMaintenanceCard());
        scroll.Add(CreateTargetEditor());
        _root.Add(scroll);

        Refresh();
    }

    private VisualElement CreateHeader()
    {
        var header = BuildPipelineUI.Card();
        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;

        var titleBox = new VisualElement();
        titleBox.style.flexGrow = 1f;
        titleBox.style.minWidth = 0f;
        var title = BuildPipelineUI.Header(_backendKey + " Publish");
        title.style.fontSize = 14f;
        titleBox.Add(title);
        top.Add(titleBox);
        top.Add(BuildPipelineUI.ToolbarButton("Refresh", Rebuild, 70f));
        _pushButton = BuildPipelineUI.ToolbarButton("Push", RunPush, 60f);
        top.Add(_pushButton);
        header.Add(top);

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginTop = 6f;
        _statusBadge = CreateBadge(string.Empty, Color.clear);
        _statusBadge.style.display = DisplayStyle.None;
        statusRow.Add(_statusBadge);
        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _messageLabel.style.marginLeft = 8f;
        _messageLabel.style.flexGrow = 1f;
        _messageLabel.style.whiteSpace = WhiteSpace.Normal;
        statusRow.Add(_messageLabel);
        header.Add(statusRow);
        return header;
    }

    private VisualElement CreateSourceCard()
    {
        var card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Publish Source"));

        _sourceDropdown = new DropdownField("Source", new List<string> { "(none)" }, 0);
        _sourceDropdown.style.flexGrow = 1f;
        SetCompactFieldLabel(_sourceDropdown, 52f);
        _sourceDropdown.RegisterValueChangedCallback(_ => RefreshSourceFacts());
        card.Add(_sourceDropdown);

        var stats = new VisualElement();
        stats.style.flexDirection = FlexDirection.Row;
        _identityLabel = AddStat(stats, "Package", "-");
        _sourceLabel = AddStat(stats, "Path", "-");
        _cacheLabel = AddStat(stats, "Publish Cache", "-");
        card.Add(stats);
        return card;
    }

    private VisualElement CreatePushCard()
    {
        var card = BuildPipelineUI.Card();

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        List<string> labels = GetPushTargetLabels();
        _targetDropdown = new DropdownField("Target", labels, ResolveInitialTargetIndex());
        _targetDropdown.style.flexGrow = 1f;
        SetCompactFieldLabel(_targetDropdown, 48f);
        _targetDropdown.RegisterValueChangedCallback(_ => HandleTargetSelectionChanged());
        row.Add(_targetDropdown);
        if (_applyHotfixUrl != null)
        {
            Button applyUrl = BuildPipelineUI.ToolbarButton("Apply URL", RunApplyTargetUrl, 78f);
            applyUrl.style.marginLeft = 6f;
            row.Add(applyUrl);
        }
        card.Add(row);

        card.Add(BuildPipelineUI.SmallText(
            "Push 读取服务器 PackageIndex 与 Manifest，复用已有 Hash 内容，最后写入 PackageIndex；"
            + "服务器事实不可用时退化为完整上传。"));
        return card;
    }

    private VisualElement CreateMaintenanceCard()
    {
        var card = BuildPipelineUI.Card();
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = BuildPipelineUI.Header("Maintenance");
        title.style.flexGrow = 1f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("Delete Old Packages", RunDeleteOldPackages, 150f));
        card.Add(header);
        card.Add(BuildPipelineUI.SmallText(
            "只删除目标服务器上不被当前 PackageIndex 指向的包目录；索引不可读时拒绝清理。"));
        return card;
    }

    private VisualElement CreateTargetEditor()
    {
        var card = BuildPipelineUI.Card();
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = BuildPipelineUI.Header("Push Targets");
        title.style.flexGrow = 1f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("+ Target", AddPushTarget, 86f));
        card.Add(header);

        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || settings.PushTargets.Count == 0)
        {
            card.Add(BuildPipelineUI.SmallText("No Push Target configured."));
            return card;
        }

        for (int i = 0; i < settings.PushTargets.Count; i++)
            card.Add(CreatePushTargetRow(settings.PushTargets[i], i));
        return card;
    }

    private VisualElement CreatePushTargetRow(PushTargetConfig config, int index)
    {
        var container = new VisualElement();
        container.style.marginTop = 6f;
        container.style.paddingBottom = 6f;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        var nameField = new TextField("Name")
        {
            value = config != null ? config.Name : string.Empty,
            isDelayed = true
        };
        nameField.style.flexGrow = 1f;
        nameField.style.minWidth = 120f;
        nameField.style.marginRight = 6f;
        SetCompactFieldLabel(nameField, 38f);
        nameField.RegisterValueChangedCallback(evt => SaveTargetName(config, evt.newValue));
        row.Add(nameField);

        var typeField = new EnumField("Type", config != null ? config.Type : PushTargetType.LocalDirectory);
        typeField.RegisterValueChangedCallback(evt =>
        {
            if (config == null || evt.newValue is not PushTargetType value)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target Type");
            config.Type = value;
            SaveSettings();
            Rebuild();
        });
        row.Add(typeField);

        Button remove = BuildPipelineUI.ToolbarButton("Delete", () => RemovePushTarget(index), 64f);
        remove.style.marginLeft = 6f;
        row.Add(remove);
        container.Add(row);

        if (config != null)
            container.Add(BuildPipelineUI.SmallText("TargetId: " + config.TargetId));

        SerializedProperty pathProperty = new SerializedObject(FYAssetSettings.Instance)
            .FindProperty(nameof(FYAssetSettings.PushTargets))
            .GetArrayElementAtIndex(index)
            .FindPropertyRelative(nameof(PushTargetConfig.Path));
        VisualElement path = BuildPipelineUI.PathField(pathProperty, "Path", BuildPipelineUI.PathPickerMode.ProjectFolder, 34f);
        container.Add(path);

        var urlField = new TextField("Public URL")
        {
            value = config != null ? config.PublicBaseUrl : string.Empty,
            isDelayed = true
        };
        urlField.RegisterValueChangedCallback(evt => SaveTargetUrl(config, evt.newValue));
        container.Add(urlField);

        if (config != null)
        {
            string note = config.Type == PushTargetType.CloudflarePages
                ? "Cloudflare Pages 由 Compat 部署入口处理。"
                : $"发布目录：(所选 Path，空为 OutputRoot)/{_backendKey}";
            if (config.TryGetHotfixUrl(_backendKey, out string preview, out string error))
                note += "  URL: " + preview;
            else
                note += "  URL 无效: " + error;
            container.Add(BuildPipelineUI.SmallText(note));
        }

        return container;
    }

    /// <summary>刷新源目录候选与目标下拉，并展示当前源目录的包身份与发布缓存状态。</summary>
    private void Refresh()
    {
        if (_root == null)
            return;

        RefreshSourceCandidates();
        RefreshSourceFacts();
    }

    private void RefreshSourceCandidates()
    {
        if (_sourceDropdown == null)
            return;

        string previousSummaryId = ResolveSelectedSourceCandidate()?.SummaryId;
        _sourceCandidates.Clear();
        _sourceCandidates.AddRange(PublishSourceCatalog.Read(_backendKey, _manifestReader));

        var labels = new List<string>();
        for (int i = 0; i < _sourceCandidates.Count; i++)
            labels.Add(_sourceCandidates[i].Label);
        if (labels.Count == 0)
            labels.Add("(none)");

        _sourceDropdown.choices = labels;
        int selectedIndex = 0;
        for (int i = 0; i < _sourceCandidates.Count; i++)
        {
            if (string.Equals(_sourceCandidates[i].SummaryId, previousSummaryId, StringComparison.Ordinal))
            {
                selectedIndex = i;
                break;
            }
        }
        _sourceDropdown.SetValueWithoutNotify(labels[selectedIndex]);
    }

    private string DescribeSelectedIdentity(PublishSourceCandidate candidate)
    {
        if (candidate == null)
            return "未选择发布源。";
        if (!candidate.IsValid)
            return "不可发布：" + candidate.InvalidReason;

        CompleteBuildSummary.SummaryDocument document = candidate.Document;
        return string.IsNullOrEmpty(document.BuildType)
            ? $"{document.BuildId} | {document.Version}"
            : $"{document.BuildId} | {document.Version} | {document.BuildType}";
    }

    private void RefreshSourceFacts()
    {
        PublishSourceCandidate candidate = ResolveSelectedSourceCandidate();
        if (_sourceLabel != null)
            _sourceLabel.text = string.IsNullOrEmpty(candidate?.SourcePackageDir) ? "-" : candidate.SourcePackageDir;
        if (_identityLabel != null)
            _identityLabel.text = DescribeSelectedIdentity(candidate);
        if (_pushButton != null)
            _pushButton.SetEnabled(candidate != null && candidate.IsValid && GetSelectedTargetConfigOrNull() != null);

        if (_cacheLabel != null)
        {
            string cachePath = ResolvePublishCachePath();
            _cacheLabel.text = string.IsNullOrEmpty(cachePath)
                ? "-"
                : (PublishCacheStore.Exists(cachePath) ? cachePath : "无（不影响发布正确性）");
        }
    }

    private PublishSourceCandidate ResolveSelectedSourceCandidate()
    {
        if (_sourceDropdown == null || _sourceCandidates.Count == 0)
            return null;

        int index = _sourceDropdown.index;
        return index >= 0 && index < _sourceCandidates.Count ? _sourceCandidates[index] : null;
    }

    private void RunPush()
    {
        try
        {
            PushTargetConfig config = GetSelectedTargetConfig();
            if (config.Type != PushTargetType.LocalDirectory)
                throw new InvalidOperationException(
                    $"面板 Push 只支持目录型目标；'{config.Type}' 请使用 Compat 部署入口。");

            PublishSourceCandidate source = ResolveSelectedSourceCandidate();
            if (source == null || !source.IsValid)
                throw new InvalidOperationException(source?.InvalidReason ?? "没有可发布的正式 Summary。");

            string sourceDir = source.SourcePackageDir;
            CompleteBuildSummary.SummaryDocument document = source.Document;
            if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
                throw new InvalidOperationException("正式 Summary 版本无法解析: " + document.Version);

            var request = new PublishRequest
            {
                BackendKey = _backendKey,
                SourcePackageDir = sourceDir,
                TargetId = config.TargetId,
                ManifestReader = _manifestReader,
                PublishCachePath = ResolvePublishCachePath(),
                PackagesFolderName = FYAssetSettings.Instance.BuildPackagesFolderName,
                Identity = new PackageBuildIdentity
                {
                    PackageName = document.BuildId,
                    Version = version,
                    BackendId = document.BackendId,
                    BuildType = document.BuildType
                },
                BaseFullSummaryId = document.BaseFullSummaryId
            };

            PushReceipt receipt = BuildPublisher.Push(request, new LocalDirectoryPushTarget(config));
            Refresh();

            if (receipt != null && receipt.Success)
            {
                SetBadge(receipt.DegradedToFullUpload ? "Push OK (Full)" : "Push OK",
                    receipt.DegradedToFullUpload ? new Color(0.60f, 0.45f, 0.12f) : new Color(0.18f, 0.48f, 0.28f));
                _messageLabel.text =
                    $"Package={receipt.TargetLocation} 上传={receipt.UploadedCount} 复用={receipt.ReusedCount}";
            }
            else
            {
                SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = receipt != null ? receipt.FailureReason : "Push returned null receipt.";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(PublishTargetPanel)}] Push 失败：{ex}");
            SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunDeleteOldPackages()
    {
        try
        {
            PushTargetConfig config = GetSelectedTargetConfig();
            if (config.Type != PushTargetType.LocalDirectory)
                throw new InvalidOperationException("旧包清理只对目录型目标生效。");

            string backendRoot = config.ResolveBackendRoot(_backendKey);
            if (!EditorUtility.DisplayDialog(
                    "Delete Old Packages",
                    $"删除 {backendRoot} 下不被 PackageIndex 指向的包目录？\n\n当前 PackageIndex 指向的包不会被删除。",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            PublishMaintenance.CleanupResult result = PublishMaintenance.DeleteUnreferencedPackages(
                backendRoot,
                FYAssetSettings.Instance.BuildPackagesFolderName);

            if (!result.Success)
            {
                SetBadge("Cleanup Refused", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = result.FailureReason;
                return;
            }

            SetBadge("Cleanup OK", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text =
                $"保留={result.KeptPackage}, 删除={result.DeletedPackages.Count}, 跳过={result.SkippedEntries.Count}";
        }
        catch (Exception ex)
        {
            SetBadge("Cleanup Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunApplyTargetUrl()
    {
        try
        {
            PushTargetConfig config = GetSelectedTargetConfig();
            string url = config.GetHotfixUrl(_backendKey);
            _applyHotfixUrl(url);

            SetBadge("URL Applied", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = $"{_backendKey} HotfixUrl -> {url}";
        }
        catch (Exception ex)
        {
            SetBadge("URL Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void AddPushTarget()
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        Undo.RecordObject(settings, "Add Push Target");
        settings.PushTargets ??= new List<PushTargetConfig>();
        settings.PushTargets.Add(new PushTargetConfig
        {
            TargetId = Guid.NewGuid().ToString("D"),
            Name = CreateUniqueTargetName(settings.PushTargets),
            Type = PushTargetType.LocalDirectory,
            Path = string.Empty,
            PublicBaseUrl = string.Empty
        });
        SaveSettings();
        Rebuild();
    }

    private void RemovePushTarget(int index)
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || index < 0 || index >= settings.PushTargets.Count)
            return;

        PushTargetConfig target = settings.PushTargets[index];
        if (!EditorUtility.DisplayDialog(
                "Delete Push Target",
                $"Delete Push Target '{target?.Name ?? "(unnamed)"}'? This only removes the configuration; published files are unchanged.",
                "Delete",
                "Cancel"))
        {
            return;
        }

        Undo.RecordObject(settings, "Delete Push Target");
        if (_getCurrentTargetId != null
            && target != null
            && string.Equals(_getCurrentTargetId(), target.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            _setCurrentTargetId(string.Empty);
        }

        settings.PushTargets.RemoveAt(index);
        SaveSettings();
        Rebuild();
    }

    private void SaveTargetName(PushTargetConfig config, string value)
    {
        if (config == null)
            return;

        string name = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || IsDuplicateTargetName(config, name))
        {
            SetFeedback("Name Invalid", "Target Name 必须非空且大小写不敏感唯一。", new Color(0.65f, 0.20f, 0.16f));
            Rebuild();
            return;
        }

        Undo.RecordObject(FYAssetSettings.Instance, "Rename Push Target");
        config.Name = name;
        SaveSettings();
        Rebuild();
    }

    private void SaveTargetUrl(PushTargetConfig config, string value)
    {
        if (config == null)
            return;

        string previous = config.PublicBaseUrl;
        config.PublicBaseUrl = (value ?? string.Empty).Trim();
        if (!config.TryNormalizePublicBaseUrl(out string normalized, out string error))
        {
            config.PublicBaseUrl = previous;
            SetFeedback("URL Invalid", error, new Color(0.65f, 0.20f, 0.16f));
            Rebuild();
            return;
        }

        Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target URL");
        config.PublicBaseUrl = normalized;
        SaveSettings();
        Rebuild();
    }

    private bool IsDuplicateTargetName(PushTargetConfig target, string name)
    {
        List<PushTargetConfig> targets = FYAssetSettings.Instance.PushTargets;
        for (int i = 0; targets != null && i < targets.Count; i++)
        {
            PushTargetConfig other = targets[i];
            if (other != null && !ReferenceEquals(other, target)
                && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string CreateUniqueTargetName(List<PushTargetConfig> targets)
    {
        int suffix = targets.Count + 1;
        while (PushTargetConfig.FindByName("target" + suffix) != null)
            suffix++;
        return "target" + suffix;
    }

    private static void SaveSettings()
    {
        EditorUtility.SetDirty(FYAssetSettings.Instance);
        AssetDatabase.SaveAssets();
    }

    private string ResolvePublishCachePath()
    {
        PushTargetConfig config = GetSelectedTargetConfigOrNull();
        return config == null
            ? null
            : PublishRequest.ResolvePublishCachePath(BuildPathManager.ProjectRoot, _backendKey, config.TargetId);
    }

    private PushTargetConfig GetSelectedTargetConfig()
    {
        if (!TryGetSelectedTargetConfig(out PushTargetConfig config, out string error))
            throw new InvalidOperationException(error);
        return config;
    }

    private PushTargetConfig GetSelectedTargetConfigOrNull()
    {
        return TryGetSelectedTargetConfig(out PushTargetConfig config, out _) ? config : null;
    }

    private bool TryGetSelectedTargetConfig(out PushTargetConfig config, out string error)
    {
        config = null;
        error = "未选择有效 Push Target。";
        if (_targetDropdown == null)
            return false;

        int index = _targetDropdown.index;
        string targetId = index >= 0 && index < _targetIds.Count ? _targetIds[index] : string.Empty;
        return PushTargetConfig.TryResolveById(
            FYAssetSettings.Instance.PushTargets,
            targetId,
            out config,
            out error);
    }

    private List<string> GetPushTargetLabels()
    {
        var labels = new List<string>();
        _targetIds.Clear();
        if (_setCurrentTargetId != null)
        {
            labels.Add("(none)");
            _targetIds.Add(string.Empty);
        }

        List<PushTargetConfig> targets = FYAssetSettings.Instance.PushTargets;
        for (int i = 0; targets != null && i < targets.Count; i++)
        {
            PushTargetConfig config = targets[i];
            if (config == null || string.IsNullOrEmpty(config.TargetId))
                continue;
            labels.Add(string.IsNullOrEmpty(config.Name) ? config.TargetId : config.Name);
            _targetIds.Add(config.TargetId);
        }

        if (labels.Count == 0)
        {
            labels.Add("(none)");
            _targetIds.Add(string.Empty);
        }
        return labels;
    }

    private int ResolveInitialTargetIndex()
    {
        string current = _getCurrentTargetId?.Invoke();
        if (!string.IsNullOrEmpty(current))
        {
            int index = _targetIds.FindIndex(id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                return index;
        }
        return _setCurrentTargetId != null ? 0 : Math.Min(0, _targetIds.Count - 1);
    }

    private void HandleTargetSelectionChanged()
    {
        int index = _targetDropdown?.index ?? -1;
        string targetId = index >= 0 && index < _targetIds.Count ? _targetIds[index] : string.Empty;
        if (_setCurrentTargetId != null)
        {
            Undo.RecordObject(FYAssetSettings.Instance, "Select AB Push Target");
            _setCurrentTargetId(targetId);
            SaveSettings();
        }
        RefreshSourceFacts();
    }

    internal static Label CreateBadge(string text, Color color)
    {
        var badge = new Label(text);
        badge.style.paddingLeft = 8f;
        badge.style.paddingRight = 8f;
        badge.style.paddingTop = 3f;
        badge.style.paddingBottom = 3f;
        badge.style.unityFontStyleAndWeight = FontStyle.Bold;
        badge.style.color = Color.white;
        badge.style.backgroundColor = color;
        badge.style.borderTopLeftRadius = 4f;
        badge.style.borderTopRightRadius = 4f;
        badge.style.borderBottomLeftRadius = 4f;
        badge.style.borderBottomRightRadius = 4f;
        return badge;
    }

    private Label AddStat(VisualElement parent, string title, string value)
    {
        var box = new VisualElement();
        box.style.flexGrow = 1f;
        box.style.minWidth = 0f;
        box.style.marginRight = 6f;
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;

        Label titleLabel = BuildPipelineUI.SmallText(title);
        box.Add(titleLabel);
        var valueLabel = new Label(value);
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.whiteSpace = WhiteSpace.Normal;
        box.Add(valueLabel);
        parent.Add(box);
        return valueLabel;
    }

    private void SetBadge(string text, Color color)
    {
        if (_statusBadge == null)
            return;

        _statusBadge.style.display = DisplayStyle.Flex;
        _statusBadge.text = text;
        _statusBadge.style.backgroundColor = color;
    }

    private void SetFeedback(string badge, string message, Color color)
    {
        SetBadge(badge, color);
        if (_messageLabel != null)
            _messageLabel.text = message;
    }

    internal static void SetCompactFieldLabel(BaseField<string> field, float labelWidth)
    {
        if (field == null)
            return;

        Label label = field.Q<Label>();
        if (label != null)
        {
            label.style.minWidth = labelWidth;
            label.style.width = labelWidth;
            label.style.marginRight = 4f;
            label.style.flexShrink = 0f;
        }

        var input = field.Q(className: "unity-base-field__input");
        if (input != null)
        {
            input.style.minWidth = 0f;
            input.style.flexShrink = 1f;
        }
    }
}
#endif

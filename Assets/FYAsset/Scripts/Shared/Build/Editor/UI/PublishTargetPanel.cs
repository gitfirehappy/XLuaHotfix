#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 发布目标面板：维护 FYAssetSettings.PushTargets、把本地包发布到所选目标、把目标 URL 写入本后端 HotfixUrl。
/// </summary>
/// <remarks>
/// 计划 T7 的发布事实：
/// 1. 待发布目录由面板显式选择某个已构建的正式包目录（来自 Summary 的成功交付列表）；
/// 2. 发布由 BuildPublisher 执行：读取服务器 PackageIndex + Manifest，复用已有 Hash 内容，最后写 PackageIndex；
/// 3. 旧包清理是独立维护动作，永远不删除当前 PackageIndex 指向的包目录。
/// </remarks>
public sealed class PublishTargetPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _backendKey;
    private readonly Action<string> _applyHotfixUrl;
    private readonly IPackageManifestReader _manifestReader;

    private VisualElement _root;
    private Label _statusBadge;
    private Label _messageLabel;
    private Label _sourceLabel;
    private Label _identityLabel;
    private Label _cacheLabel;
    private DropdownField _targetDropdown;
    private DropdownField _sourceDropdown;
    private readonly List<string> _sourcePaths = new List<string>();

    public PublishTargetPanel(string backendKey, Action<string> applyHotfixUrl, IPackageManifestReader manifestReader)
    {
        if (string.IsNullOrEmpty(backendKey))
            throw new ArgumentNullException(nameof(backendKey));
        _backendKey = backendKey;
        _applyHotfixUrl = applyHotfixUrl ?? throw new ArgumentNullException(nameof(applyHotfixUrl));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
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
        var title = BuildPipelineUI.Header(_backendKey);
        title.style.fontSize = 14f;
        titleBox.Add(title);
        titleBox.Add(BuildPipelineUI.SmallText($"Backend: {_backendKey}"));
        top.Add(titleBox);
        top.Add(BuildPipelineUI.ToolbarButton("Refresh", Rebuild, 70f));
        top.Add(BuildPipelineUI.ToolbarButton("Push", RunPush, 60f));
        header.Add(top);

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginTop = 6f;
        _statusBadge = CreateBadge("Idle", new Color(0.42f, 0.42f, 0.42f));
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
        _targetDropdown = new DropdownField("Target", GetPushTargetLabels(), 0);
        _targetDropdown.style.flexGrow = 1f;
        SetCompactFieldLabel(_targetDropdown, 48f);
        row.Add(_targetDropdown);
        Button applyUrl = BuildPipelineUI.ToolbarButton("Apply URL", RunApplyTargetUrl, 78f);
        applyUrl.style.marginLeft = 6f;
        row.Add(applyUrl);
        card.Add(row);

        card.Add(BuildPipelineUI.SmallText(
            "Push 读取服务器 PackageIndex 与 Manifest，复用已有 Hash 内容，最后写入 PackageIndex；"
            + "服务器事实不可用时退化为完整上传。Apply URL 只写入本后端 HotfixUrl。"));
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

        var idField = new TextField("Id")
        {
            value = config != null ? config.Id : string.Empty,
            isDelayed = true
        };
        idField.style.flexGrow = 1f;
        idField.style.minWidth = 120f;
        idField.style.marginRight = 6f;
        SetCompactFieldLabel(idField, 22f);
        idField.RegisterValueChangedCallback(evt =>
        {
            if (config == null)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target");
            config.Id = (evt.newValue ?? string.Empty).Trim();
            SaveSettings();
            Rebuild();
        });
        row.Add(idField);

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

        Button remove = BuildPipelineUI.ToolbarButton("Remove", () => RemovePushTarget(index), 64f);
        remove.style.marginLeft = 6f;
        row.Add(remove);
        container.Add(row);
        container.Add(typeField);

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
        urlField.RegisterValueChangedCallback(evt =>
        {
            if (config == null)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target URL");
            config.PublicBaseUrl = (evt.newValue ?? string.Empty).Trim();
            SaveSettings();
            Rebuild();
        });
        container.Add(urlField);

        if (config != null)
        {
            string note = config.Type == PushTargetType.CloudflarePages
                ? "Cloudflare Pages 由 Compat 部署胶水处理；面板 Push 只支持目录型目标。"
                : $"发布目录：(所选 Path，空为 OutputRoot)/{_backendKey}";
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

        string previous = ResolveSelectedSource();
        _sourcePaths.Clear();
        var labels = new List<string>();

        string[] packageDirs = FileHelper.GetDirectories(BuildPathManager.PackagesDir, "Build_*");
        Array.Sort(packageDirs, StringComparer.Ordinal);
        for (int i = 0; i < packageDirs.Length; i++)
        {
            _sourcePaths.Add(packageDirs[i]);
            labels.Add(Path.GetFileName(packageDirs[i]));
        }

        if (labels.Count == 0)
            labels.Add("(none)");

        _sourceDropdown.choices = labels;
        int index = previous != null ? _sourcePaths.IndexOf(previous) : -1;
        _sourceDropdown.SetValueWithoutNotify(labels[index >= 0 ? index : 0]);
    }

    /// <summary>发布源身份只来自正式 Summary：包目录不承载构建事实。</summary>
    private string DescribeSelectedIdentity(string sourceDir)
    {
        string packageName = string.IsNullOrEmpty(sourceDir) ? null : Path.GetFileName(sourceDir);
        if (string.IsNullOrEmpty(packageName))
            return "未选择发布源。";

        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        if (!store.TryReadSummaryDocument(_backendKey, packageName,
                out CompleteBuildSummary.SummaryDocument document, out string error))
            return $"正式 Summary 中缺少包身份: {error}";

        return string.IsNullOrEmpty(document.BuildType)
            ? $"{document.BuildId} | {document.Version}"
            : $"{document.BuildId} | {document.Version} | {document.BuildType}";
    }

    private void RefreshSourceFacts()
    {
        string sourceDir = ResolveSelectedSource();
        if (_sourceLabel != null)
            _sourceLabel.text = string.IsNullOrEmpty(sourceDir) ? "-" : sourceDir;

        if (_identityLabel != null)
        {
            _identityLabel.text = DescribeSelectedIdentity(sourceDir);
        }

        if (_cacheLabel != null)
        {
            string cachePath = ResolvePublishCachePath();
            _cacheLabel.text = string.IsNullOrEmpty(cachePath)
                ? "-"
                : (PublishCacheStore.Exists(cachePath) ? cachePath : "无（不影响发布正确性）");
        }
    }

    private string ResolveSelectedSource()
    {
        if (_sourceDropdown == null || _sourcePaths.Count == 0)
            return null;

        int index = _sourceDropdown.index;
        return index >= 0 && index < _sourcePaths.Count ? _sourcePaths[index] : null;
    }

    private void RunPush()
    {
        try
        {
            PushTargetConfig config = GetSelectedTargetConfig();
            if (config.Type != PushTargetType.LocalDirectory)
                throw new InvalidOperationException(
                    $"面板 Push 只支持目录型目标；'{config.Type}' 请使用 Compat 部署入口。");

            string sourceDir = ResolveSelectedSource();
            if (string.IsNullOrEmpty(sourceDir))
                throw new InvalidOperationException("没有可发布的本地包目录。请先完成一次构建。");

            // 发布身份只来自正式 Summary：包目录不承载身份，面板不得从目录名或包内文件推断。
            string packageName = Path.GetFileName(
                sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!BuildSummaryStore.CreateDefault().TryReadSummaryDocument(
                    _backendKey, packageName, out CompleteBuildSummary.SummaryDocument document, out string identityError))
            {
                throw new InvalidOperationException("正式 Summary 中缺少包身份: " + identityError);
            }

            if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
                throw new InvalidOperationException("正式 Summary 版本无法解析: " + document.Version);

            var request = new PublishRequest
            {
                BackendKey = _backendKey,
                SourcePackageDir = sourceDir,
                TargetId = config.Id,
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
        int index = settings.PushTargets.Count + 1;
        settings.PushTargets.Add(new PushTargetConfig
        {
            Id = "target" + index,
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

        Undo.RecordObject(settings, "Remove Push Target");
        settings.PushTargets.RemoveAt(index);
        SaveSettings();
        Rebuild();
    }

    private static void SaveSettings()
    {
        EditorUtility.SetDirty(FYAssetSettings.Instance);
        AssetDatabase.SaveAssets();
    }

    /// <summary>发布缓存的独立存储路径（后端 + 目标隔离）；目标缺失时返回 null。</summary>
    private string ResolvePublishCachePath()
    {
        PushTargetConfig config = GetSelectedTargetConfigOrNull();
        return config == null
            ? null
            : PublishRequest.ResolvePublishCachePath(BuildPathManager.ProjectRoot, _backendKey, config.Id);
    }

    private PushTargetConfig GetSelectedTargetConfig()
    {
        PushTargetConfig config = GetSelectedTargetConfigOrNull();
        if (config == null)
            throw new InvalidOperationException("未配置 Push Target。");
        return config;
    }

    private PushTargetConfig GetSelectedTargetConfigOrNull()
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        string targetId = _targetDropdown != null && !string.IsNullOrEmpty(_targetDropdown.value)
            ? _targetDropdown.value
            : (settings.PushTargets != null && settings.PushTargets.Count > 0 ? settings.PushTargets[0].Id : string.Empty);
        return PushTargetConfig.FindById(targetId);
    }

    private static List<string> GetPushTargetLabels()
    {
        var labels = new List<string>();
        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets != null)
        {
            for (int i = 0; i < settings.PushTargets.Count; i++)
            {
                PushTargetConfig config = settings.PushTargets[i];
                if (config != null && !string.IsNullOrEmpty(config.Id))
                    labels.Add(config.Id);
            }
        }
        if (labels.Count == 0)
            labels.Add("(none)");
        return labels;
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

        _statusBadge.text = text;
        _statusBadge.style.backgroundColor = color;
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

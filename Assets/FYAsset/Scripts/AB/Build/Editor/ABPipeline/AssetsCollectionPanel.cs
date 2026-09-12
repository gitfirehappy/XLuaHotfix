using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Project Scan 预览 Collector；Curate 持有候选编辑，Save/Cancel 提交或丢弃。
/// </summary>
/// <remarks>
/// 配置层级只有 Setting -> Group -> Collector。Setting 一级字段包括 AddressStyle、IgnorePatterns、
/// ExcludedAssets、RawFileRules 与 SharePolicy；资产级只保留 Address / Labels 人工覆盖（AssetOverrides）。
/// </remarks>
public class AssetsCollectionPanel : IBuildPipelinePanel
{
    private enum WorkflowStage
    {
        Scan,
        Preview,
        Curate
    }

    private enum SelectionType
    {
        None,
        Group,
        Asset
    }

    private enum CuratePanelMode
    {
        Details,
        ScanPreview,
        Settings,
        Overrides
    }

    private const float MinSidebarWidth = 220f;
    private const float MaxSidebarWidth = 560f;
    private const float SidebarCharWidth = 7.5f;
    private const float SidebarPaddingWidth = 76f;

    private EditorWindow _window;
    private AssetCollectionSetting _setting;
    private VisualElement _root;
    private WorkflowStage _stage;
    private ProjectScanSnapshot _projectSnapshot;
    private AssetCollectionSetting _curateSetting;
    private ScanResult _curateResult;
    private bool _curatePreviewDirty;
    private bool _curateHasUnsavedChanges;
    private CuratePanelMode _curatePanelMode = CuratePanelMode.Details;
    private SelectionType _selectionType = SelectionType.None;
    private int _selectedGroupIndex = -1;
    private string _selectedAssetGuid;
    private float _curateSidebarWidth = 250f;
    private VisualElement _curateSidebar;
    private ScrollView _curateSidebarTree;
    private Vector2 _curateSidebarScrollOffset;
    private readonly HashSet<string> _collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _draggingCurateSplitter;
    private bool _suppressExternalCollectorChanged;
    private Vector2 _splitterDragStartMouse;
    private float _splitterDragStartWidth;
    private List<BuildMessage> _validationMessages = new List<BuildMessage>();

    public string PanelName => "Collection";
    public bool HasUnsavedChanges => _stage == WorkflowStage.Curate && _curateHasUnsavedChanges;

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        CollectorMutationUtility.Changed -= OnExternalCollectorChanged;
        CollectorMutationUtility.Changed += OnExternalCollectorChanged;
        LoadSetting();
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement
        {
            style =
            {
                flexGrow = 1f,
                flexDirection = FlexDirection.Column
            }
        };
        Rebuild();
        return _root;
    }

    public void OnDisable()
    {
        CollectorMutationUtility.Changed -= OnExternalCollectorChanged;
        _root?.Unbind();
        _root = null;
    }

    private void LoadSetting(bool preserveExpansionState = false)
    {
        _setting = CollectorMutationUtility.LoadSetting();

        if (_setting == null)
            return;

        EnsureScanDefaults(_setting);
        if (HasGroups(_setting))
        {
            AssetCollectionSetting candidate = CloneSetting(_setting);
            EnterCurate(candidate, false, CollectionScanner.Scan(candidate, CollectionScanOptions.FromSetting(candidate)), !preserveExpansionState);
        }
        else
            EnterScan();
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        CaptureCurateSidebarScrollOffset();
        _root.Clear();
        _root.Unbind();
        _curateSidebar = null;
        _curateSidebarTree = null;

        if (_setting == null)
        {
            DrawNoSetting();
            return;
        }

        DrawToolbar();
        switch (_stage)
        {
            case WorkflowStage.Preview:
                DrawPreviewStage();
                break;
            case WorkflowStage.Curate:
                DrawCurateStage();
                break;
            default:
                DrawScanStage();
                break;
        }
    }

    private void EnterScan()
    {
        _stage = WorkflowStage.Scan;
        _projectSnapshot = null;
        _curateSetting = null;
        _curateResult = null;
        _curatePreviewDirty = false;
        _curateHasUnsavedChanges = false;
        _validationMessages = new List<BuildMessage>();
        _curatePanelMode = CuratePanelMode.Details;
        ResetCurateSidebarScroll();
        ClearSelection();
    }

    private void EnterPreview(ProjectScanSnapshot snapshot)
    {
        _stage = WorkflowStage.Preview;
        _projectSnapshot = snapshot;
        _curateSetting = null;
        _curateResult = null;
        _curatePreviewDirty = false;
        _curateHasUnsavedChanges = false;
        _validationMessages = new List<BuildMessage>();
        _curatePanelMode = CuratePanelMode.Details;
        ResetCurateSidebarScroll();
        ClearSelection();
    }

    private void EnterCurate(
        AssetCollectionSetting candidate,
        bool selectFirst,
        ScanResult initialResult = null,
        bool initializeExpansionState = true)
    {
        bool normalizedSceneCollectors = NormalizeSceneCollectors(candidate);
        if (normalizedSceneCollectors && initialResult != null)
            initialResult = CollectionScanner.Scan(candidate, CollectionScanOptions.FromSetting(candidate));

        _stage = WorkflowStage.Curate;
        _curateSetting = candidate;
        _curateResult = initialResult;
        _curatePreviewDirty = initialResult == null;
        _curateHasUnsavedChanges = normalizedSceneCollectors;
        _validationMessages = new List<BuildMessage>();
        _curatePanelMode = CuratePanelMode.Details;
        if (initializeExpansionState)
            ResetCurateSidebarScroll();
        EnsureSelection(selectFirst);
    }

    private void OnExternalCollectorChanged()
    {
        if (_root == null)
            return;
        if (_suppressExternalCollectorChanged)
            return;

        if (_stage == WorkflowStage.Curate && _curateSetting != null && _curateHasUnsavedChanges)
        {
            RescanCurate(false, _curatePanelMode, false);
            return;
        }

        LoadSetting(true);
        Rebuild();
    }

    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        Button scan = BuildPipelineUI.ToolbarButton("Scan", ShowScanStage, 72f);
        scan.SetEnabled(_stage != WorkflowStage.Scan);
        toolbar.Add(scan);

        Button curate = BuildPipelineUI.ToolbarButton("Curate", EnterCurateFromToolbar, 82f);
        curate.SetEnabled(_stage != WorkflowStage.Curate);
        toolbar.Add(curate);

        if (_stage == WorkflowStage.Curate)
        {
            Button save = BuildPipelineUI.ToolbarButton("Save", SaveCollectors, 64f);
            save.SetEnabled(CanSaveCollectors());
            toolbar.Add(save);
            toolbar.Add(BuildPipelineUI.ToolbarButton("Validate", ValidateCurate, 76f));
            toolbar.Add(BuildPipelineUI.ToolbarButton("Cancel", CancelCurate, 72f));
        }

        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(GetStageHint()));
        _root.Add(toolbar);
    }

    private string GetStageHint()
    {
        switch (_stage)
        {
            case WorkflowStage.Preview:
                return "Read-only preview";
            case WorkflowStage.Curate:
                return _curatePreviewDirty
                    ? "Preview outdated"
                    : _curateHasUnsavedChanges ? "Unsaved changes" : "Saved";
            default:
                return "Scan";
        }
    }

    private void EnterCurateFromToolbar()
    {
        if (HasGroups(_setting))
        {
            AssetCollectionSetting candidate = CloneSetting(_setting);
            EnterCurate(candidate, false, CollectionScanner.Scan(candidate, CollectionScanOptions.FromSetting(candidate)));
        }
        else
            EnterCurate(ScriptableObject.CreateInstance<AssetCollectionSetting>(), false);

        Rebuild();
    }

    private void ShowScanStage()
    {
        if (HasUnsavedChanges && !EditorUtility.DisplayDialog("Discard Changes?",
                "切换到 Scan 会丢弃当前 Collection candidate，包括未保存的 Asset Overrides。", "Discard", "Cancel"))
            return;
        EnterScan();
        Rebuild();
    }

    private void DrawScanStage()
    {
        ScrollView scroll = CreateScroll();
        scroll.Add(CreateSettingEditor(_setting, true));
        scroll.Add(CreateScanActionRow(false));
        _root.Add(scroll);
    }

    private void RunProjectScan()
    {
        AssetCollectionSetting preview = BuildProjectScanSetting();
        var snapshot = new ProjectScanSnapshot
        {
            PreviewSetting = preview,
            Result = CollectionScanner.Scan(preview, CollectionScanOptions.FromSetting(preview))
        };
        EnterPreview(snapshot);
        Rebuild();
    }

    private AssetCollectionSetting BuildProjectScanSetting()
    {
        var setting = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        EnsureScanDefaults(_setting);
        setting.AddressStyle = _setting != null ? _setting.AddressStyle : AssetAddressStyle.ShortName;
        setting.IgnorePatterns = CloneList(_setting?.IgnorePatterns);
        setting.ExcludedAssets = CloneExcludedAssets(_setting?.ExcludedAssets);
        setting.RawFileRules = CloneRawFileRules(_setting?.RawFileRules);
        setting.SharePolicy = CloneSharePolicy(_setting?.SharePolicy);

        string[] folders = AssetDatabase.GetSubFolders("Assets");
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < folders.Length; i++)
        {
            string folder = CollectorPathUtility.NormalizePath(folders[i]);
            if (HasNonSceneCollectableAssets(folder, setting))
                AddProjectScanGroup(setting, folder);
        }

        AddScatteredSceneGroups(setting);
        NormalizeSceneCollectors(setting);
        return setting;
    }

    private void DrawPreviewStage()
    {
        ScrollView scroll = CreateScroll();
        if (_projectSnapshot?.PreviewSetting == null)
        {
            scroll.Add(BuildPipelineUI.SmallText("No scan preview. Run Scan first."));
            _root.Add(scroll);
            return;
        }

        scroll.Add(CreateSettingEditor(_setting, true));
        scroll.Add(CreateScanActionRow(true));
        RenderPreviewSummary(scroll);
        RenderPreviewTree(scroll, _projectSnapshot.PreviewSetting, _projectSnapshot.Result);
        RenderMessages(scroll, _projectSnapshot.Result);
        _root.Add(scroll);
    }

    private VisualElement CreateScanActionRow(bool canConfirm)
    {
        VisualElement row = BuildPipelineUI.Toolbar();
        row.style.marginTop = 8f;
        row.style.marginBottom = 8f;
        Button scan = BuildPipelineUI.ToolbarButton("Project Scan", RunProjectScan, 120f);
        row.Add(scan);
        Button confirm = BuildPipelineUI.ToolbarButton("Confirm To Curate", ConfirmPreview, 140f);
        confirm.SetEnabled(canConfirm);
        row.Add(confirm);
        return row;
    }

    private void RenderPreviewSummary(VisualElement parent)
    {
        int groupCount = _projectSnapshot.PreviewSetting.Groups?.Count ?? 0;
        int assetCount = _projectSnapshot.Result?.Assets?.Count ?? 0;
        int bundleCount = CountDistinctBundles(_projectSnapshot.Result?.Assets);
        int warningCount = CountMessages(_projectSnapshot.Result, BuildSeverity.Warning);
        int errorCount = CountMessages(_projectSnapshot.Result, BuildSeverity.Error);

        parent.Add(BuildPipelineUI.Header("Scan Preview"));
        parent.Add(CreateMetricStrip(groupCount, assetCount, bundleCount, warningCount, errorCount));
    }

    private void ConfirmPreview()
    {
        if (_projectSnapshot?.PreviewSetting == null)
            return;

        EnterCurate(CloneSetting(_projectSnapshot.PreviewSetting), true, CloneScanResult(_projectSnapshot.Result));
        _curateHasUnsavedChanges = true;
        Rebuild();
    }

    private void DrawCurateStage()
    {
        VisualElement main = new VisualElement
        {
            style =
            {
                flexGrow = 1f,
                flexDirection = FlexDirection.Row,
                minHeight = 0f
            }
        };
        _root.Add(main);

        VisualElement sidebar = BuildCurateSidebar();
        main.Add(sidebar);
        VisualElement splitter = BuildPipelineUI.Splitter(true);
        splitter.RegisterCallback<PointerDownEvent>(OnCurateSplitterDown);
        splitter.RegisterCallback<PointerMoveEvent>(OnCurateSplitterMove);
        splitter.RegisterCallback<PointerUpEvent>(OnCurateSplitterUp);
        main.Add(splitter);

        VisualElement detail = new VisualElement
        {
            style =
            {
                flexGrow = 1f,
                minWidth = 0f
            }
        };
        main.Add(detail);

        ScrollView scroll = CreateScroll();
        detail.Add(scroll);

        if (_curateSetting == null)
        {
            scroll.Add(BuildPipelineUI.SmallText("No Curate candidate. Run Scan or reload the saved AssetCollectionSetting."));
            return;
        }

        DrawCurateModeToolbar(scroll);
        RenderValidationMessages(scroll);
        switch (_curatePanelMode)
        {
            case CuratePanelMode.ScanPreview:
                DrawCuratePreview(scroll);
                break;
            case CuratePanelMode.Settings:
                scroll.Add(CreateSettingEditor(_curateSetting, false));
                break;
            case CuratePanelMode.Overrides:
                DrawAssetOverridesEditor(scroll);
                break;
            default:
                DrawCurateDetails(scroll);
                break;
        }
    }

    private void DrawCurateModeToolbar(VisualElement parent)
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(CreateModeButton("Details", CuratePanelMode.Details, 88f));
        toolbar.Add(CreateModeButton("Scan Preview", CuratePanelMode.ScanPreview, 112f));
        toolbar.Add(CreateModeButton("Settings", CuratePanelMode.Settings, 82f));
        toolbar.Add(CreateModeButton("Asset Overrides", CuratePanelMode.Overrides, 120f));
        parent.Add(toolbar);
    }

    private Button CreateModeButton(string text, CuratePanelMode mode, float width)
    {
        Button button = BuildPipelineUI.ToolbarButton(text, () =>
        {
            _curatePanelMode = mode;
            Rebuild();
        }, width);
        button.SetEnabled(_curatePanelMode != mode);
        return button;
    }

    private void RenderValidationMessages(VisualElement parent)
    {
        if (_validationMessages == null || _validationMessages.Count == 0)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Validation"));
        for (int i = 0; i < _validationMessages.Count; i++)
        {
            BuildMessage message = _validationMessages[i];
            Label label = BuildPipelineUI.SmallText($"{message.Severity}  {message.Code}  {message.Message}");
            if (message.Severity == BuildSeverity.Error)
                label.style.color = new Color(1f, 0.42f, 0.35f);
            card.Add(label);
        }
        parent.Add(card);
    }

    private void OnCurateSplitterDown(PointerDownEvent evt)
    {
        _draggingCurateSplitter = true;
        _splitterDragStartMouse = evt.position;
        _splitterDragStartWidth = _curateSidebarWidth;
        ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnCurateSplitterMove(PointerMoveEvent evt)
    {
        if (!_draggingCurateSplitter)
            return;

        float delta = evt.position.x - _splitterDragStartMouse.x;
        _curateSidebarWidth = Mathf.Clamp(_splitterDragStartWidth + delta, MinSidebarWidth, MaxSidebarWidth);
        if (_curateSidebar != null)
            _curateSidebar.style.width = _curateSidebarWidth;
        evt.StopPropagation();
    }

    private void OnCurateSplitterUp(PointerUpEvent evt)
    {
        if (!_draggingCurateSplitter)
            return;

        _draggingCurateSplitter = false;
        ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private VisualElement BuildCurateSidebar()
    {
        FitCurateSidebarWidthToContent();
        VisualElement sidebar = new VisualElement
        {
            style =
            {
                width = _curateSidebarWidth,
                flexShrink = 0f,
                backgroundColor = BuildPipelineUI.SidebarBackgroundColor,
                paddingLeft = 6f,
                paddingRight = 6f,
                paddingTop = 6f,
                minHeight = 0f
            }
        };
        _curateSidebar = sidebar;

        VisualElement buttons = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                marginBottom = 6f
            }
        };
        buttons.Add(new Button(AddGroup) { text = "+ Group" });
        Button addAsset = new Button(AddAssetToSelectedGroup) { text = "+ Asset" };
        addAsset.SetEnabled(GetSelectedGroup() != null);
        buttons.Add(addAsset);
        Button delete = new Button(DeleteSelection) { text = "Delete" };
        delete.SetEnabled(_selectionType == SelectionType.Group || _selectionType == SelectionType.Asset);
        buttons.Add(delete);
        sidebar.Add(buttons);

        if (_curateSetting.Groups == null || _curateSetting.Groups.Count == 0)
        {
            sidebar.Add(BuildPipelineUI.SmallText("No Group. Add one or run Scan."));
            return sidebar;
        }

        ScrollView tree = new ScrollView(ScrollViewMode.Vertical)
        {
            style =
            {
                flexGrow = 1f,
                minHeight = 0f
            }
        };
        _curateSidebarTree = tree;
        RestoreCurateSidebarScrollOffset(tree);
        sidebar.Add(tree);

        for (int gi = 0; gi < _curateSetting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = _curateSetting.Groups[gi];
            string suffix = group != null && !group.Enabled ? " [Disabled]" : string.Empty;
            string groupKey = GetGroupNavKey(gi, group);
            bool groupExpanded = !_collapsedGroups.Contains(groupKey);
            Label groupLabel = CreateNavDisclosureLabel(GetGroupDisplayName(group) + suffix, groupExpanded, IsSelectedGroup(gi), 0f, 20f);
            int groupIndex = gi;
            groupLabel.RegisterCallback<PointerDownEvent>(evt =>
            {
                ToggleCollapsed(_collapsedGroups, groupKey);
                SelectGroup(groupIndex);
                _curatePanelMode = CuratePanelMode.Details;
                Rebuild();
                evt.StopPropagation();
            });
            tree.Add(groupLabel);

            if (!groupExpanded)
                continue;

            List<CollectedAssetInfo> assets = GetAssetsForGroup(_curateResult, group?.GroupName);
            assets.Sort((left, right) => string.Compare(GetAssetNavName(left), GetAssetNavName(right), StringComparison.OrdinalIgnoreCase));
            for (int ai = 0; ai < assets.Count; ai++)
            {
                CollectedAssetInfo asset = assets[ai];
                Label assetLabel = CreateNavLabel(GetAssetNavName(asset), IsSelectedAsset(asset.AssetGUID), 18f);
                assetLabel.style.marginLeft = 32f;
                string assetGuid = asset.AssetGUID;
                assetLabel.RegisterCallback<PointerDownEvent>(evt =>
                {
                    SelectAsset(groupIndex, assetGuid);
                    _curatePanelMode = CuratePanelMode.Details;
                    Rebuild();
                    evt.StopPropagation();
                });
                tree.Add(assetLabel);
            }
        }

        return sidebar;
    }

    private void CaptureCurateSidebarScrollOffset()
    {
        if (_curateSidebarTree == null)
            return;

        _curateSidebarScrollOffset = _curateSidebarTree.scrollOffset;
    }

    private void RestoreCurateSidebarScrollOffset(ScrollView tree)
    {
        if (tree == null)
            return;

        Vector2 offset = _curateSidebarScrollOffset;
        tree.scrollOffset = offset;
        tree.schedule.Execute(() =>
        {
            if (_curateSidebarTree == tree)
                tree.scrollOffset = offset;
        });
    }

    private void ResetCurateSidebarScroll()
    {
        _curateSidebarScrollOffset = Vector2.zero;
        _curateSidebarTree = null;
    }

    private void DrawCurateDetails(VisualElement parent)
    {
        if (!string.IsNullOrEmpty(_selectedAssetGuid))
        {
            DrawAssetEditor(parent, _selectedAssetGuid);
            return;
        }

        switch (_selectionType)
        {
            case SelectionType.Group:
                DrawGroupEditor(parent);
                break;
            default:
                parent.Add(BuildPipelineUI.SmallText("Select a Group or Asset to edit Curate data."));
                break;
        }
    }

    private void DrawCuratePreview(VisualElement parent)
    {
        VisualElement card = BuildPipelineUI.Card();
        VisualElement header = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center
            }
        };
        header.Add(BuildPipelineUI.Header("Curate Preview"));
        header.Add(BuildPipelineUI.Spacer());
        Button previewButton = new Button(RefreshCuratePreview)
        {
            text = _curateResult == null ? "Preview" : "Refresh Preview"
        };
        previewButton.style.width = 120f;
        header.Add(previewButton);
        card.Add(header);

        if (_curatePreviewDirty)
            card.Add(BuildPipelineUI.SmallText("Preview outdated"));
        else
            card.Add(BuildPipelineUI.SmallText("Preview is current."));

        if (_curateResult == null)
        {
            card.Add(BuildPipelineUI.SmallText("No preview"));
            parent.Add(card);
            return;
        }

        parent.Add(card);
        RenderPreviewTree(parent, _curateSetting, _curateResult);
        RenderMessages(parent, _curateResult);
    }

    private void DrawGroupEditor(VisualElement parent)
    {
        AssetCollectionGroup group = GetSelectedGroup();
        if (group == null)
        {
            parent.Add(BuildPipelineUI.SmallText("Selected Group is missing."));
            return;
        }

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Group"));
        card.Add(CreateTextField("Group Name", group.GroupName, value =>
        {
            group.GroupName = value;
            MarkCuratePreviewDirty();
        }));

        Toggle enabled = new Toggle("Enabled") { value = group.Enabled };
        enabled.RegisterValueChangedCallback(evt =>
        {
            group.Enabled = evt.newValue;
            MarkCuratePreviewDirty();
        });
        card.Add(enabled);

        EnumField packingMode = new EnumField("Bundle Packing", group.BundlePackingMode);
        packingMode.RegisterValueChangedCallback(evt =>
        {
            group.BundlePackingMode = (BundlePackingMode)evt.newValue;
            MarkCuratePreviewDirty();
        });
        card.Add(packingMode);
        AddAddressOperationButtons(card, "Apply Auto Address", style => ApplyAddressStyleToGroup(group.GroupName, style));
        parent.Add(card);

        DrawCollectorsEditor(parent, group);
    }

    private void DrawCollectorsEditor(VisualElement parent, AssetCollectionGroup group)
    {
        group.Collectors ??= new List<Collector>();

        VisualElement card = BuildPipelineUI.Card();
        VisualElement header = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center
            }
        };
        header.Add(BuildPipelineUI.Header("Collectors"));
        header.Add(BuildPipelineUI.Spacer());
        header.Add(new Button(() =>
        {
            group.Collectors.Add(CreateEmptyCollector(ECollectPathType.Folder));
            MarkCuratePreviewDirty();
        }) { text = "+ Folder" });
        header.Add(new Button(() =>
        {
            group.Collectors.Add(CreateEmptyCollector(ECollectPathType.File));
            MarkCuratePreviewDirty();
        }) { text = "+ File" });
        card.Add(header);

        if (group.Collectors.Count == 0)
        {
            card.Add(BuildPipelineUI.SmallText("No Collector in this Group."));
            parent.Add(card);
            return;
        }

        for (int i = 0; i < group.Collectors.Count; i++)
            card.Add(CreateCollectorEditorRow(group, group.Collectors[i], i));

        parent.Add(card);
    }

    private VisualElement CreateCollectorEditorRow(AssetCollectionGroup group, Collector collector, int index)
    {
        VisualElement row = BuildPipelineUI.Card();
        row.style.marginLeft = 0f;
        row.style.marginRight = 0f;

        VisualElement top = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f
            }
        };

        EnumField pathType = new EnumField(collector.CollectPathType);
        pathType.style.width = 88f;
        pathType.style.flexShrink = 0f;
        pathType.RegisterValueChangedCallback(evt =>
        {
            collector.CollectPathType = (ECollectPathType)evt.newValue;
            MarkCuratePreviewDirty();
        });
        top.Add(pathType);

        TextField path = new TextField { value = collector.CollectPath, isDelayed = true };
        path.style.width = 0f;
        path.style.flexGrow = 1f;
        path.style.flexShrink = 1f;
        path.style.flexBasis = 0f;
        path.style.minWidth = 0f;
        path.style.marginRight = 4f;
        path.RegisterValueChangedCallback(evt =>
        {
            collector.CollectPath = CollectorPathUtility.NormalizePath(evt.newValue);
            MarkCuratePreviewDirty();
        });
        top.Add(path);

        Button pickButton = new Button(() =>
        {
            string picked = PickCollectPath(collector.CollectPathType == ECollectPathType.File);
            if (string.IsNullOrEmpty(picked))
                return;
            collector.CollectPath = picked;
            MarkCuratePreviewDirty();
            Rebuild();
        }) { text = "..." };
        pickButton.style.width = 34f;
        pickButton.style.flexShrink = 0f;
        top.Add(pickButton);

        Button removeButton = new Button(() =>
        {
            group.Collectors.RemoveAt(index);
            MarkCuratePreviewDirty();
        }) { text = "x" };
        removeButton.style.width = 28f;
        removeButton.style.flexShrink = 0f;
        top.Add(removeButton);
        row.Add(top);
        return row;
    }

    private void MarkCuratePreviewDirty()
    {
        if (_curateSetting == null)
            return;

        RescanCurate(false, _curatePanelMode);
    }

    private void RefreshCuratePreview()
    {
        if (_curateSetting == null)
            return;

        RescanCurate(false, CuratePanelMode.ScanPreview, false);
    }

    private void RescanCurate(bool selectFirst, CuratePanelMode mode, bool markUnsaved = true)
    {
        if (_curateSetting == null)
            return;

        _curateResult = CollectionScanner.Scan(_curateSetting, CollectionScanOptions.FromSetting(_curateSetting));
        _curatePreviewDirty = false;
        _curatePanelMode = mode;
        if (markUnsaved)
            _curateHasUnsavedChanges = true;
        EnsureSelection(selectFirst);
        Rebuild();
    }

    private void ValidateCurate()
    {
        if (_curateSetting == null)
            return;

        _validationMessages = AssetCollectionSettingValidator.Validate(_curateSetting);
        Rebuild();
    }

    private void SaveCollectors()
    {
        if (_curateSetting == null)
            return;

        if (NormalizeSceneCollectors(_curateSetting))
            _curatePreviewDirty = true;

        if (_curatePreviewDirty || _curateResult == null)
        {
            _curateResult = CollectionScanner.Scan(_curateSetting, CollectionScanOptions.FromSetting(_curateSetting));
            _curatePreviewDirty = false;
        }

        // 保存前强制校验：Error 阻断写入，避免把非法配置落到磁盘资产上。
        _validationMessages = AssetCollectionSettingValidator.Validate(_curateSetting);
        if (HasValidationError())
        {
            _curatePanelMode = CuratePanelMode.Details;
            Rebuild();
            return;
        }

        Undo.RecordObject(_setting, "Save Collectors");
        EnsureScanDefaults(_curateSetting);
        _setting.AddressStyle = _curateSetting.AddressStyle;
        _setting.IgnorePatterns = CloneList(_curateSetting.IgnorePatterns);
        _setting.ExcludedAssets = CloneExcludedAssets(_curateSetting.ExcludedAssets);
        _setting.Groups = CloneGroups(_curateSetting.Groups);
        _setting.AssetOverrides = CloneAssetOverrides(_curateSetting.AssetOverrides);
        _setting.RawFileRules = CloneRawFileRules(_curateSetting.RawFileRules);
        _setting.SharePolicy = CloneSharePolicy(_curateSetting.SharePolicy);
        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
        AssetDatabase.ForceReserializeAssets(new List<string> { FYAssetABSettings.Instance.AssetCollectionSettingPath });
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        _curateHasUnsavedChanges = false;
        LoadSetting(true);
        Rebuild();
    }

    private bool HasValidationError()
    {
        if (_validationMessages == null)
            return false;

        for (int i = 0; i < _validationMessages.Count; i++)
        {
            if (_validationMessages[i].Severity == BuildSeverity.Error)
                return true;
        }

        return false;
    }

    private bool CanSaveCollectors()
    {
        return _stage == WorkflowStage.Curate &&
               _curateSetting != null &&
               HasGroups(_curateSetting);
    }

    private void CancelCurate()
    {
        if (HasGroups(_setting))
            EnterCurate(CloneSetting(_setting), false);
        else
            EnterScan();
        Rebuild();
    }

    private void RenderPreviewTree(VisualElement parent, AssetCollectionSetting setting, ScanResult result)
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Collected Tree"));

        if (setting?.Groups == null || setting.Groups.Count == 0)
        {
            card.Add(BuildPipelineUI.SmallText("No Group."));
            parent.Add(card);
            return;
        }

        for (int gi = 0; gi < setting.Groups.Count; gi++)
            card.Add(CreateGroupPreview(setting.Groups[gi], result));

        parent.Add(card);
    }

    private VisualElement CreateGroupPreview(AssetCollectionGroup group, ScanResult result)
    {
        int assetCount = CountAssetsForGroup(result, group?.GroupName);
        int bundleCount = CountBundlesForGroup(result, group?.GroupName);
        string disabled = group != null && !group.Enabled ? " [Disabled]" : string.Empty;
        Foldout foldout = new Foldout
        {
            text = $"{GetGroupDisplayName(group)}{disabled}  {assetCount} assets / {bundleCount} bundles",
            value = assetCount > 0
        };

        if (group?.Collectors != null)
        {
            for (int ci = 0; ci < group.Collectors.Count; ci++)
                foldout.Add(CreateCollectorPreview(group, group.Collectors[ci], result));
        }

        return foldout;
    }

    private VisualElement CreateCollectorPreview(AssetCollectionGroup group, Collector collector, ScanResult result)
    {
        List<CollectedAssetInfo> assets = GetAssetsForCollector(result, group?.GroupName, collector?.CollectPath);
        Foldout foldout = new Foldout
        {
            text = $"{(collector?.CollectPathType == ECollectPathType.File ? "[File]" : "[Folder]")} {collector?.CollectPath}  ({assets.Count})",
            value = assets.Count > 0 && assets.Count <= 80
        };

        Dictionary<string, List<CollectedAssetInfo>> bundles = BucketByBundle(assets);
        List<string> bundleNames = new List<string>(bundles.Keys);
        bundleNames.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bundleNames.Count; i++)
            foldout.Add(CreateBundlePreview(bundleNames[i], bundles[bundleNames[i]], i));

        return foldout;
    }

    private VisualElement CreateBundlePreview(string bundleName, List<CollectedAssetInfo> assets, int index)
    {
        Foldout foldout = new Foldout
        {
            text = $"{bundleName}  ({assets.Count})",
            value = assets.Count <= 24
        };
        foldout.style.borderLeftWidth = 4f;
        foldout.style.borderLeftColor = GetBundleColor(index);
        foldout.style.marginLeft = 12f;
        foldout.style.paddingLeft = 6f;

        assets.Sort((left, right) => string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < assets.Count; i++)
            foldout.Add(CreateAssetRow(assets[i]));

        return foldout;
    }

    private VisualElement CreateAssetRow(CollectedAssetInfo asset)
    {
        VisualElement row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                marginLeft = 10f,
                marginBottom = 3f
            }
        };
        if (string.Equals(_selectedAssetGuid, asset.AssetGUID, StringComparison.Ordinal))
            row.style.backgroundColor = new Color(0.17f, 0.36f, 0.53f, 0.18f);

        Label path = BuildPipelineUI.SmallText(asset.AssetPath);
        path.style.unityFontStyleAndWeight = FontStyle.Bold;
        if (asset.HasError)
            path.style.color = new Color(1f, 0.42f, 0.35f);
        else if (asset.HasWarning)
            path.style.color = new Color(1f, 0.74f, 0.28f);
        row.Add(path);
        row.Add(BuildPipelineUI.SmallText(BuildAssetMetaText(asset)));
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (_stage == WorkflowStage.Curate)
            {
                SelectAsset(FindGroupIndex(asset.SourceGroupName), asset.AssetGUID);
                _curatePanelMode = CuratePanelMode.Details;
                Rebuild();
            }
            evt.StopPropagation();
        });
        return row;
    }

    private void DrawAssetEditor(VisualElement parent, string assetGuid)
    {
        CollectedAssetInfo preview = FindPreviewAsset(assetGuid);
        string assetPath = preview != null ? preview.AssetPath : AssetDatabase.GUIDToAssetPath(assetGuid);

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Asset"));
        card.Add(BuildPipelineUI.SmallText(string.IsNullOrEmpty(assetPath) ? "(missing asset)" : assetPath));
        if (preview != null)
        {
            card.Add(BuildPipelineUI.SmallText("Content: " + preview.ContentName));
            card.Add(BuildPipelineUI.SmallText("Group: " + preview.GroupName));
        }

        card.Add(BuildPipelineUI.SmallText("GUID: " + assetGuid));

        AssetOverride current = _curateSetting.FindAssetOverride(assetGuid);
        string address = current?.Address ?? string.Empty;
        List<string> labels = current?.Labels ?? new List<string>();

        card.Add(CreateTextField("Address Override", address, value =>
        {
            UpdateAssetOverride(assetGuid, value, current?.Labels);
            MarkCuratePreviewDirty();
        }));
        card.Add(BuildPipelineUI.SmallText(string.IsNullOrEmpty(address)
            ? $"Empty Address follows the Setting AddressStyle ({_curateSetting.AddressStyle})."
            : "Fixed Address override."));

        card.Add(CreateTextField("Labels Override", JoinLabelList(labels), value =>
        {
            UpdateAssetOverride(assetGuid, current?.Address, NormalizeLabels(value));
            MarkCuratePreviewDirty();
        }));

        VisualElement actions = BuildPipelineUI.Toolbar();
        actions.Add(BuildPipelineUI.ToolbarButton("Clear Override", () =>
        {
            if (current != null)
                _curateSetting.AssetOverrides.Remove(current);
            MarkCuratePreviewDirty();
        }, 116f));
        card.Add(actions);

        if (preview != null)
            AddAddressOperationButtons(card, "Apply Address", style => ApplyAddressStyleToAsset(assetGuid, preview, style));
        parent.Add(card);
    }

    private void DrawAssetOverridesEditor(VisualElement parent)
    {
        _curateSetting.AssetOverrides ??= new List<AssetOverride>();

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Asset Overrides"));
        card.Add(BuildPipelineUI.SmallText(
            "Override entries are keyed by Unity GUID. Empty Address follows the Setting AddressStyle; Labels have no automatic source."));

        if (_curateSetting.AssetOverrides.Count == 0)
            card.Add(BuildPipelineUI.SmallText("No override entry."));

        VisualElement header = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f
            }
        };
        header.Add(CreateColumnLabel("Asset GUID", 0f));
        header.Add(CreateFixedColumnLabel("Address", 220f));
        header.Add(CreateFixedColumnLabel("Labels", 200f));
        header.Add(CreateFixedColumnLabel(string.Empty, 72f));
        card.Add(header);

        for (int i = 0; i < _curateSetting.AssetOverrides.Count; i++)
        {
            AssetOverride entry = _curateSetting.AssetOverrides[i];
            if (entry == null)
                continue;

            card.Add(CreateAssetOverrideRow(entry));
        }

        ObjectField addField = new ObjectField("Add Override")
        {
            objectType = typeof(UnityEngine.Object),
            allowSceneObjects = false
        };
        addField.RegisterValueChangedCallback(evt =>
        {
            UnityEngine.Object assetObject = evt.newValue;
            addField.SetValueWithoutNotify(null);
            if (assetObject == null)
                return;

            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GetAssetPath(assetObject));
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.IsValidFolder(assetPath))
                return;

            _curateSetting.GetOrCreateAssetOverride(guid);
            MarkCuratePreviewDirty();
        });
        card.Add(addField);
        parent.Add(card);
    }

    private VisualElement CreateAssetOverrideRow(AssetOverride entry)
    {
        VisualElement box = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                width = Length.Percent(100f),
                minWidth = 0f,
                marginBottom = 4f
            }
        };

        VisualElement row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f
            }
        };

        string assetPath = AssetDatabase.GUIDToAssetPath(entry.AssetGUID);
        Label guidLabel = CreateColumnLabel(string.IsNullOrEmpty(assetPath) ? entry.AssetGUID : assetPath, 0f);
        guidLabel.tooltip = entry.AssetGUID;
        if (string.IsNullOrEmpty(assetPath))
            guidLabel.style.color = new Color(1f, 0.42f, 0.35f);
        row.Add(guidLabel);

        TextField address = new TextField { value = entry.Address ?? string.Empty, isDelayed = true };
        address.style.width = 220f;
        address.style.flexShrink = 0f;
        address.style.marginRight = 4f;
        address.RegisterValueChangedCallback(evt =>
        {
            entry.Address = (evt.newValue ?? string.Empty).Trim();
            MarkCuratePreviewDirty();
        });
        row.Add(address);

        TextField labels = new TextField { value = JoinLabelList(entry.Labels), isDelayed = true };
        labels.style.width = 200f;
        labels.style.flexShrink = 0f;
        labels.style.marginRight = 4f;
        labels.tooltip = "Comma, semicolon, or newline separated";
        labels.RegisterValueChangedCallback(evt =>
        {
            entry.Labels = NormalizeLabels(evt.newValue);
            MarkCuratePreviewDirty();
        });
        row.Add(labels);

        Button remove = new Button(() =>
        {
            _curateSetting.AssetOverrides.Remove(entry);
            MarkCuratePreviewDirty();
        }) { text = "Remove" };
        remove.style.width = 72f;
        remove.style.flexShrink = 0f;
        row.Add(remove);
        box.Add(row);

        string duplicateGuid = string.IsNullOrEmpty(entry.AssetGUID)
            ? "Missing AssetGUID."
            : string.Empty;
        if (!string.IsNullOrEmpty(duplicateGuid))
            box.Add(BuildPipelineUI.SmallText(duplicateGuid));
        return box;
    }

    private static Label CreateColumnLabel(string text, float width)
    {
        Label label = BuildPipelineUI.SmallText(text);
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.marginRight = 4f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        if (width > 0f)
        {
            label.style.width = width;
            label.style.flexGrow = 0f;
        }
        return label;
    }

    private static Label CreateFixedColumnLabel(string text, float width)
    {
        Label label = CreateColumnLabel(text, width);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    /// <summary>
    /// Setting 一级配置编辑区。persistent 为 true 时直接写回磁盘资产，否则写入 Curate candidate。
    /// </summary>
    private VisualElement CreateSettingEditor(AssetCollectionSetting setting, bool persistent)
    {
        if (setting == null)
            return new VisualElement();

        EnsureScanDefaults(setting);
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Setting"));

        EnumField addressStyle = new EnumField("Address Style", setting.AddressStyle);
        addressStyle.RegisterValueChangedCallback(evt =>
        {
            setting.AddressStyle = (AssetAddressStyle)evt.newValue;
            OnSettingFieldChanged(persistent);
        });
        card.Add(addressStyle);

        card.Add(BuildPipelineUI.Header("Ignore"));
        card.Add(CreateStringListEditor("Patterns", setting.IgnorePatterns, () => OnSettingFieldChanged(persistent), persistent ? setting : null));
        card.Add(CreateExcludedAssetsEditor(setting));

        card.Add(BuildPipelineUI.Header("Raw File Rules"));
        setting.RawFileRules ??= new RawFileRules();
        card.Add(CreateStringListEditor("Extensions", setting.RawFileRules.Extensions, () => OnSettingFieldChanged(persistent), persistent ? setting : null));
        card.Add(CreateStringListEditor("File Names", setting.RawFileRules.FileNames, () => OnSettingFieldChanged(persistent), persistent ? setting : null));
        card.Add(CreateStringListEditor("Folders", setting.RawFileRules.Folders, () => OnSettingFieldChanged(persistent), persistent ? setting : null));

        card.Add(BuildPipelineUI.Header("Share Policy"));
        setting.SharePolicy ??= new SharePolicyConfig();
        card.Add(CreateStringListEditor("Force Share Patterns", setting.SharePolicy.ForceSharePatterns, () => OnSettingFieldChanged(persistent), persistent ? setting : null));
        card.Add(CreateStringListEditor("No Share Patterns", setting.SharePolicy.NoSharePatterns, () => OnSettingFieldChanged(persistent), persistent ? setting : null));
        return card;
    }

    private void OnSettingFieldChanged(bool persistent)
    {
        if (persistent)
        {
            SavePersistentSetting();
            CollectorReverseIndex.Instance.MarkDirty();
            return;
        }

        MarkCuratePreviewDirty();
    }

    private VisualElement CreateExcludedAssetsEditor(AssetCollectionSetting setting)
    {
        VisualElement box = new VisualElement();
        box.style.marginTop = 8f;
        box.style.width = Length.Percent(100f);
        box.style.minWidth = 0f;
        box.Add(BuildPipelineUI.Header("Excluded Assets"));

        setting.ExcludedAssets ??= new List<AssetExclusion>();
        if (setting.ExcludedAssets.Count == 0)
            box.Add(BuildPipelineUI.SmallText("No excluded assets."));

        for (int i = 0; i < setting.ExcludedAssets.Count; i++)
        {
            int index = i;
            AssetExclusion exclusion = setting.ExcludedAssets[i];
            if (exclusion == null)
                continue;

            string assetPath = ResolveExclusionPath(exclusion);
            UnityEngine.Object assetObject = LoadAssetObject(assetPath);

            VisualElement row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    width = Length.Percent(100f),
                    minWidth = 0f,
                    marginBottom = 3f
                }
            };

            ObjectField objectField = new ObjectField
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false,
                value = assetObject
            };
            objectField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == assetObject)
                    return;

                if (!ReplaceExcludedAsset(setting, index, evt.newValue))
                    objectField.SetValueWithoutNotify(assetObject);
            });
            objectField.style.width = 0f;
            objectField.style.flexGrow = 1f;
            objectField.style.flexShrink = 1f;
            objectField.style.flexBasis = 0f;
            objectField.style.minWidth = 0f;
            objectField.style.marginRight = 4f;
            row.Add(objectField);

            Button remove = new Button(() =>
            {
                Undo.RecordObject(setting, "Remove Excluded Asset");
                setting.ExcludedAssets.RemoveAt(index);
                SavePersistentSetting();
                CollectorReverseIndex.Instance.MarkDirty();
                Rebuild();
            }) { text = "Remove" };
            remove.style.width = 72f;
            remove.style.flexShrink = 0f;
            row.Add(remove);
            box.Add(row);

            string pathText = !string.IsNullOrEmpty(assetPath)
                ? assetPath
                : string.Concat("Missing asset, GUID: ", exclusion.AssetGUID);
            Label pathLabel = BuildPipelineUI.SmallText(pathText);
            pathLabel.style.marginLeft = 4f;
            box.Add(pathLabel);
        }

        ObjectField addField = new ObjectField("Add Asset")
        {
            objectType = typeof(UnityEngine.Object),
            allowSceneObjects = false
        };
        addField.RegisterValueChangedCallback(evt =>
        {
            UnityEngine.Object assetObject = evt.newValue;
            addField.SetValueWithoutNotify(null);
            if (assetObject == null)
                return;

            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GetAssetPath(assetObject));
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.IsValidFolder(assetPath))
                return;

            Undo.RecordObject(setting, "Add Excluded Asset");
            if (setting.AddExcludedAsset(guid, assetPath))
            {
                SavePersistentSetting();
                CollectorReverseIndex.Instance.MarkDirty();
                Rebuild();
            }
        });
        box.Add(addField);
        return box;
    }

    private static ScrollView CreateScroll()
    {
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.paddingLeft = 8f;
        scroll.style.paddingRight = 8f;
        return scroll;
    }

    private VisualElement CreateTextField(string label, string value, Action<string> onChanged)
    {
        TextField field = new TextField(label)
        {
            value = value ?? string.Empty,
            isDelayed = true
        };
        field.style.width = Length.Percent(100f);
        field.style.minWidth = 0f;
        field.style.flexShrink = 1f;
        field.RegisterValueChangedCallback(evt => onChanged((evt.newValue ?? string.Empty).Trim()));
        return field;
    }

    private static void AddAddressOperationButtons(VisualElement parent, string title, Action<AssetAddressStyle> onApply)
    {
        VisualElement box = new VisualElement();
        box.style.marginTop = 4f;
        box.style.width = Length.Percent(100f);
        box.style.minWidth = 0f;
        box.Add(BuildPipelineUI.SmallText(title));

        VisualElement row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                flexWrap = Wrap.Wrap,
                minWidth = 0f
            }
        };

        row.Add(CreateAddressStyleButton("Apply Short", () => onApply(AssetAddressStyle.ShortName)));
        row.Add(CreateAddressStyleButton("Apply Path+Ext", () => onApply(AssetAddressStyle.LongAssetPathWithoutExtension)));
        row.Add(CreateAddressStyleButton("Apply Name#Type", () => onApply(AssetAddressStyle.NameType)));
        box.Add(row);
        parent.Add(box);
    }

    private static Button CreateAddressStyleButton(string text, Action onClick)
    {
        Button button = new Button(onClick) { text = text };
        button.style.width = 116f;
        button.style.minWidth = 116f;
        button.style.flexShrink = 0f;
        button.style.marginRight = 4f;
        button.style.marginBottom = 2f;
        return button;
    }

    /// <summary>
    /// 字符串列表编辑器。undoTarget 非空时记录 Undo，用于直接写回磁盘资产的持久字段。
    /// 持久字段沿用旧的即时清洗；Curate candidate 保留原文本，交由保存前校验报告空项与首尾空白。
    /// </summary>
    private VisualElement CreateStringListEditor(string title, List<string> values, Action onChanged, UnityEngine.Object undoTarget = null)
    {
        values ??= new List<string>();
        bool immediate = undoTarget != null;
        VisualElement box = new VisualElement();
        box.style.marginTop = 4f;
        box.style.width = Length.Percent(100f);
        box.style.minWidth = 0f;
        box.Add(BuildPipelineUI.SmallText(title));

        for (int i = 0; i < values.Count; i++)
        {
            int index = i;
            VisualElement row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    width = Length.Percent(100f),
                    minWidth = 0f
                }
            };
            TextField field = new TextField
            {
                value = values[i],
                isDelayed = true
            };
            field.style.width = 0f;
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.style.flexBasis = 0f;
            field.style.minWidth = 0f;
            field.style.marginRight = 4f;
            field.RegisterValueChangedCallback(evt =>
            {
                RecordUndo(undoTarget, "Edit Collection Setting");
                string raw = evt.newValue ?? string.Empty;
                values[index] = immediate ? raw.Trim() : raw;
                onChanged?.Invoke();
                Rebuild();
            });
            row.Add(field);
            Button remove = new Button(() =>
            {
                RecordUndo(undoTarget, "Edit Collection Setting");
                values.RemoveAt(index);
                onChanged?.Invoke();
                Rebuild();
            }) { text = "Remove" };
            remove.style.width = 72f;
            remove.style.flexShrink = 0f;
            row.Add(remove);
            box.Add(row);
        }

        VisualElement addRow = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f
            }
        };
        TextField addField = new TextField();
        addField.style.width = 0f;
        addField.style.flexGrow = 1f;
        addField.style.flexShrink = 1f;
        addField.style.flexBasis = 0f;
        addField.style.minWidth = 0f;
        addField.style.marginRight = 4f;
        addRow.Add(addField);
        Button add = new Button(() =>
        {
            string value = addField.value ?? string.Empty;
            if (immediate)
                value = value.Trim();
            if (string.IsNullOrEmpty(value))
                return;
            RecordUndo(undoTarget, "Edit Collection Setting");
            values.Add(value);
            onChanged?.Invoke();
            Rebuild();
        }) { text = "Add" };
        add.style.width = 72f;
        add.style.flexShrink = 0f;
        addRow.Add(add);
        box.Add(addRow);
        return box;
    }

    private static void RecordUndo(UnityEngine.Object target, string name)
    {
        if (target != null)
            Undo.RecordObject(target, name);
    }

    private static Label CreateNavLabel(string text, bool selected, float height)
    {
        var label = new Label(text);
        label.style.height = height;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.paddingLeft = 6f;
        label.style.paddingRight = 6f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.color = selected ? Color.white : BuildPipelineUI.SecondaryTextColor;
        label.style.backgroundColor = selected ? BuildPipelineUI.ActiveColor : Color.clear;
        label.style.marginBottom = 2f;
        return label;
    }

    private static Label CreateNavDisclosureLabel(string text, bool expanded, bool selected, float marginLeft, float height)
    {
        return CreateNavLabel((expanded ? "v " : "> ") + text, selected, height, marginLeft);
    }

    private static Label CreateNavLabel(string text, bool selected, float height, float marginLeft)
    {
        Label label = CreateNavLabel(text, selected, height);
        label.style.marginLeft = marginLeft;
        return label;
    }

    private void FitCurateSidebarWidthToContent()
    {
        if (_draggingCurateSplitter || _curateSetting?.Groups == null)
            return;

        int maxChars = 0;
        for (int gi = 0; gi < _curateSetting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = _curateSetting.Groups[gi];
            string suffix = group != null && !group.Enabled ? " [Disabled]" : string.Empty;
            maxChars = Mathf.Max(maxChars, GetGroupDisplayName(group).Length + suffix.Length + 6);

            List<CollectedAssetInfo> assets = GetAssetsForGroup(_curateResult, group?.GroupName);
            for (int ai = 0; ai < assets.Count; ai++)
                maxChars = Mathf.Max(maxChars, GetAssetNavName(assets[ai]).Length + 10);
        }

        float desired = SidebarPaddingWidth + maxChars * SidebarCharWidth;
        _curateSidebarWidth = Mathf.Clamp(Mathf.Max(_curateSidebarWidth, desired), MinSidebarWidth, MaxSidebarWidth);
    }

    private static void ToggleCollapsed(HashSet<string> collapsedSet, string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (!collapsedSet.Add(key))
            collapsedSet.Remove(key);
    }

    private static string GetGroupNavKey(int groupIndex, AssetCollectionGroup group)
    {
        return string.Concat(groupIndex, ":", GetGroupDisplayName(group));
    }

    private static VisualElement CreateMetricStrip(int groupCount, int assetCount, int bundleCount, int warningCount, int errorCount)
    {
        VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        row.style.marginBottom = 8f;
        row.Add(CreateMetric("Groups", groupCount.ToString()));
        row.Add(CreateMetric("Assets", assetCount.ToString()));
        row.Add(CreateMetric("Bundles", bundleCount.ToString()));
        row.Add(CreateMetric("Warnings", warningCount.ToString()));
        row.Add(CreateMetric("Errors", errorCount.ToString()));
        return row;
    }

    private static VisualElement CreateMetric(string label, string value)
    {
        VisualElement metric = BuildPipelineUI.Card();
        metric.style.flexGrow = 1f;
        metric.style.marginRight = 6f;
        metric.Add(BuildPipelineUI.SmallText(label));
        Label valueLabel = BuildPipelineUI.Header(string.IsNullOrEmpty(value) ? "-" : value);
        valueLabel.style.marginBottom = 0f;
        metric.Add(valueLabel);
        return metric;
    }

    private static void RenderMessages(VisualElement parent, ScanResult result)
    {
        if (result?.Messages == null || result.Messages.Count == 0)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Messages"));
        for (int i = 0; i < result.Messages.Count; i++)
        {
            BuildMessage message = result.Messages[i];
            card.Add(BuildPipelineUI.SmallText($"{message.Severity}  {message.Code}  {message.Message}"));
        }
        parent.Add(card);
    }

    private void AddGroup()
    {
        _curateSetting.Groups ??= new List<AssetCollectionGroup>();
        _curateSetting.Groups.Add(new AssetCollectionGroup
        {
            GroupName = "NewGroup" + (_curateSetting.Groups.Count + 1),
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackTogetherByLabel
        });
        SelectGroup(_curateSetting.Groups.Count - 1);
        _curatePanelMode = CuratePanelMode.Details;
        MarkCuratePreviewDirty();
    }

    private void DeleteSelection()
    {
        if (_selectionType == SelectionType.Group)
        {
            if (_curateSetting.Groups != null && _selectedGroupIndex >= 0 && _selectedGroupIndex < _curateSetting.Groups.Count)
                _curateSetting.Groups.RemoveAt(_selectedGroupIndex);
            ClearSelection();
        }
        else if (_selectionType == SelectionType.Asset)
        {
            CollectedAssetInfo selected = FindPreviewAsset(_selectedAssetGuid);
            if (selected == null)
                return;

            RemoveAssetFromCurate(selected);
            SelectGroup(FindGroupIndex(selected.SourceGroupName));
        }

        _curatePanelMode = CuratePanelMode.Details;
        RescanCurate(false, CuratePanelMode.Details);
    }

    private void AddAssetToSelectedGroup()
    {
        AssetCollectionGroup group = GetSelectedGroup();
        if (group == null)
            return;

        string assetPath = PickCollectPath(true);
        if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            return;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
            return;

        if (IsExcludedInCurate(guid))
        {
            RemoveExcludedFromCurate(guid);
        }
        else if (CollectorMutationUtility.IsExcludedGuid(guid))
        {
            // 外部 Inspector/右键菜单可能刚修改了保存态，进入 Curate 前优先恢复保存态排除。
            _suppressExternalCollectorChanged = true;
            try
            {
                CollectorMutationUtility.RestoreExcluded(assetPath);
            }
            finally
            {
                _suppressExternalCollectorChanged = false;
            }
        }

        if (!IsCoveredByCurateCollector(assetPath))
        {
            group.Collectors ??= new List<Collector>();
            group.Collectors.Add(CreateFileCollectorForCurate(assetPath));
        }

        RescanCurate(false, CuratePanelMode.Details);
    }

    private static Collector CreateEmptyCollector(ECollectPathType pathType)
    {
        return new Collector
        {
            CollectPath = string.Empty,
            CollectPathType = pathType
        };
    }

    private void RemoveAssetFromCurate(CollectedAssetInfo asset)
    {
        if (asset == null)
            return;

        if (RemoveDirectFileCollectorFromCurate(asset))
            return;

        AddExcludedToCurate(asset.AssetPath);
    }

    private bool RemoveDirectFileCollectorFromCurate(CollectedAssetInfo asset)
    {
        AssetCollectionGroup group = GetSourceGroup(asset);
        if (group?.Collectors == null)
            return false;

        string assetPath = CollectorPathUtility.NormalizePath(asset.AssetPath);
        for (int i = group.Collectors.Count - 1; i >= 0; i--)
        {
            Collector collector = group.Collectors[i];
            if (collector == null || collector.CollectPathType != ECollectPathType.File)
                continue;

            if (!string.Equals(CollectorPathUtility.NormalizePath(collector.CollectPath), assetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            group.Collectors.RemoveAt(i);
            return true;
        }

        return false;
    }

    private bool IsCoveredByCurateCollector(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        if (_curateSetting?.Groups == null)
            return false;

        for (int gi = 0; gi < _curateSetting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = _curateSetting.Groups[gi];
            if (group?.Collectors == null || !group.Enabled)
                continue;

            for (int ci = 0; ci < group.Collectors.Count; ci++)
            {
                Collector collector = group.Collectors[ci];
                string collectPath = CollectorPathUtility.NormalizePath(collector?.CollectPath);
                if (string.IsNullOrEmpty(collectPath))
                    continue;

                if (collector.CollectPathType == ECollectPathType.File &&
                    string.Equals(collectPath, normalized, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (collector.CollectPathType == ECollectPathType.Folder &&
                    CollectorPathUtility.IsPathContained(collectPath, normalized))
                    return true;
            }
        }

        return false;
    }

    private bool IsExcludedInCurate(string guid)
    {
        return _curateSetting != null && _curateSetting.IsExcludedAssetGuid(guid);
    }

    private bool AddExcludedToCurate(string assetPath)
    {
        if (_curateSetting == null || string.IsNullOrEmpty(assetPath))
            return false;

        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        string guid = AssetDatabase.AssetPathToGUID(normalized);
        if (string.IsNullOrEmpty(guid))
            return false;

        _curateSetting.ExcludedAssets ??= new List<AssetExclusion>();
        return _curateSetting.AddExcludedAsset(guid, normalized);
    }

    private bool RemoveExcludedFromCurate(string guid)
    {
        return _curateSetting != null && _curateSetting.RemoveExcludedAsset(guid);
    }

    private AssetCollectionGroup GetSourceGroup(CollectedAssetInfo asset)
    {
        return GetGroupAt(FindGroupIndex(asset?.SourceGroupName));
    }

    private static Collector CreateFileCollectorForCurate(string assetPath)
    {
        return new Collector
        {
            CollectPath = CollectorPathUtility.NormalizePath(assetPath),
            CollectPathType = ECollectPathType.File
        };
    }

    private AssetCollectionGroup GetSelectedGroup()
    {
        return GetGroupAt(_selectedGroupIndex);
    }

    private AssetCollectionGroup GetGroupAt(int groupIndex)
    {
        if (_curateSetting?.Groups == null || groupIndex < 0 || groupIndex >= _curateSetting.Groups.Count)
            return null;
        return _curateSetting.Groups[groupIndex];
    }

    private void EnsureSelection(bool selectFirst)
    {
        if (_curateSetting?.Groups == null || _curateSetting.Groups.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (selectFirst || _selectedGroupIndex < 0 || _selectedGroupIndex >= _curateSetting.Groups.Count)
        {
            SelectGroup(0);
            return;
        }

        if (_selectionType == SelectionType.Asset && FindPreviewAsset(_selectedAssetGuid) == null)
            SelectGroup(0);
    }

    private void SelectGroup(int groupIndex)
    {
        _selectionType = SelectionType.Group;
        _selectedGroupIndex = groupIndex;
        _selectedAssetGuid = null;
    }

    private void SelectAsset(int groupIndex, string assetGuid)
    {
        _selectionType = SelectionType.Asset;
        _selectedGroupIndex = groupIndex;
        _selectedAssetGuid = assetGuid;
    }

    private void ClearSelection()
    {
        _selectionType = SelectionType.None;
        _selectedGroupIndex = -1;
        _selectedAssetGuid = null;
    }

    private bool IsSelectedGroup(int groupIndex)
    {
        return _selectionType == SelectionType.Group && _selectedGroupIndex == groupIndex;
    }

    private bool IsSelectedAsset(string assetGuid)
    {
        return _selectionType == SelectionType.Asset &&
               !string.IsNullOrEmpty(assetGuid) &&
               string.Equals(_selectedAssetGuid, assetGuid, StringComparison.Ordinal);
    }

    private int FindGroupIndex(string groupName)
    {
        if (_curateSetting?.Groups == null)
            return -1;

        for (int i = 0; i < _curateSetting.Groups.Count; i++)
        {
            AssetCollectionGroup group = _curateSetting.Groups[i];
            if (string.Equals(group?.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void AddProjectScanGroup(AssetCollectionSetting setting, string folder)
    {
        string groupName = CreateProjectScanGroupName(GetLastPathSegment(folder));
        var group = new AssetCollectionGroup
        {
            GroupName = groupName,
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackTogetherByLabel
        };
        group.Collectors.Add(CreateFolderCollector(folder));
        setting.Groups.Add(group);
    }

    private static void AddScatteredSceneGroups(AssetCollectionSetting setting)
    {
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
        if (guids == null)
            return;

        List<string> ignorePatterns = setting?.GetEffectiveIgnorePatterns();
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (string.IsNullOrEmpty(assetPath))
                continue;
            if (!IsSceneAssetPath(assetPath))
                continue;
            if (CollectorPathUtility.MatchesIgnorePattern(assetPath, "Assets", ignorePatterns))
                continue;
            if (IsExcludedBySetting(setting, assetPath))
                continue;
            if (IsOwnedByExistingFileCollector(setting, assetPath))
                continue;

            string groupName = CreateProjectScanGroupName(GetSceneGroupName(assetPath));
            AssetCollectionGroup group = FindOrCreateGroup(setting, groupName);
            group.BundlePackingMode = BundlePackingMode.PackSeparately;
            group.Collectors.Add(CreateFileCollector(assetPath));
        }
    }

    private static bool NormalizeSceneCollectors(AssetCollectionSetting setting)
    {
        if (setting?.Groups == null)
            return false;

        bool changed = false;
        for (int gi = 0; gi < setting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = setting.Groups[gi];
            if (group?.Collectors == null)
                continue;

            for (int ci = group.Collectors.Count - 1; ci >= 0; ci--)
            {
                Collector collector = group.Collectors[ci];
                if (collector == null || collector.CollectPathType != ECollectPathType.Folder)
                    continue;

                string folder = CollectorPathUtility.NormalizePath(collector.CollectPath);
                if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                    continue;
                if (HasNonSceneCollectableAssets(folder, setting))
                    continue;

                List<string> scenePaths = CollectSceneAssetPaths(folder, setting);
                if (scenePaths.Count == 0)
                    continue;

                group.Collectors.RemoveAt(ci);
                for (int si = 0; si < scenePaths.Count; si++)
                {
                    string scenePath = scenePaths[si];
                    if (IsOwnedByExistingFileCollector(setting, scenePath))
                        continue;

                    string groupName = CreateProjectScanGroupName(GetSceneGroupName(scenePath));
                    AssetCollectionGroup sceneGroup = FindOrCreateGroup(setting, groupName);
                    sceneGroup.BundlePackingMode = BundlePackingMode.PackSeparately;
                    sceneGroup.Collectors.Add(CreateFileCollector(scenePath));
                }
                changed = true;
            }
        }

        return changed;
    }

    private static bool HasNonSceneCollectableAssets(string folder, AssetCollectionSetting setting)
    {
        List<string> ignorePatterns = setting?.GetEffectiveIgnorePatterns();
        if (CollectorPathUtility.MatchesIgnorePattern(folder, "Assets", ignorePatterns))
            return false;

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
        if (guids == null || guids.Length == 0)
            return false;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;
            if (IsSceneAssetPath(assetPath))
                continue;
            if (IsExcludedBySetting(setting, assetPath))
                continue;
            if (!CollectorPathUtility.MatchesIgnorePattern(assetPath, "Assets", ignorePatterns))
                return true;
        }

        return false;
    }

    private static List<string> CollectSceneAssetPaths(string folder, AssetCollectionSetting setting)
    {
        var scenePaths = new List<string>();
        List<string> ignorePatterns = setting?.GetEffectiveIgnorePatterns();
        if (CollectorPathUtility.MatchesIgnorePattern(folder, "Assets", ignorePatterns))
            return scenePaths;

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
        if (guids == null || guids.Length == 0)
            return scenePaths;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;
            if (!IsSceneAssetPath(assetPath))
                continue;
            if (IsExcludedBySetting(setting, assetPath))
                continue;
            if (CollectorPathUtility.MatchesIgnorePattern(assetPath, "Assets", ignorePatterns))
                continue;

            scenePaths.Add(assetPath);
        }

        scenePaths.Sort(StringComparer.OrdinalIgnoreCase);
        return scenePaths;
    }

    private static bool IsSceneAssetPath(string assetPath)
    {
        return string.Equals(System.IO.Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedByExistingFileCollector(AssetCollectionSetting setting, string assetPath)
    {
        if (setting?.Groups == null)
            return false;

        string normalizedAsset = CollectorPathUtility.NormalizePath(assetPath);
        for (int gi = 0; gi < setting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = setting.Groups[gi];
            if (group?.Collectors == null)
                continue;

            for (int ci = 0; ci < group.Collectors.Count; ci++)
            {
                Collector collector = group.Collectors[ci];
                if (collector?.CollectPathType != ECollectPathType.File)
                    continue;

                string collectPath = CollectorPathUtility.NormalizePath(collector.CollectPath);
                if (string.Equals(collectPath, normalizedAsset, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static AssetCollectionGroup FindOrCreateGroup(AssetCollectionSetting setting, string groupName)
    {
        if (setting.Groups == null)
            setting.Groups = new List<AssetCollectionGroup>();

        for (int i = 0; i < setting.Groups.Count; i++)
        {
            AssetCollectionGroup group = setting.Groups[i];
            if (group != null && string.Equals(group.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                return group;
        }

        var created = new AssetCollectionGroup
        {
            GroupName = groupName,
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackSeparately
        };
        setting.Groups.Add(created);
        return created;
    }

    private static Collector CreateFolderCollector(string path)
    {
        return new Collector
        {
            CollectPath = path,
            CollectPathType = ECollectPathType.Folder
        };
    }

    private static Collector CreateFileCollector(string path)
    {
        return new Collector
        {
            CollectPath = path,
            CollectPathType = ECollectPathType.File
        };
    }

    private static string GetSceneGroupName(string assetPath)
    {
        string[] segments = assetPath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.Equals(segments[i], "Scenes", StringComparison.OrdinalIgnoreCase))
                return "Scenes";
        }

        string parent = System.IO.Path.GetDirectoryName(assetPath);
        return string.IsNullOrEmpty(parent) ? "Scenes" : string.Concat(GetLastPathSegment(parent), "Scenes");
    }

    private static string GetLastPathSegment(string path)
    {
        string normalized = CollectorPathUtility.NormalizePath(path);
        int slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
    }

    private static string CreateProjectScanGroupName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Group";

        string trimmed = rawName.Trim();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(trimmed.Length);
        bool capitalizeNext = true;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (IsBundleSegmentCharAllowed(c))
            {
                builder.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        string sanitized = builder.ToString();
        return string.IsNullOrEmpty(sanitized) ? "Group" : sanitized;
    }

    private static bool IsBundleSegmentCharAllowed(char c)
    {
        for (int i = 0; i < SystemIdentifiers.ReservedChars.Length; i++)
        {
            if (c == SystemIdentifiers.ReservedChars[i])
                return false;
        }

        return true;
    }

    private static void EnsureScanDefaults(AssetCollectionSetting setting)
    {
        if (setting == null)
            return;

        if (setting.IgnorePatterns == null)
            setting.IgnorePatterns = AssetCollectionSetting.CreateDefaultIgnorePatterns();
        if (setting.ExcludedAssets == null)
            setting.ExcludedAssets = new List<AssetExclusion>();
        setting.RawFileRules ??= new RawFileRules();
        setting.SharePolicy ??= new SharePolicyConfig();
        setting.AssetOverrides ??= new List<AssetOverride>();
        setting.Groups ??= new List<AssetCollectionGroup>();
    }

    private void SavePersistentSetting()
    {
        if (_setting == null)
            return;

        _setting.RefreshExcludedAssetPaths();
        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
    }

    private static string ResolveExclusionPath(AssetExclusion exclusion)
    {
        if (exclusion == null)
            return string.Empty;

        string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(exclusion.AssetGUID));
        if (!string.IsNullOrEmpty(assetPath))
        {
            exclusion.AssetPath = assetPath;
            return assetPath;
        }

        return CollectorPathUtility.NormalizePath(exclusion.AssetPath);
    }

    private bool ReplaceExcludedAsset(AssetCollectionSetting setting, int index, UnityEngine.Object assetObject)
    {
        if (setting?.ExcludedAssets == null || index < 0 || index >= setting.ExcludedAssets.Count || assetObject == null)
            return false;

        string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GetAssetPath(assetObject));
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid) || AssetDatabase.IsValidFolder(assetPath))
            return false;

        Undo.RecordObject(setting, "Replace Excluded Asset");
        AssetExclusion exclusion = setting.ExcludedAssets[index];
        if (exclusion == null)
        {
            setting.ExcludedAssets[index] = new AssetExclusion
            {
                AssetGUID = guid,
                AssetPath = assetPath
            };
        }
        else
        {
            exclusion.AssetGUID = guid;
            exclusion.AssetPath = assetPath;
        }
        RemoveDuplicateExclusions(setting.ExcludedAssets, guid, index);

        SavePersistentSetting();
        CollectorReverseIndex.Instance.MarkDirty();
        Rebuild();
        return true;
    }

    private static void RemoveDuplicateExclusions(List<AssetExclusion> exclusions, string assetGuid, int keepIndex)
    {
        if (exclusions == null || string.IsNullOrEmpty(assetGuid))
            return;

        for (int i = exclusions.Count - 1; i >= 0; i--)
        {
            if (i == keepIndex)
                continue;

            AssetExclusion exclusion = exclusions[i];
            if (exclusion != null && string.Equals(exclusion.AssetGUID, assetGuid, StringComparison.Ordinal))
                exclusions.RemoveAt(i);
        }
    }

    private static UnityEngine.Object LoadAssetObject(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        UnityEngine.Object assetObject = AssetDatabase.LoadMainAssetAtPath(normalized);
        if (assetObject != null)
            return assetObject;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(normalized);
        return assets != null && assets.Length > 0 ? assets[0] : null;
    }

    private static bool IsExcludedBySetting(AssetCollectionSetting setting, string assetPath)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        return !string.IsNullOrEmpty(guid) && setting != null && setting.IsExcludedAssetGuid(guid);
    }

    private static bool HasGroups(AssetCollectionSetting setting)
    {
        return setting?.Groups != null && setting.Groups.Count > 0;
    }

    private static int CountMessages(ScanResult result, BuildSeverity severity)
    {
        int count = 0;
        if (result?.Messages == null)
            return count;

        for (int i = 0; i < result.Messages.Count; i++)
        {
            if (result.Messages[i].Severity == severity)
                count++;
        }
        return count;
    }

    private static int CountDistinctBundles(List<CollectedAssetInfo> assets)
    {
        if (assets == null || assets.Count == 0)
            return 0;

        HashSet<string> bundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].ContentName))
                bundles.Add(assets[i].ContentName);
        }
        return bundles.Count;
    }

    private static int CountAssetsForGroup(ScanResult result, string groupName)
    {
        return GetAssetsForGroup(result, groupName).Count;
    }

    private static int CountBundlesForGroup(ScanResult result, string groupName)
    {
        HashSet<string> bundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<CollectedAssetInfo> assets = GetAssetsForGroup(result, groupName);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].ContentName))
                bundles.Add(assets[i].ContentName);
        }
        return bundles.Count;
    }

    private static List<CollectedAssetInfo> GetAssetsForGroup(ScanResult result, string groupName)
    {
        List<CollectedAssetInfo> assets = new List<CollectedAssetInfo>();
        if (result?.Assets == null)
            return assets;

        for (int i = 0; i < result.Assets.Count; i++)
        {
            CollectedAssetInfo asset = result.Assets[i];
            if (string.Equals(asset.SourceGroupName, groupName, StringComparison.OrdinalIgnoreCase))
                assets.Add(asset);
        }
        return assets;
    }

    private static List<CollectedAssetInfo> GetAssetsForCollector(ScanResult result, string groupName, string collectorPath)
    {
        List<CollectedAssetInfo> assets = new List<CollectedAssetInfo>();
        if (result?.Assets == null)
            return assets;

        string normalizedCollector = CollectorPathUtility.NormalizePath(collectorPath);
        for (int i = 0; i < result.Assets.Count; i++)
        {
            CollectedAssetInfo asset = result.Assets[i];
            if (!string.Equals(asset.SourceGroupName, groupName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(CollectorPathUtility.NormalizePath(asset.SourceCollectorPath), normalizedCollector, StringComparison.OrdinalIgnoreCase))
            {
                assets.Add(asset);
            }
        }
        return assets;
    }

    private static Dictionary<string, List<CollectedAssetInfo>> BucketByBundle(List<CollectedAssetInfo> assets)
    {
        Dictionary<string, List<CollectedAssetInfo>> bundles = new Dictionary<string, List<CollectedAssetInfo>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            string key = string.IsNullOrEmpty(assets[i].ContentName) ? "(invalid content)" : assets[i].ContentName;
            if (!bundles.TryGetValue(key, out List<CollectedAssetInfo> bucket))
            {
                bucket = new List<CollectedAssetInfo>();
                bundles[key] = bucket;
            }
            bucket.Add(assets[i]);
        }
        return bundles;
    }

    private static Color GetBundleColor(int index)
    {
        Color[] colors =
        {
            new Color(0.32f, 0.58f, 0.92f),
            new Color(0.37f, 0.72f, 0.48f),
            new Color(0.94f, 0.62f, 0.26f),
            new Color(0.77f, 0.48f, 0.88f),
            new Color(0.88f, 0.45f, 0.45f)
        };
        return colors[index % colors.Length];
    }

    private static string BuildAssetMetaText(CollectedAssetInfo asset)
    {
        string state = asset.HasError ? "Error" : asset.HasWarning ? "Warning" : "OK";
        return $"{state}    Group: {asset.GroupName}    Address: {asset.Address}    Type: {asset.PrimaryType}    ContentType: {asset.ContentType}";
    }

    private static string GetAssetNavName(CollectedAssetInfo asset)
    {
        if (asset == null)
            return "(missing asset)";

        if (!string.IsNullOrEmpty(asset.Address))
            return asset.Address;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(asset.AssetPath);
        return string.IsNullOrEmpty(fileName) ? asset.AssetPath : fileName;
    }

    private CollectedAssetInfo FindPreviewAsset(string assetGuid)
    {
        if (_curateResult?.Assets == null || string.IsNullOrEmpty(assetGuid))
            return null;

        for (int i = 0; i < _curateResult.Assets.Count; i++)
        {
            CollectedAssetInfo asset = _curateResult.Assets[i];
            if (asset != null && string.Equals(asset.AssetGUID, assetGuid, StringComparison.Ordinal))
                return asset;
        }

        return null;
    }

    /// <summary>
    /// 写入或清除资产级人工覆盖。Address 与 Labels 都为空时删除条目，避免配置里堆积空覆盖。
    /// </summary>
    private void UpdateAssetOverride(string assetGuid, string address, List<string> labels)
    {
        if (_curateSetting == null || string.IsNullOrEmpty(assetGuid))
            return;

        string normalizedAddress = (address ?? string.Empty).Trim();
        List<string> normalizedLabels = labels ?? new List<string>();
        AssetOverride existing = _curateSetting.FindAssetOverride(assetGuid);

        if (string.IsNullOrEmpty(normalizedAddress) && normalizedLabels.Count == 0)
        {
            if (existing != null)
                _curateSetting.AssetOverrides.Remove(existing);
            return;
        }

        AssetOverride target = existing ?? _curateSetting.GetOrCreateAssetOverride(assetGuid);
        target.Address = normalizedAddress;
        target.Labels = new List<string>(normalizedLabels);
    }

    /// <summary>把指定样式生成的 Address 固化为人工覆盖，供人工按需固定地址。</summary>
    private void ApplyAddressStyleToAsset(string assetGuid, CollectedAssetInfo preview, AssetAddressStyle style)
    {
        if (preview == null)
            return;

        UpdateAssetOverride(assetGuid, GeneratePreviewAddress(preview, style), _curateSetting.FindAssetOverride(assetGuid)?.Labels);
        MarkCuratePreviewDirty();
    }

    private void ApplyAddressStyleToGroup(string groupName, AssetAddressStyle style)
    {
        List<CollectedAssetInfo> assets = GetAssetsForGroup(_curateResult, groupName);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            if (string.IsNullOrEmpty(asset.AssetGUID))
                continue;

            UpdateAssetOverride(asset.AssetGUID, GeneratePreviewAddress(asset, style), _curateSetting.FindAssetOverride(asset.AssetGUID)?.Labels);
        }

        MarkCuratePreviewDirty();
    }

    private static string GeneratePreviewAddress(CollectedAssetInfo preview, AssetAddressStyle style)
    {
        return preview == null
            ? string.Empty
            : AssetAddressGenerator.GenerateAddress(preview.AssetPath, preview.PrimaryType, style);
    }

    private static string JoinLabelList(List<string> labels)
    {
        return labels == null || labels.Count == 0 ? string.Empty : string.Join(", ", labels);
    }

    private static List<string> NormalizeLabels(string rawLabels)
    {
        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (rawLabels ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string label = part.Trim();
            if (label.Length > 0 && seen.Add(label))
                labels.Add(label);
        }
        return labels;
    }

    private static string GetGroupDisplayName(AssetCollectionGroup group)
    {
        return string.IsNullOrEmpty(group?.GroupName) ? "(unnamed group)" : group.GroupName;
    }

    private static string PickCollectPath(bool isFile)
    {
        string absolutePath = isFile
            ? EditorUtility.OpenFilePanel("Select Collect File", Application.dataPath, string.Empty)
            : EditorUtility.OpenFolderPanel("Select Collect Folder", Application.dataPath, string.Empty);

        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;

        if (!FYAssetPathUtility.TryMakeAssetPath(absolutePath, Application.dataPath, out string assetPath))
            return string.Empty;

        return assetPath;
    }

    private static AssetCollectionSetting CloneSetting(AssetCollectionSetting source)
    {
        var clone = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        clone.AddressStyle = source != null ? source.AddressStyle : AssetAddressStyle.ShortName;
        clone.IgnorePatterns = CloneList(source?.IgnorePatterns);
        clone.ExcludedAssets = CloneExcludedAssets(source?.ExcludedAssets);
        clone.Groups = CloneGroups(source?.Groups);
        clone.AssetOverrides = CloneAssetOverrides(source?.AssetOverrides);
        clone.RawFileRules = CloneRawFileRules(source?.RawFileRules);
        clone.SharePolicy = CloneSharePolicy(source?.SharePolicy);
        return clone;
    }

    private static ScanResult CloneScanResult(ScanResult source)
    {
        if (source == null)
            return null;

        return new ScanResult
        {
            Assets = source.Assets != null ? new List<CollectedAssetInfo>(source.Assets) : new List<CollectedAssetInfo>(),
            Messages = source.Messages != null ? new List<BuildMessage>(source.Messages) : new List<BuildMessage>()
        };
    }

    private static List<AssetCollectionGroup> CloneGroups(List<AssetCollectionGroup> source)
    {
        var groups = new List<AssetCollectionGroup>();
        if (source == null)
            return groups;

        for (int i = 0; i < source.Count; i++)
        {
            AssetCollectionGroup group = source[i];
            if (group == null)
                continue;
            groups.Add(new AssetCollectionGroup
            {
                GroupName = group.GroupName,
                Enabled = group.Enabled,
                BundlePackingMode = group.BundlePackingMode,
                Collectors = CloneCollectors(group.Collectors)
            });
        }
        return groups;
    }

    private static List<Collector> CloneCollectors(List<Collector> source)
    {
        var collectors = new List<Collector>();
        if (source == null)
            return collectors;

        for (int i = 0; i < source.Count; i++)
        {
            Collector collector = source[i];
            if (collector == null)
                continue;
            collectors.Add(new Collector
            {
                CollectPath = collector.CollectPath,
                CollectPathType = collector.CollectPathType
            });
        }
        return collectors;
    }

    private static List<AssetOverride> CloneAssetOverrides(List<AssetOverride> source)
    {
        var overrides = new List<AssetOverride>();
        if (source == null)
            return overrides;

        for (int i = 0; i < source.Count; i++)
        {
            AssetOverride entry = source[i];
            if (entry == null)
                continue;

            overrides.Add(new AssetOverride
            {
                AssetGUID = entry.AssetGUID,
                Address = entry.Address,
                Labels = CloneList(entry.Labels)
            });
        }

        return overrides;
    }

    private static List<AssetExclusion> CloneExcludedAssets(List<AssetExclusion> source)
    {
        var exclusions = new List<AssetExclusion>();
        if (source == null)
            return exclusions;

        for (int i = 0; i < source.Count; i++)
        {
            AssetExclusion exclusion = source[i];
            if (exclusion == null)
                continue;

            exclusions.Add(new AssetExclusion
            {
                AssetGUID = exclusion.AssetGUID,
                AssetPath = exclusion.AssetPath
            });
        }

        return exclusions;
    }

    private static RawFileRules CloneRawFileRules(RawFileRules source)
    {
        if (source == null)
            return new RawFileRules();

        return new RawFileRules
        {
            Extensions = CloneList(source.Extensions),
            FileNames = CloneList(source.FileNames),
            Folders = CloneList(source.Folders)
        };
    }

    private static SharePolicyConfig CloneSharePolicy(SharePolicyConfig source)
    {
        if (source == null)
            return new SharePolicyConfig();

        return new SharePolicyConfig
        {
            NoSharePatterns = CloneList(source.NoSharePatterns),
            ForceSharePatterns = CloneList(source.ForceSharePatterns)
        };
    }

    private static List<string> CloneList(List<string> source)
    {
        return source == null ? new List<string>() : new List<string>(source);
    }

    private void DrawNoSetting()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 420f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateTitle("未找到 AssetCollectionSetting"));
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody(FYAssetABSettings.Instance.AssetCollectionSettingPath));
        panel.Add(new Button(CreateAssetCollectionSetting) { text = "Create" });
    }

    private void CreateAssetCollectionSetting()
    {
        BuildPipelineUI.EnsureAssetParentFolder(FYAssetABSettings.Instance.AssetCollectionSettingPath);
        AssetCollectionSetting newSetting = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        AssetDatabase.CreateAsset(newSetting, FYAssetABSettings.Instance.AssetCollectionSettingPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
        Rebuild();
    }

    private sealed class ProjectScanSnapshot
    {
        public AssetCollectionSetting PreviewSetting;
        public ScanResult Result;
    }
}

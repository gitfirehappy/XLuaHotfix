using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AssetsCollection workflow panel. Project Scan is read-only; Curate owns candidate editing and final save.
/// </summary>
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
        Package,
        Group
    }

    private enum CuratePanelMode
    {
        Details,
        ScanPreview
    }

    private static readonly ECollectorType[] ManualCollectorTypes =
    {
        ECollectorType.Main,
        ECollectorType.Static,
        ECollectorType.Depend
    };

    private static readonly List<string> ManualCollectorTypeNames = new List<string>
    {
        ECollectorType.Main.ToString(),
        ECollectorType.Static.ToString(),
        ECollectorType.Depend.ToString()
    };

    private const float MinSidebarWidth = 180f;
    private const float MaxSidebarWidth = 420f;

    private EditorWindow _window;
    private AssetCollectionSetting _setting;
    private VisualElement _root;
    private WorkflowStage _stage;
    private ProjectScanSnapshot _projectSnapshot;
    private AssetCollectionSetting _curateSetting;
    private ScanResult _curateResult;
    private bool _curatePreviewDirty;
    private CuratePanelMode _curatePanelMode = CuratePanelMode.Details;
    private SelectionType _selectionType = SelectionType.None;
    private int _selectedPackageIndex = -1;
    private int _selectedGroupIndex = -1;
    private float _curateSidebarWidth = 250f;
    private VisualElement _curateSidebar;
    private bool _draggingCurateSplitter;
    private Vector2 _splitterDragStartMouse;
    private float _splitterDragStartWidth;

    public string PanelName => "AssetsCollection";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
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
        _root?.Unbind();
        _root = null;
    }

    #region Lifecycle

    private void LoadSetting()
    {
        _setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);

        if (_setting == null)
            return;

        EnsureScanDefaults(_setting);
        if (HasPackages(_setting))
            EnterCurate(CloneSetting(_setting), false);
        else
            EnterScan();
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

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
        _curatePanelMode = CuratePanelMode.Details;
        ClearSelection();
    }

    private void EnterPreview(ProjectScanSnapshot snapshot)
    {
        _stage = WorkflowStage.Preview;
        _projectSnapshot = snapshot;
        _curateSetting = null;
        _curateResult = null;
        _curatePreviewDirty = false;
        _curatePanelMode = CuratePanelMode.Details;
        ClearSelection();
    }

    private void EnterCurate(AssetCollectionSetting candidate, bool selectFirst, ScanResult initialResult = null)
    {
        _stage = WorkflowStage.Curate;
        _curateSetting = candidate;
        _curateResult = initialResult;
        _curatePreviewDirty = initialResult == null;
        _curatePanelMode = CuratePanelMode.Details;
        EnsureSelection(selectFirst);
    }

    #endregion

    #region Toolbar

    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Scan", RunProjectScan, 72f));

        Button curate = BuildPipelineUI.ToolbarButton("Curate", EnterCurateFromToolbar, 82f);
        curate.SetEnabled(_stage != WorkflowStage.Curate || _projectSnapshot?.PreviewSetting != null || HasPackages(_setting));
        toolbar.Add(curate);

        if (_stage == WorkflowStage.Curate)
        {
            Button save = BuildPipelineUI.ToolbarButton("Save Collectors", SaveCollectors, 120f);
            save.SetEnabled(CanSaveCollectors());
            toolbar.Add(save);
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
                return "Scan Preview is read-only. Curate copies the snapshot into editable data.";
            case WorkflowStage.Curate:
                return _curatePreviewDirty
                    ? "Curate preview is outdated. Use the right-panel Preview button to refresh manually."
                    : "Curate edits stay in memory until Save Collectors.";
            default:
                return "Edit Ignore, then Scan to preview project collectors.";
        }
    }

    private void EnterCurateFromToolbar()
    {
        if (_projectSnapshot?.PreviewSetting != null)
        {
            ConfirmPreview();
            return;
        }

        if (HasPackages(_setting))
            EnterCurate(CloneSetting(_setting), false);
        else
            EnterCurate(ScriptableObject.CreateInstance<AssetCollectionSetting>(), false);

        Rebuild();
    }

    #endregion

    #region Scan Preview

    private void DrawScanStage()
    {
        ScrollView scroll = CreateScroll();
        scroll.Add(BuildPipelineUI.Header("Project Scan"));
        scroll.Add(BuildPipelineUI.SmallText("Edit Ignore first. Scan generates a read-only preview from Assets/* without writing Packages."));
        scroll.Add(CreatePersistentIgnoreEditor());
        scroll.Add(BuildPipelineUI.SmallText("Default package: " + GetDefaultPackageName()));
        _root.Add(scroll);
    }

    private void RunProjectScan()
    {
        AssetCollectionSetting preview = BuildProjectScanSetting();
        var snapshot = new ProjectScanSnapshot
        {
            PreviewSetting = preview,
            Result = CollectionScanner.Scan(preview)
        };
        EnterPreview(snapshot);
        Rebuild();
    }

    private AssetCollectionSetting BuildProjectScanSetting()
    {
        var setting = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        EnsureScanDefaults(_setting);
        setting.IgnorePatterns = CloneList(_setting.IgnorePatterns);
        var package = new AssetCollectionPackage
        {
            PackageName = GetDefaultPackageName()
        };
        setting.Packages.Add(package);

        string[] folders = AssetDatabase.GetSubFolders("Assets");
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < folders.Length; i++)
        {
            string folder = CollectorPathUtility.NormalizePath(folders[i]);
            if (HasCollectableAssets(folder, setting.IgnorePatterns))
                AddProjectScanGroup(package, folder);
        }

        AddScatteredSceneGroups(setting, package);
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

        RenderPreviewSummary(scroll);
        RenderPreviewTree(scroll, _projectSnapshot.PreviewSetting, _projectSnapshot.Result);
        RenderMessages(scroll, _projectSnapshot.Result);
        _root.Add(scroll);
    }

    private void RenderPreviewSummary(VisualElement parent)
    {
        AssetCollectionPackage package = _projectSnapshot.PreviewSetting.Packages.Count > 0
            ? _projectSnapshot.PreviewSetting.Packages[0]
            : null;
        string packageName = string.IsNullOrEmpty(package?.PackageName) ? "(unnamed package)" : package.PackageName;
        int groupCount = package?.Groups?.Count ?? 0;
        int assetCount = _projectSnapshot.Result?.Assets?.Count ?? 0;
        int bundleCount = CountDistinctBundles(_projectSnapshot.Result?.Assets);
        int warningCount = CountMessages(_projectSnapshot.Result, BuildSeverity.Warning);
        int errorCount = CountMessages(_projectSnapshot.Result, BuildSeverity.Error);

        parent.Add(BuildPipelineUI.Header("Scan Preview: " + packageName));
        parent.Add(CreateMetricStrip(packageName, groupCount, assetCount, bundleCount, warningCount, errorCount));
        parent.Add(BuildPipelineUI.SmallText("Preview is read-only. Use Confirm to start Curate from this snapshot."));
    }

    private void ConfirmPreview()
    {
        if (_projectSnapshot?.PreviewSetting == null)
            return;

        EnterCurate(CloneSetting(_projectSnapshot.PreviewSetting), true, CloneScanResult(_projectSnapshot.Result));
        Rebuild();
    }

    #endregion

    #region Curate

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
        if (_curatePanelMode == CuratePanelMode.ScanPreview)
            DrawCuratePreview(scroll);
        else
            DrawCurateDetails(scroll);
    }

    private void DrawCurateModeToolbar(VisualElement parent)
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        Button details = BuildPipelineUI.ToolbarButton("Details", () =>
        {
            _curatePanelMode = CuratePanelMode.Details;
            Rebuild();
        }, 88f);
        details.SetEnabled(_curatePanelMode != CuratePanelMode.Details);
        toolbar.Add(details);

        Button previewMode = BuildPipelineUI.ToolbarButton("Scan Preview", () =>
        {
            _curatePanelMode = CuratePanelMode.ScanPreview;
            Rebuild();
        }, 112f);
        previewMode.SetEnabled(_curatePanelMode != CuratePanelMode.ScanPreview);
        toolbar.Add(previewMode);

        toolbar.Add(BuildPipelineUI.Spacer());
        Button refresh = BuildPipelineUI.ToolbarButton(_curateResult == null ? "Preview" : "Refresh Preview", RefreshCuratePreview, 120f);
        refresh.SetEnabled(_curateSetting != null);
        toolbar.Add(refresh);
        parent.Add(toolbar);

        if (_curatePanelMode == CuratePanelMode.Details)
        {
            string status = _curateResult == null
                ? "No Curate preview yet. Use Preview when you need to inspect the collected tree."
                : _curatePreviewDirty
                    ? "Curate preview is outdated. Details mode does not rebuild the tree."
                    : "Curate preview is current. Switch to Scan Preview to inspect the tree.";
            parent.Add(BuildPipelineUI.SmallText(status));
        }
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
        VisualElement sidebar = new VisualElement
        {
            style =
            {
                width = _curateSidebarWidth,
                flexShrink = 0f,
                backgroundColor = BuildPipelineUI.SidebarBackgroundColor,
                paddingLeft = 6f,
                paddingRight = 6f,
                paddingTop = 6f
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
        buttons.Add(new Button(AddPackage) { text = "+ Package" });
        Button addGroup = new Button(AddGroup) { text = "+ Group" };
        addGroup.SetEnabled(_selectedPackageIndex >= 0);
        buttons.Add(addGroup);
        Button delete = new Button(DeleteSelection) { text = "Delete" };
        delete.SetEnabled(_selectionType != SelectionType.None);
        buttons.Add(delete);
        sidebar.Add(buttons);

        if (_curateSetting.Packages == null || _curateSetting.Packages.Count == 0)
        {
            sidebar.Add(BuildPipelineUI.SmallText("No Package. Add one or run Scan."));
            return sidebar;
        }

        for (int pi = 0; pi < _curateSetting.Packages.Count; pi++)
        {
            AssetCollectionPackage package = _curateSetting.Packages[pi];
            Label packageLabel = CreateNavLabel("□ " + GetPackageDisplayName(package), IsSelectedPackage(pi), 20f);
            int packageIndex = pi;
            packageLabel.RegisterCallback<PointerDownEvent>(evt =>
            {
                SelectPackage(packageIndex);
                _curatePanelMode = CuratePanelMode.Details;
                Rebuild();
                evt.StopPropagation();
            });
            sidebar.Add(packageLabel);

            if (package?.Groups == null)
                continue;

            for (int gi = 0; gi < package.Groups.Count; gi++)
            {
                AssetCollectionGroup group = package.Groups[gi];
                string suffix = group != null && !group.Enabled ? " [Disabled]" : string.Empty;
                Label groupLabel = CreateNavLabel("  □ " + GetGroupDisplayName(group) + suffix, IsSelectedGroup(pi, gi), 18f);
                int groupIndex = gi;
                groupLabel.RegisterCallback<PointerDownEvent>(evt =>
                {
                    SelectGroup(packageIndex, groupIndex);
                    _curatePanelMode = CuratePanelMode.Details;
                    Rebuild();
                    evt.StopPropagation();
                });
                sidebar.Add(groupLabel);
            }
        }

        return sidebar;
    }

    private void DrawCurateDetails(VisualElement parent)
    {
        switch (_selectionType)
        {
            case SelectionType.Package:
                DrawPackageEditor(parent);
                break;
            case SelectionType.Group:
                DrawGroupEditor(parent);
                break;
            default:
                parent.Add(BuildPipelineUI.SmallText("Select a Package or Group to edit Curate data."));
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
            card.Add(BuildPipelineUI.SmallText("Preview is outdated. Click Refresh Preview to update the collected tree and messages."));
        else
            card.Add(BuildPipelineUI.SmallText("Preview is current."));

        if (_curateResult == null)
        {
            card.Add(BuildPipelineUI.SmallText("No Curate preview yet. Click Preview to run CollectionScanner manually."));
            parent.Add(card);
            return;
        }

        parent.Add(card);
        RenderPreviewTree(parent, _curateSetting, _curateResult);
        RenderMessages(parent, _curateResult);
    }

    private void DrawPackageEditor(VisualElement parent)
    {
        AssetCollectionPackage package = GetSelectedPackage();
        if (package == null)
        {
            parent.Add(BuildPipelineUI.SmallText("Selected Package is missing."));
            return;
        }

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Package"));
        card.Add(CreateTextField("Package Name", package.PackageName, value =>
        {
            package.PackageName = value;
            MarkCuratePreviewDirty();
        }));

        card.Add(BuildPipelineUI.Header("Share"));
        package.SharePolicy ??= new SharePolicyConfig();
        IntegerField minRef = new IntegerField("Min Reference Count") { value = package.SharePolicy.MinReferenceCount, isDelayed = true };
        minRef.RegisterValueChangedCallback(evt =>
        {
            package.SharePolicy.MinReferenceCount = Math.Max(0, evt.newValue);
            MarkCuratePreviewDirty();
        });
        card.Add(minRef);

        LongField minSize = new LongField("Min Asset Size Bytes") { value = package.SharePolicy.MinAssetSizeBytes, isDelayed = true };
        minSize.RegisterValueChangedCallback(evt =>
        {
            package.SharePolicy.MinAssetSizeBytes = Math.Max(0L, evt.newValue);
            MarkCuratePreviewDirty();
        });
        card.Add(minSize);

        card.Add(CreateStringListEditor("No Share Patterns", package.SharePolicy.NoSharePatterns, MarkCuratePreviewDirty));
        card.Add(CreateStringListEditor("Force Share Patterns", package.SharePolicy.ForceSharePatterns, MarkCuratePreviewDirty));
        parent.Add(card);
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
        card.Add(CreateStringListEditor("Group Labels", group.Labels, MarkCuratePreviewDirty));
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

        VisualElement bottom = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f,
                marginTop = 4f
            }
        };
        bottom.Add(CreateCollectorTypePopup(collector));

        EnumField payload = new EnumField(collector.ForcePayloadKind);
        payload.style.width = 160f;
        payload.style.flexShrink = 0f;
        payload.RegisterValueChangedCallback(evt =>
        {
            collector.ForcePayloadKind = (EForcePayloadKind)evt.newValue;
            MarkCuratePreviewDirty();
        });
        bottom.Add(payload);
        row.Add(bottom);

        VisualElement rules = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                width = Length.Percent(100f),
                minWidth = 0f,
                marginTop = 4f
            }
        };
        rules.Add(CreateRulePopup("Filter", string.IsNullOrEmpty(collector.FilterRuleName) ? FYAssetSettings.RULE_COLLECT_ALL : collector.FilterRuleName, RuleDropdownHelper.GetFilterRuleNames(), value =>
        {
            collector.FilterRuleName = value;
            MarkCuratePreviewDirty();
        }));
        rules.Add(CreateRulePopup("Group", string.IsNullOrEmpty(collector.GroupRuleName) ? FYAssetSettings.RULE_GROUP_ALL : collector.GroupRuleName, RuleDropdownHelper.GetGroupRuleNames(), value =>
        {
            collector.GroupRuleName = value;
            MarkCuratePreviewDirty();
        }));
        row.Add(rules);
        row.Add(CreateStringListEditor("Ignore Patterns", collector.IgnorePatterns, MarkCuratePreviewDirty));
        return row;
    }

    private VisualElement CreateCollectorTypePopup(Collector collector)
    {
        string current = IsManualCollectorType(collector.CollectorType)
            ? collector.CollectorType.ToString()
            : ECollectorType.Main.ToString();
        var popup = new PopupField<string>("Type", ManualCollectorTypeNames, current);
        popup.style.width = 180f;
        popup.RegisterValueChangedCallback(evt =>
        {
            if (Enum.TryParse(evt.newValue, out ECollectorType parsed) && IsManualCollectorType(parsed))
            {
                collector.CollectorType = parsed;
                MarkCuratePreviewDirty();
            }
        });
        return popup;
    }

    private void MarkCuratePreviewDirty()
    {
        if (_curateSetting == null)
            return;

        _curatePreviewDirty = true;
        EnsureSelection(false);
        Rebuild();
    }

    private void RefreshCuratePreview()
    {
        if (_curateSetting == null)
            return;

        _curateResult = CollectionScanner.Scan(_curateSetting);
        _curatePreviewDirty = false;
        _curatePanelMode = CuratePanelMode.ScanPreview;
        EnsureSelection(false);
        Rebuild();
    }

    private void SaveCollectors()
    {
        if (_curateSetting != null && (_curatePreviewDirty || _curateResult == null))
        {
            _curateResult = CollectionScanner.Scan(_curateSetting);
            _curatePreviewDirty = false;
        }

        if (!CanSaveCollectors())
        {
            Rebuild();
            return;
        }

        Undo.RecordObject(_setting, "Save Collectors");
        _setting.Packages = ClonePackages(_curateSetting.Packages);
        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
        Rebuild();
    }

    private bool CanSaveCollectors()
    {
        return _stage == WorkflowStage.Curate &&
               _curateSetting != null &&
               HasPackages(_curateSetting) &&
               !HasErrors(_curateResult);
    }

    private void CancelCurate()
    {
        if (HasPackages(_setting))
            EnterCurate(CloneSetting(_setting), false);
        else
            EnterScan();
        Rebuild();
    }

    #endregion

    #region Tree Rendering

    private void RenderPreviewTree(VisualElement parent, AssetCollectionSetting setting, ScanResult result)
    {
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Collected Tree"));

        if (setting?.Packages == null || setting.Packages.Count == 0)
        {
            card.Add(BuildPipelineUI.SmallText("No Package."));
            parent.Add(card);
            return;
        }

        for (int pi = 0; pi < setting.Packages.Count; pi++)
            card.Add(CreatePackagePreview(setting.Packages[pi], result));

        parent.Add(card);
    }

    private VisualElement CreatePackagePreview(AssetCollectionPackage package, ScanResult result)
    {
        int groupCount = package?.Groups?.Count ?? 0;
        Foldout foldout = new Foldout
        {
            text = $"{GetPackageDisplayName(package)}  ({groupCount} groups)",
            value = true
        };

        if (package?.Groups != null)
        {
            for (int gi = 0; gi < package.Groups.Count; gi++)
                foldout.Add(CreateGroupPreview(package.Groups[gi], result));
        }

        return foldout;
    }

    private VisualElement CreateGroupPreview(AssetCollectionGroup group, ScanResult result)
    {
        int assetCount = CountAssetsForSourceGroup(result, group?.GroupName);
        int bundleCount = CountBundlesForSourceGroup(result, group?.GroupName);
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

        foldout.Add(BuildPipelineUI.SmallText($"{collector?.CollectorType}    {collector?.ForcePayloadKind}"));
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
        Label path = BuildPipelineUI.SmallText(asset.AssetPath);
        path.style.unityFontStyleAndWeight = FontStyle.Bold;
        if (asset.HasError)
            path.style.color = new Color(1f, 0.42f, 0.35f);
        else if (asset.HasWarning)
            path.style.color = new Color(1f, 0.74f, 0.28f);
        row.Add(path);
        row.Add(BuildPipelineUI.SmallText(BuildAssetMetaText(asset)));
        return row;
    }

    #endregion

    #region Ignore

    private VisualElement CreatePersistentIgnoreEditor()
    {
        EnsureScanDefaults(_setting);
        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Ignore"));
        card.Add(CreateStringListEditor("Patterns", _setting.IgnorePatterns, () =>
        {
            EditorUtility.SetDirty(_setting);
            AssetDatabase.SaveAssets();
            CollectorReverseIndex.Instance.MarkDirty();
        }, true));
        card.Add(BuildPipelineUI.SmallText("Saved on AssetCollectionSetting and applied before Project Scan generation."));
        return card;
    }

    #endregion

    #region Shared UI

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

    private VisualElement CreateRulePopup(string label, string current, string[] choices, Action<string> onChanged)
    {
        List<string> list = new List<string>(choices ?? Array.Empty<string>());
        if (list.Count == 0)
            list.Add(current);
        if (!string.IsNullOrEmpty(current) && !list.Contains(current))
            list.Insert(0, current);

        string selected = string.IsNullOrEmpty(current) ? list[0] : current;
        var popup = new PopupField<string>(label, list, selected);
        popup.style.width = Length.Percent(100f);
        popup.style.minWidth = 0f;
        popup.style.flexShrink = 1f;
        popup.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        return popup;
    }

    private VisualElement CreateStringListEditor(string title, List<string> values, Action onChanged, bool persistent = false)
    {
        values ??= new List<string>();
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
                if (persistent)
                    Undo.RecordObject(_setting, "Edit Ignore");
                values[index] = (evt.newValue ?? string.Empty).Trim();
                onChanged?.Invoke();
                Rebuild();
            });
            row.Add(field);
            Button remove = new Button(() =>
            {
                if (persistent)
                    Undo.RecordObject(_setting, "Edit Ignore");
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
            string value = (addField.value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
                return;
            if (persistent)
                Undo.RecordObject(_setting, "Edit Ignore");
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

    private static Label CreateNavLabel(string text, bool selected, float height)
    {
        var label = new Label(text);
        label.style.height = height;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.paddingLeft = 6f;
        label.style.color = selected ? Color.white : BuildPipelineUI.SecondaryTextColor;
        label.style.backgroundColor = selected ? BuildPipelineUI.ActiveColor : Color.clear;
        label.style.marginBottom = 2f;
        return label;
    }

    private static VisualElement CreateMetricStrip(string packageName, int groupCount, int assetCount, int bundleCount, int warningCount, int errorCount)
    {
        VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        row.style.marginBottom = 8f;
        row.Add(CreateMetric("Package", packageName));
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

    #endregion

    #region Mutations

    private void AddPackage()
    {
        _curateSetting.Packages ??= new List<AssetCollectionPackage>();
        _curateSetting.Packages.Add(new AssetCollectionPackage
        {
            PackageName = "NewPackage" + (_curateSetting.Packages.Count + 1),
            SharePolicy = new SharePolicyConfig()
        });
        SelectPackage(_curateSetting.Packages.Count - 1);
        _curatePanelMode = CuratePanelMode.Details;
        MarkCuratePreviewDirty();
    }

    private void AddGroup()
    {
        AssetCollectionPackage package = GetSelectedPackage();
        if (package == null)
            return;
        package.Groups ??= new List<AssetCollectionGroup>();
        package.Groups.Add(new AssetCollectionGroup
        {
            GroupName = "NewGroup" + (package.Groups.Count + 1),
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackTogetherByLabel
        });
        SelectGroup(_selectedPackageIndex, package.Groups.Count - 1);
        _curatePanelMode = CuratePanelMode.Details;
        MarkCuratePreviewDirty();
    }

    private void DeleteSelection()
    {
        if (_selectionType == SelectionType.Group)
        {
            AssetCollectionPackage package = GetSelectedPackage();
            if (package?.Groups != null && _selectedGroupIndex >= 0 && _selectedGroupIndex < package.Groups.Count)
                package.Groups.RemoveAt(_selectedGroupIndex);
            SelectPackage(_selectedPackageIndex);
        }
        else if (_selectionType == SelectionType.Package)
        {
            if (_curateSetting?.Packages != null && _selectedPackageIndex >= 0 && _selectedPackageIndex < _curateSetting.Packages.Count)
                _curateSetting.Packages.RemoveAt(_selectedPackageIndex);
            EnsureSelection(true);
        }

        _curatePanelMode = CuratePanelMode.Details;
        MarkCuratePreviewDirty();
    }

    private static Collector CreateEmptyCollector(ECollectPathType pathType)
    {
        return new Collector
        {
            CollectPath = string.Empty,
            CollectPathType = pathType,
            CollectorType = ECollectorType.Main,
            ForcePayloadKind = EForcePayloadKind.Auto,
            FilterRuleName = FYAssetSettings.RULE_COLLECT_ALL,
            GroupRuleName = FYAssetSettings.RULE_GROUP_ALL,
            IgnorePatterns = new List<string>()
        };
    }

    #endregion

    #region Selection

    private void EnsureSelection(bool selectFirst)
    {
        if (_curateSetting?.Packages == null || _curateSetting.Packages.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (selectFirst || _selectedPackageIndex < 0 || _selectedPackageIndex >= _curateSetting.Packages.Count)
        {
            SelectPackage(0);
            return;
        }

        if (_selectionType == SelectionType.Group)
        {
            AssetCollectionPackage package = GetSelectedPackage();
            if (package?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= package.Groups.Count)
                SelectPackage(_selectedPackageIndex);
        }
    }

    private void SelectPackage(int packageIndex)
    {
        _selectionType = SelectionType.Package;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = -1;
    }

    private void SelectGroup(int packageIndex, int groupIndex)
    {
        _selectionType = SelectionType.Group;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = groupIndex;
    }

    private void ClearSelection()
    {
        _selectionType = SelectionType.None;
        _selectedPackageIndex = -1;
        _selectedGroupIndex = -1;
    }

    private bool IsSelectedPackage(int packageIndex)
    {
        return _selectionType == SelectionType.Package && _selectedPackageIndex == packageIndex;
    }

    private bool IsSelectedGroup(int packageIndex, int groupIndex)
    {
        return _selectionType == SelectionType.Group &&
               _selectedPackageIndex == packageIndex &&
               _selectedGroupIndex == groupIndex;
    }

    private AssetCollectionPackage GetSelectedPackage()
    {
        if (_curateSetting?.Packages == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _curateSetting.Packages.Count)
            return null;
        return _curateSetting.Packages[_selectedPackageIndex];
    }

    private AssetCollectionGroup GetSelectedGroup()
    {
        AssetCollectionPackage package = GetSelectedPackage();
        if (package?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= package.Groups.Count)
            return null;
        return package.Groups[_selectedGroupIndex];
    }

    #endregion

    #region Helpers

    private static string GetDefaultPackageName()
    {
        string projectName = FYAssetSettings.Instance.ProjectName;
        return string.IsNullOrWhiteSpace(projectName) ? "Default" : projectName.Trim();
    }

    private static void AddProjectScanGroup(AssetCollectionPackage package, string folder)
    {
        string groupName = GetLastPathSegment(folder);
        var group = new AssetCollectionGroup
        {
            GroupName = groupName,
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackTogetherByLabel
        };
        group.Collectors.Add(CreateFolderCollector(folder));
        package.Groups.Add(group);
    }

    private static void AddScatteredSceneGroups(AssetCollectionSetting setting, AssetCollectionPackage package)
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        if (guids == null)
            return;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (string.IsNullOrEmpty(assetPath))
                continue;
            if (CollectorPathUtility.MatchesIgnorePattern(assetPath, "Assets", setting.IgnorePatterns))
                continue;
            if (IsOwnedByExistingCollector(package, assetPath))
                continue;

            string groupName = GetSceneGroupName(assetPath);
            AssetCollectionGroup group = FindOrCreateGroup(package, groupName);
            group.Collectors.Add(CreateFileCollector(assetPath));
        }
    }

    private static bool HasCollectableAssets(string folder, List<string> ignorePatterns)
    {
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
            if (!CollectorPathUtility.MatchesIgnorePattern(assetPath, "Assets", ignorePatterns))
                return true;
        }

        return false;
    }

    private static bool IsOwnedByExistingCollector(AssetCollectionPackage package, string assetPath)
    {
        if (package?.Groups == null)
            return false;

        string normalizedAsset = CollectorPathUtility.NormalizePath(assetPath);
        for (int gi = 0; gi < package.Groups.Count; gi++)
        {
            AssetCollectionGroup group = package.Groups[gi];
            if (group?.Collectors == null)
                continue;

            for (int ci = 0; ci < group.Collectors.Count; ci++)
            {
                Collector collector = group.Collectors[ci];
                string collectPath = CollectorPathUtility.NormalizePath(collector?.CollectPath);
                if (string.IsNullOrEmpty(collectPath))
                    continue;

                if (collector.CollectPathType == ECollectPathType.Folder &&
                    CollectorPathUtility.IsPathContained(collectPath, normalizedAsset))
                {
                    return true;
                }

                if (collector.CollectPathType == ECollectPathType.File &&
                    string.Equals(collectPath, normalizedAsset, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static AssetCollectionGroup FindOrCreateGroup(AssetCollectionPackage package, string groupName)
    {
        for (int i = 0; i < package.Groups.Count; i++)
        {
            AssetCollectionGroup group = package.Groups[i];
            if (group != null && string.Equals(group.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                return group;
        }

        var created = new AssetCollectionGroup
        {
            GroupName = groupName,
            Enabled = true,
            BundlePackingMode = BundlePackingMode.PackSeparately
        };
        package.Groups.Add(created);
        return created;
    }

    private static Collector CreateFolderCollector(string path)
    {
        return new Collector
        {
            CollectPath = path,
            CollectPathType = ECollectPathType.Folder,
            CollectorType = ECollectorType.Main,
            ForcePayloadKind = EForcePayloadKind.Auto,
            FilterRuleName = FYAssetSettings.RULE_COLLECT_ALL,
            GroupRuleName = FYAssetSettings.RULE_GROUP_ALL,
            IgnorePatterns = new List<string>()
        };
    }

    private static Collector CreateFileCollector(string path)
    {
        return new Collector
        {
            CollectPath = path,
            CollectPathType = ECollectPathType.File,
            CollectorType = ECollectorType.Main,
            ForcePayloadKind = EForcePayloadKind.Scene,
            FilterRuleName = FYAssetSettings.RULE_COLLECT_ALL,
            GroupRuleName = FYAssetSettings.RULE_GROUP_ALL,
            IgnorePatterns = new List<string>()
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

    private static void EnsureScanDefaults(AssetCollectionSetting setting)
    {
        if (setting != null && setting.IgnorePatterns == null)
            setting.IgnorePatterns = AssetCollectionSetting.CreateDefaultIgnorePatterns();
    }

    private static bool HasPackages(AssetCollectionSetting setting)
    {
        return setting?.Packages != null && setting.Packages.Count > 0;
    }

    private static bool HasErrors(ScanResult result)
    {
        if (result?.Messages != null)
        {
            for (int i = 0; i < result.Messages.Count; i++)
            {
                if (result.Messages[i].Severity == BuildSeverity.Error)
                    return true;
            }
        }

        if (result?.Assets != null)
        {
            for (int i = 0; i < result.Assets.Count; i++)
            {
                if (result.Assets[i].HasError)
                    return true;
            }
        }

        return false;
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
            if (!string.IsNullOrEmpty(assets[i].BundleName))
                bundles.Add(assets[i].BundleName);
        }
        return bundles.Count;
    }

    private static int CountAssetsForSourceGroup(ScanResult result, string groupName)
    {
        return GetAssetsForSourceGroup(result, groupName).Count;
    }

    private static int CountBundlesForSourceGroup(ScanResult result, string groupName)
    {
        HashSet<string> bundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<CollectedAssetInfo> assets = GetAssetsForSourceGroup(result, groupName);
        for (int i = 0; i < assets.Count; i++)
        {
            if (!string.IsNullOrEmpty(assets[i].BundleName))
                bundles.Add(assets[i].BundleName);
        }
        return bundles.Count;
    }

    private static List<CollectedAssetInfo> GetAssetsForSourceGroup(ScanResult result, string groupName)
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
            if (string.Equals(asset.SourceGroupName, groupName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(CollectorPathUtility.NormalizePath(asset.SourceCollectorPath), normalizedCollector, StringComparison.OrdinalIgnoreCase))
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
            string key = string.IsNullOrEmpty(assets[i].BundleName) ? "(invalid bundle)" : assets[i].BundleName;
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
        return $"{state}    Group: {asset.GroupName}    Address: {asset.Address}    Type: {asset.PrimaryType}    Class: {asset.Classification}";
    }

    private static string GetPackageDisplayName(AssetCollectionPackage package)
    {
        return string.IsNullOrEmpty(package?.PackageName) ? "(unnamed package)" : package.PackageName;
    }

    private static string GetGroupDisplayName(AssetCollectionGroup group)
    {
        return string.IsNullOrEmpty(group?.GroupName) ? "(unnamed group)" : group.GroupName;
    }

    private static bool IsManualCollectorType(ECollectorType type)
    {
        for (int i = 0; i < ManualCollectorTypes.Length; i++)
        {
            if (ManualCollectorTypes[i] == type)
                return true;
        }
        return false;
    }

    private static string PickCollectPath(bool isFile)
    {
        string absolutePath = isFile
            ? EditorUtility.OpenFilePanel("Select Collect File", Application.dataPath, string.Empty)
            : EditorUtility.OpenFolderPanel("Select Collect Folder", Application.dataPath, string.Empty);

        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;

        string projectDataPath = Application.dataPath.Replace('\\', '/');
        string normalizedAbsolute = absolutePath.Replace('\\', '/');
        if (!normalizedAbsolute.StartsWith(projectDataPath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return "Assets" + normalizedAbsolute.Substring(projectDataPath.Length);
    }

    private static AssetCollectionSetting CloneSetting(AssetCollectionSetting source)
    {
        var clone = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        clone.IgnorePatterns = CloneList(source?.IgnorePatterns);
        clone.Packages = ClonePackages(source?.Packages);
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

    private static List<AssetCollectionPackage> ClonePackages(List<AssetCollectionPackage> source)
    {
        var packages = new List<AssetCollectionPackage>();
        if (source == null)
            return packages;

        for (int i = 0; i < source.Count; i++)
        {
            AssetCollectionPackage package = source[i];
            if (package == null)
                continue;
            packages.Add(new AssetCollectionPackage
            {
                PackageName = package.PackageName,
                SharePolicy = CloneSharePolicy(package.SharePolicy),
                Groups = CloneGroups(package.Groups)
            });
        }
        return packages;
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
                Labels = CloneList(group.Labels),
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
                CollectPathType = collector.CollectPathType,
                CollectorType = collector.CollectorType,
                ForcePayloadKind = collector.ForcePayloadKind,
                FilterRuleName = string.IsNullOrEmpty(collector.FilterRuleName) ? FYAssetSettings.RULE_COLLECT_ALL : collector.FilterRuleName,
                GroupRuleName = string.IsNullOrEmpty(collector.GroupRuleName) ? FYAssetSettings.RULE_GROUP_ALL : collector.GroupRuleName,
                IgnorePatterns = CloneList(collector.IgnorePatterns)
            });
        }
        return collectors;
    }

    private static SharePolicyConfig CloneSharePolicy(SharePolicyConfig source)
    {
        if (source == null)
            return new SharePolicyConfig();

        return new SharePolicyConfig
        {
            MinReferenceCount = source.MinReferenceCount,
            MinAssetSizeBytes = source.MinAssetSizeBytes,
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
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath));
        panel.Add(new Button(CreateAssetCollectionSetting) { text = "Create" });
    }

    private void CreateAssetCollectionSetting()
    {
        BuildPipelineUI.EnsureAssetParentFolder(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);
        AssetCollectionSetting newSetting = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        AssetDatabase.CreateAsset(newSetting, FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
        Rebuild();
    }

    #endregion

    private sealed class ProjectScanSnapshot
    {
        public AssetCollectionSetting PreviewSetting;
        public ScanResult Result;
    }
}

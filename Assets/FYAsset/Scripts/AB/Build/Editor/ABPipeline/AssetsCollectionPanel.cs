using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Collection 编辑器：左侧编辑 Group/Collector/Asset，右侧显示 Details 与 Scan Preview，Save/Cancel 提交或丢弃工作副本。
/// </summary>
/// <remarks>
/// 配置层级只有 Setting -> Group -> Collector；资源 Address/Labels 只在 Details 中编辑。
/// </remarks>
public class AssetsCollectionPanel : IBuildPipelinePanel
{
    private enum RightPanelSection
    {
        Details,
        Scan,
        Settings
    }

    private const float MinSidebarWidth = 220f;
    private const float MaxSidebarWidth = 560f;
    private const float SidebarCharWidth = 8f;
    private const float SidebarPaddingWidth = 76f;
    private const float AssetDetailFontSize = 14f;

    private EditorWindow _window;
    private AssetCollectionSetting _setting;
    private VisualElement _root;
    private AssetCollectionSetting _curateSetting;
    private ScanResult _curateResult;
    private AssetCollectionSetting _scanPreviewSetting;
    private ScanResult _scanPreviewResult;
    private bool _curatePreviewDirty;
    private bool _curateHasUnsavedChanges;
    private int _selectedGroupIndex = -1;
    private string _selectedAssetGuid;
    private int _selectionAnchorIndex = -1;
    private string _selectionAnchorAssetGuid;
    private readonly HashSet<int> _selectedGroupIndices = new HashSet<int>();
    private readonly HashSet<string> _selectedAssetGuids = new HashSet<string>(StringComparer.Ordinal);
    private float _curateSidebarWidth = 250f;
    private VisualElement _curateSidebar;
    private ScrollView _curateSidebarTree;
    private Vector2 _curateSidebarScrollOffset;
    private readonly HashSet<string> _collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPreviewNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _draggingCurateSplitter;
    private bool _suppressExternalCollectorChanged;
    private Vector2 _splitterDragStartMouse;
    private float _splitterDragStartWidth;
    private List<BuildMessage> _validationMessages = new List<BuildMessage>();
    private RightPanelSection _rightPanelSection = RightPanelSection.Details;

    public string PanelName => "Collection";
    public bool HasUnsavedChanges => _curateHasUnsavedChanges;
    private bool HasSelection => _selectedGroupIndices.Count > 0 || _selectedAssetGuids.Count > 0;

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
        _curateSetting = CloneSetting(_setting);
        _curateResult = CollectionScanner.Scan(_curateSetting);
        _curatePreviewDirty = false;
        _curateHasUnsavedChanges = false;
        _validationMessages = new List<BuildMessage>();
        _scanPreviewSetting = BuildProjectScanSetting();
        _scanPreviewResult = CollectionScanner.Scan(_scanPreviewSetting);
        if (!preserveExpansionState)
        {
            ResetCurateSidebarScroll();
            CollapseAllGroups(_curateSetting);
        }
        EnsureSelection(true);
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
        DrawCollectionStage();
    }

    private void OnExternalCollectorChanged()
    {
        if (_root == null || _suppressExternalCollectorChanged)
            return;

        if (_curateSetting != null && _curateHasUnsavedChanges)
        {
            RescanCurate(false, true);
            return;
        }

        LoadSetting(true);
        Rebuild();
    }

    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        Button save = BuildPipelineUI.ToolbarButton("Save", SaveCollectors, 64f);
        save.SetEnabled(_curateSetting != null);
        toolbar.Add(save);
        toolbar.Add(BuildPipelineUI.ToolbarButton("Cancel", CancelCurate, 72f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(GetStageHint()));
        _root.Add(toolbar);
    }

    private string GetStageHint()
    {
        if (_curatePreviewDirty)
            return "Preview outdated";
        return _curateHasUnsavedChanges ? "Unsaved changes" : "Saved";
    }

    private void DrawCollectionStage()
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

        VisualElement sectionToolbar = BuildPipelineUI.Toolbar();
        sectionToolbar.Add(CreateRightPanelButton("Details", RightPanelSection.Details, 82f));
        sectionToolbar.Add(CreateRightPanelButton("Scan", RightPanelSection.Scan, 64f));
        sectionToolbar.Add(CreateRightPanelButton("Settings", RightPanelSection.Settings, 82f));
        detail.Add(sectionToolbar);

        ScrollView scroll = CreateScroll();
        detail.Add(scroll);

        if (_curateSetting == null)
        {
            scroll.Add(BuildPipelineUI.SmallText("No Collection setting."));
            return;
        }

        switch (_rightPanelSection)
        {
            case RightPanelSection.Scan:
                DrawScanSection(scroll);
                break;
            case RightPanelSection.Settings:
                DrawSettingsSection(scroll);
                break;
            default:
                DrawDetailsSection(scroll);
                break;
        }
    }

    private void DrawDetailsSection(VisualElement parent)
    {
        DrawCurateDetails(parent);
    }

    private void DrawScanSection(VisualElement parent)
    {
        DrawScanPreview(parent);
    }

    private void DrawSettingsSection(VisualElement parent)
    {
        RenderValidationMessages(parent);
        parent.Add(CreateCollectionSettingsEditor());
    }

    private Button CreateRightPanelButton(string text, RightPanelSection section, float width)
    {
        Button button = BuildPipelineUI.ToolbarButton(text, () =>
        {
            _rightPanelSection = section;
            Rebuild();
        }, width);
        if (_rightPanelSection == section)
            button.style.backgroundColor = BuildPipelineUI.ActiveColor;
        return button;
    }

    private VisualElement CreateCollectionSettingsEditor()
    {
        if (_curateSetting == null)
            return new VisualElement();

        EnsureScanDefaults(_curateSetting);
        VisualElement settings = new VisualElement();
        settings.Add(BuildPipelineUI.Header("Ignore"));
        settings.Add(CreateStringListEditor("Patterns", _curateSetting.IgnorePatterns));

        settings.Add(BuildPipelineUI.Header("Raw File Rules"));
        settings.Add(CreateStringListEditor("Patterns", _curateSetting.RawFileRules.Patterns));

        settings.Add(BuildPipelineUI.Header("Share Policy"));
        settings.Add(CreateStringListEditor("Force Share Patterns", _curateSetting.SharePolicy.ForceSharePatterns));
        settings.Add(CreateStringListEditor("No Share Patterns", _curateSetting.SharePolicy.NoSharePatterns));
        return settings;
    }

    private VisualElement CreateStringListEditor(string title, List<string> values)
    {
        values ??= new List<string>();
        VisualElement box = new VisualElement
        {
            style =
            {
                marginTop = 4f,
                marginBottom = 8f,
                width = Length.Percent(100f),
                minWidth = 0f
            }
        };
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
                    minWidth = 0f,
                    marginBottom = 3f
                }
            };
            TextField field = new TextField { value = values[i], isDelayed = true };
            field.style.width = 0f;
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.style.flexBasis = 0f;
            field.style.minWidth = 0f;
            field.style.marginRight = 4f;
            field.RegisterValueChangedCallback(evt =>
            {
                values[index] = evt.newValue ?? string.Empty;
                MarkCuratePreviewDirty();
            });
            row.Add(field);

            Button remove = new Button(() =>
            {
                values.RemoveAt(index);
                MarkCuratePreviewDirty();
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
            values.Add(value);
            MarkCuratePreviewDirty();
        }) { text = "Add" };
        add.style.width = 72f;
        add.style.flexShrink = 0f;
        addRow.Add(add);
        box.Add(addRow);
        return box;
    }

    private AssetCollectionSetting BuildProjectScanSetting()
    {
        var setting = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        EnsureScanDefaults(_curateSetting);
        setting.IgnorePatterns = CloneList(_curateSetting?.IgnorePatterns);
        setting.RawFileRules = CloneRawFileRules(_curateSetting?.RawFileRules);
        setting.SharePolicy = CloneSharePolicy(_curateSetting?.SharePolicy);

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
        delete.SetEnabled(HasSelection);
        buttons.Add(delete);
        sidebar.Add(buttons);

        if (_curateSetting.Groups == null || _curateSetting.Groups.Count == 0)
        {
            sidebar.Add(BuildPipelineUI.SmallText("No Group. Add one or refresh Scan Preview."));
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
            RegisterGroupContextMenu(groupLabel, gi);
            int groupIndex = gi;
            groupLabel.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                bool extend = evt.ctrlKey || evt.commandKey;
                if (evt.shiftKey)
                    SelectGroupRange(groupIndex, extend);
                else
                    SelectGroup(groupIndex, extend);

                if (!extend && !evt.shiftKey)
                    ToggleCollapsed(_collapsedGroups, groupKey);
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
                RegisterAssetContextMenu(assetLabel, assetGuid, asset);
                assetLabel.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                        return;

                    SelectAsset(groupIndex, assetGuid, evt.ctrlKey || evt.commandKey, evt.shiftKey);
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
        _collapsedGroups.Clear();
        _expandedPreviewNodes.Clear();
    }

    private void CollapseAllGroups(AssetCollectionSetting setting)
    {
        if (setting?.Groups == null)
            return;

        for (int i = 0; i < setting.Groups.Count; i++)
            _collapsedGroups.Add(GetGroupNavKey(i, setting.Groups[i]));
    }

    private void DrawCurateDetails(VisualElement parent)
    {
        if (_selectedAssetGuids.Count == 1 && _selectedGroupIndices.Count == 0 && !string.IsNullOrEmpty(_selectedAssetGuid))
        {
            DrawAssetEditor(parent, _selectedAssetGuid);
            return;
        }

        if (_selectedGroupIndices.Count == 1 && _selectedAssetGuids.Count == 0)
        {
            DrawGroupEditor(parent);
            return;
        }

        if (_selectedGroupIndices.Count > 0 || _selectedAssetGuids.Count > 0)
        {
            parent.Add(BuildPipelineUI.SmallText(
                $"Selected Groups: {_selectedGroupIndices.Count}    Selected Assets: {_selectedAssetGuids.Count}"));
            return;
        }

        parent.Add(BuildPipelineUI.SmallText("Select a Group or Asset to edit Collection data."));
    }

    private void DrawScanPreview(VisualElement parent)
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
        header.Add(BuildPipelineUI.Header("Scan Preview"));
        header.Add(BuildPipelineUI.Spacer());
        header.Add(BuildPipelineUI.ToolbarButton("Refresh", RefreshScanPreview, 76f));
        Button apply = BuildPipelineUI.ToolbarButton("Apply", ApplyScanPreview, 64f);
        apply.SetEnabled(_scanPreviewSetting != null);
        header.Add(apply);
        card.Add(header);

        if (_curatePreviewDirty)
            card.Add(BuildPipelineUI.SmallText("Preview outdated"));

        if (_scanPreviewResult == null || _scanPreviewSetting == null)
        {
            card.Add(BuildPipelineUI.SmallText("No scan preview."));
            parent.Add(card);
            return;
        }

        int groupCount = _scanPreviewSetting.Groups?.Count ?? 0;
        int assetCount = _scanPreviewResult.Assets?.Count ?? 0;
        int bundleCount = CountDistinctBundles(_scanPreviewResult.Assets);
        int warningCount = CountMessages(_scanPreviewResult, BuildSeverity.Warning);
        int errorCount = CountMessages(_scanPreviewResult, BuildSeverity.Error);
        card.Add(CreateMetricStrip(groupCount, assetCount, bundleCount, warningCount, errorCount));
        card.style.borderBottomWidth = 0f;
        card.style.marginBottom = 0f;
        parent.Add(card);
        RenderPreviewTree(parent, _scanPreviewSetting, _scanPreviewResult);
        RenderMessages(parent, _scanPreviewResult);
    }

    private void RefreshScanPreview()
    {
        if (_curateSetting == null)
            return;

        _scanPreviewSetting = BuildProjectScanSetting();
        _scanPreviewResult = CollectionScanner.Scan(_scanPreviewSetting);
        _curatePreviewDirty = false;
        Rebuild();
    }

    private void ApplyScanPreview()
    {
        if (_curateSetting == null || _scanPreviewSetting == null)
            return;

        HashSet<string> scannedGuids = new HashSet<string>(StringComparer.Ordinal);
        if (_scanPreviewResult?.Assets != null)
        {
            for (int i = 0; i < _scanPreviewResult.Assets.Count; i++)
            {
                string guid = _scanPreviewResult.Assets[i]?.AssetGUID;
                if (!string.IsNullOrEmpty(guid))
                    scannedGuids.Add(guid);
            }
        }

        _curateSetting.Groups = CloneGroups(_scanPreviewSetting.Groups);
        _curateSetting.AssetAddressEntries = CloneAssetAddressEntriesForGuids(_curateSetting.AssetAddressEntries, scannedGuids);
        RescanCurate(true, true);
        _scanPreviewSetting = CloneSetting(_curateSetting);
        _scanPreviewResult = CloneScanResult(_curateResult);
        _curatePreviewDirty = false;
        Rebuild();
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
        card.style.borderBottomWidth = 0f;
        card.style.borderBottomColor = Color.clear;
        card.style.marginBottom = 0f;
        parent.Add(card);

        DrawCollectorsEditor(parent, group);
    }

    private void DrawCollectorsEditor(VisualElement parent, AssetCollectionGroup group)
    {
        group.Collectors ??= new List<Collector>();

        VisualElement card = BuildPipelineUI.Card();
        card.style.borderBottomWidth = 0f;
        card.style.borderBottomColor = Color.clear;
        card.style.marginBottom = 0f;
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
        row.style.borderBottomWidth = 0f;
        row.style.borderBottomColor = Color.clear;
        row.style.marginBottom = 0f;
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

        RescanCurate(false, true);
    }

    private void RescanCurate(bool selectFirst, bool markUnsaved = true)
    {
        if (_curateSetting == null)
            return;

        _curateResult = CollectionScanner.Scan(_curateSetting);
        _curatePreviewDirty = true;
        if (markUnsaved)
            _curateHasUnsavedChanges = true;
        EnsureSelection(selectFirst);
        Rebuild();
    }

    private void SaveCollectors()
    {
        if (_curateSetting == null)
            return;

        if (NormalizeSceneCollectors(_curateSetting))
            _curatePreviewDirty = true;

        if (_curateResult == null)
            _curateResult = CollectionScanner.Scan(_curateSetting);

        // 保存前强制校验：Error 阻断写入，避免把非法配置落到磁盘资产上。
        _validationMessages = AssetCollectionSettingValidator.Validate(_curateSetting);
        if (HasValidationError())
        {
            Rebuild();
            return;
        }

        Undo.RecordObject(_setting, "Save Collectors");
        EnsureScanDefaults(_curateSetting);
        _setting.IgnorePatterns = CloneList(_curateSetting.IgnorePatterns);
        _setting.Groups = CloneGroups(_curateSetting.Groups);
        _setting.AssetAddressEntries = CloneAssetAddressEntries(_curateSetting.AssetAddressEntries);
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
        return _curateSetting != null;
    }

    private void CancelCurate()
    {
        LoadSetting(true);
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
            card.Add(CreateGroupPreview(setting.Groups[gi], result, gi));

        parent.Add(card);
    }

    private VisualElement CreateGroupPreview(AssetCollectionGroup group, ScanResult result, int groupIndex)
    {
        int assetCount = CountAssetsForGroup(result, group?.GroupName);
        int bundleCount = CountBundlesForGroup(result, group?.GroupName);
        string disabled = group != null && !group.Enabled ? " [Disabled]" : string.Empty;
        string nodeKey = "group:" + groupIndex;
        Foldout foldout = CreatePreviewFoldout(nodeKey, $"{GetGroupDisplayName(group)}{disabled}  {assetCount} assets / {bundleCount} bundles");

        if (group?.Collectors != null)
        {
            for (int ci = 0; ci < group.Collectors.Count; ci++)
                foldout.Add(CreateCollectorPreview(group, group.Collectors[ci], result, groupIndex, ci));
        }

        return foldout;
    }

    private VisualElement CreateCollectorPreview(AssetCollectionGroup group, Collector collector, ScanResult result, int groupIndex, int collectorIndex)
    {
        List<CollectedAssetInfo> assets = GetAssetsForCollector(result, group?.GroupName, collector?.CollectPath);
        string nodeKey = $"collector:{groupIndex}:{collectorIndex}";
        Foldout foldout = CreatePreviewFoldout(nodeKey, $"{(collector?.CollectPathType == ECollectPathType.File ? "[File]" : "[Folder]")} {collector?.CollectPath}  ({assets.Count})");

        Dictionary<string, List<CollectedAssetInfo>> bundles = BucketByBundle(assets);
        List<string> bundleNames = new List<string>(bundles.Keys);
        bundleNames.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bundleNames.Count; i++)
            foldout.Add(CreateBundlePreview(bundleNames[i], bundles[bundleNames[i]], i, groupIndex, collectorIndex));

        return foldout;
    }

    private VisualElement CreateBundlePreview(string bundleName, List<CollectedAssetInfo> assets, int index, int groupIndex, int collectorIndex)
    {
        string nodeKey = $"bundle:{groupIndex}:{collectorIndex}:{bundleName}";
        Foldout foldout = CreatePreviewFoldout(nodeKey, $"{bundleName}  ({assets.Count})");
        foldout.style.borderLeftWidth = 4f;
        foldout.style.borderLeftColor = GetBundleColor(index);
        foldout.style.marginLeft = 12f;
        foldout.style.paddingLeft = 6f;

        assets.Sort((left, right) => string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < assets.Count; i++)
            foldout.Add(CreateAssetRow(assets[i]));

        return foldout;
    }

    private Foldout CreatePreviewFoldout(string key, string text)
    {
        Foldout foldout = new Foldout
        {
            text = text,
            value = _expandedPreviewNodes.Contains(key)
        };
        foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
                _expandedPreviewNodes.Add(key);
            else
                _expandedPreviewNodes.Remove(key);
        });
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
        if (IsSelectedAsset(asset.AssetGUID))
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
            if (evt.button != 0)
            {
                evt.StopPropagation();
                return;
            }

            SelectAsset(FindGroupIndex(asset.SourceGroupName), asset.AssetGUID, evt.ctrlKey || evt.commandKey, evt.shiftKey);
            Rebuild();
            evt.StopPropagation();
        });
        return row;
    }

    private void DrawAssetEditor(VisualElement parent, string assetGuid)
    {
        CollectedAssetInfo preview = FindPreviewAsset(assetGuid);
        string assetPath = preview != null ? preview.AssetPath : AssetDatabase.GUIDToAssetPath(assetGuid);
        AssetAddressEntry entry = _curateSetting.FindAssetAddressEntry(assetGuid);
        string address = preview != null
            ? preview.Address
            : AssetAddressGenerator.GenerateAddress(assetPath, "Unknown", AssetAddressStyle.LongAssetPath);

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Asset"));

        Label path = BuildPipelineUI.SmallText(string.IsNullOrEmpty(assetPath) ? "(missing asset)" : assetPath);
        path.style.fontSize = AssetDetailFontSize;
        path.style.whiteSpace = WhiteSpace.Normal;
        card.Add(path);

        if (preview != null)
        {
            AddDetailLabel(card, "Content", preview.ContentName);
            AddDetailLabel(card, "Group", preview.GroupName);
        }

        AddDetailLabel(card, "GUID", assetGuid);

        TextField addressField = CreateTextField("Address", address, value =>
        {
            UpdateAssetAddressEntry(assetGuid, value, _curateSetting.FindAssetAddressEntry(assetGuid)?.Labels);
            MarkCuratePreviewDirty();
        });
        addressField.style.fontSize = AssetDetailFontSize;
        addressField.style.minHeight = 28f;
        card.Add(addressField);

        TextField labelsField = CreateTextField("Labels", JoinLabelList(entry?.Labels), value =>
        {
            UpdateAssetAddressEntry(assetGuid, _curateSetting.FindAssetAddressEntry(assetGuid)?.Address, NormalizeLabels(value));
            MarkCuratePreviewDirty();
        });
        labelsField.style.fontSize = AssetDetailFontSize;
        labelsField.style.minHeight = 28f;
        labelsField.tooltip = "Comma, semicolon, or newline separated";
        card.Add(labelsField);
        card.style.borderBottomWidth = 0f;
        card.style.borderBottomColor = Color.clear;
        card.style.marginBottom = 0f;
        parent.Add(card);
    }

    private static void AddDetailLabel(VisualElement parent, string title, string value)
    {
        Label label = BuildPipelineUI.SmallText(string.Concat(title, ": ", value ?? string.Empty));
        label.style.fontSize = AssetDetailFontSize;
        label.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(label);
    }

    private void RegisterAssetContextMenu(VisualElement element, string assetGuid, CollectedAssetInfo preview)
    {
        element.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            bool useSelection = IsSelectedAsset(assetGuid);
            string fallbackGuid = useSelection ? null : assetGuid;
            CollectedAssetInfo fallbackPreview = useSelection ? null : preview;
            evt.menu.AppendAction("Apply Short Name", _ => ApplyAddressStyleToSelection(AssetAddressStyle.ShortName, fallbackGuid, fallbackPreview));
            evt.menu.AppendAction("Apply Long Path", _ => ApplyAddressStyleToSelection(AssetAddressStyle.LongAssetPath, fallbackGuid, fallbackPreview));
        }));
    }

    private void RegisterGroupContextMenu(VisualElement element, int groupIndex)
    {
        element.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            bool useSelection = IsSelectedGroup(groupIndex);
            CollectedAssetInfo preview = useSelection ? null : FindPreviewAssetForGroup(groupIndex);
            string fallbackGuid = preview?.AssetGUID;
            evt.menu.AppendAction("Apply Short Name", _ => ApplyAddressStyleToSelection(AssetAddressStyle.ShortName, fallbackGuid, preview));
            evt.menu.AppendAction("Apply Long Path", _ => ApplyAddressStyleToSelection(AssetAddressStyle.LongAssetPath, fallbackGuid, preview));
        }));
    }

    private static ScrollView CreateScroll()
    {
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.paddingLeft = 8f;
        scroll.style.paddingRight = 8f;
        return scroll;
    }

    private TextField CreateTextField(string label, string value, Action<string> onChanged)
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

    private static Label CreateNavLabel(string text, bool selected, float height)
    {
        var label = new Label(text);
        label.style.fontSize = 12f;
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
        SelectGroup(_curateSetting.Groups.Count - 1, false);
        MarkCuratePreviewDirty();
    }

    private void DeleteSelection()
    {
        if (_curateSetting == null)
            return;

        HashSet<int> groupIndices = new HashSet<int>(_selectedGroupIndices);
        List<string> assetGuids = new List<string>(_selectedAssetGuids);
        for (int i = 0; i < assetGuids.Count; i++)
        {
            CollectedAssetInfo asset = FindPreviewAsset(assetGuids[i]);
            int groupIndex = FindGroupIndex(asset?.SourceGroupName);
            if (asset != null && !groupIndices.Contains(groupIndex))
                RemoveAssetFromCurate(asset);
        }

        List<int> sortedGroups = new List<int>(groupIndices);
        sortedGroups.Sort();
        for (int i = sortedGroups.Count - 1; i >= 0; i--)
        {
            int groupIndex = sortedGroups[i];
            if (groupIndex >= 0 && groupIndex < _curateSetting.Groups.Count)
                _curateSetting.Groups.RemoveAt(groupIndex);
        }

        ClearSelection();
        RescanCurate(false, true);
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

        if (IsIgnoredInCurate(assetPath))
            RemoveIgnoreFromCurate(assetPath);

        if (!IsCoveredByCurateCollector(assetPath))
        {
            group.Collectors ??= new List<Collector>();
            group.Collectors.Add(CreateFileCollectorForCurate(assetPath));
        }

        RescanCurate(false, true);
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

        AddIgnoreToCurate(asset.AssetPath);
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

    private bool IsIgnoredInCurate(string assetPath)
    {
        return _curateSetting != null && GitIgnoreMatcher.Evaluate(assetPath, _curateSetting.GetEffectiveIgnorePatterns());
    }

    private bool AddIgnoreToCurate(string assetPath)
    {
        if (_curateSetting == null || string.IsNullOrEmpty(assetPath))
            return false;

        _curateSetting.IgnorePatterns ??= new List<string>();
        if (IsIgnoredInCurate(assetPath))
            return false;

        _curateSetting.IgnorePatterns.Add(CollectorPathUtility.NormalizePath(assetPath));
        return true;
    }

    private bool RemoveIgnoreFromCurate(string assetPath)
    {
        if (_curateSetting?.IgnorePatterns == null)
            return false;

        bool removed = false;
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        for (int i = _curateSetting.IgnorePatterns.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_curateSetting.IgnorePatterns[i], normalized, StringComparison.OrdinalIgnoreCase))
                continue;
            _curateSetting.IgnorePatterns.RemoveAt(i);
            removed = true;
        }
        return removed;
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

        _selectedGroupIndices.RemoveWhere(index => index < 0 || index >= _curateSetting.Groups.Count);
        _selectedAssetGuids.RemoveWhere(guid => FindPreviewAsset(guid) == null);
        if (selectFirst || (_selectedGroupIndices.Count == 0 && _selectedAssetGuids.Count == 0))
            SelectGroup(0, false);
        else if (!string.IsNullOrEmpty(_selectedAssetGuid) && !_selectedAssetGuids.Contains(_selectedAssetGuid))
            _selectedAssetGuid = null;
    }

    private void SelectGroup(int groupIndex, bool extend)
    {
        if (groupIndex < 0 || groupIndex >= (_curateSetting?.Groups?.Count ?? 0))
            return;

        if (!extend)
        {
            _selectedGroupIndices.Clear();
            _selectedAssetGuids.Clear();
        }

        if (extend && !_selectedGroupIndices.Add(groupIndex))
            _selectedGroupIndices.Remove(groupIndex);
        else
            _selectedGroupIndices.Add(groupIndex);

        _selectedGroupIndex = groupIndex;
        _selectedAssetGuid = null;
        _selectionAnchorIndex = groupIndex;
        _selectionAnchorAssetGuid = null;
    }

    private void SelectGroupRange(int groupIndex, bool extend)
    {
        if (groupIndex < 0 || groupIndex >= (_curateSetting?.Groups?.Count ?? 0))
            return;

        if (!extend)
        {
            _selectedGroupIndices.Clear();
            _selectedAssetGuids.Clear();
        }

        int start = _selectionAnchorIndex >= 0 ? Mathf.Min(_selectionAnchorIndex, groupIndex) : groupIndex;
        int end = _selectionAnchorIndex >= 0 ? Mathf.Max(_selectionAnchorIndex, groupIndex) : groupIndex;
        for (int i = start; i <= end; i++)
            _selectedGroupIndices.Add(i);

        _selectedGroupIndex = groupIndex;
        _selectedAssetGuid = null;
    }

    private void SelectAsset(int groupIndex, string assetGuid, bool extend, bool range)
    {
        if (string.IsNullOrEmpty(assetGuid))
            return;

        List<CollectedAssetInfo> groupAssets = GetAssetsForGroup(_curateResult, GetGroupAt(groupIndex)?.GroupName);
        groupAssets.Sort((left, right) => string.Compare(GetAssetNavName(left), GetAssetNavName(right), StringComparison.OrdinalIgnoreCase));
        if (!extend)
        {
            _selectedGroupIndices.Clear();
            _selectedAssetGuids.Clear();
        }

        if (range && !string.IsNullOrEmpty(_selectionAnchorAssetGuid))
        {
            int start = -1;
            int end = -1;
            for (int i = 0; i < groupAssets.Count; i++)
            {
                if (string.Equals(groupAssets[i].AssetGUID, _selectionAnchorAssetGuid, StringComparison.Ordinal))
                    start = i;
                if (string.Equals(groupAssets[i].AssetGUID, assetGuid, StringComparison.Ordinal))
                    end = i;
            }

            if (start >= 0 && end >= 0)
            {
                int first = Mathf.Min(start, end);
                int last = Mathf.Max(start, end);
                for (int i = first; i <= last; i++)
                    _selectedAssetGuids.Add(groupAssets[i].AssetGUID);
            }
            else
                _selectedAssetGuids.Add(assetGuid);
        }
        else if (extend && !_selectedAssetGuids.Add(assetGuid))
            _selectedAssetGuids.Remove(assetGuid);
        else
            _selectedAssetGuids.Add(assetGuid);

        _selectedGroupIndex = groupIndex;
        _selectedAssetGuid = assetGuid;
        _selectionAnchorIndex = groupIndex;
        _selectionAnchorAssetGuid = assetGuid;
    }

    private void ClearSelection()
    {
        _selectedGroupIndices.Clear();
        _selectedAssetGuids.Clear();
        _selectedGroupIndex = -1;
        _selectedAssetGuid = null;
        _selectionAnchorIndex = -1;
        _selectionAnchorAssetGuid = null;
    }

    private bool IsSelectedGroup(int groupIndex)
    {
        return _selectedGroupIndices.Contains(groupIndex);
    }

    private bool IsSelectedAsset(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid))
            return false;
        if (_selectedAssetGuids.Contains(assetGuid))
            return true;

        return false;
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
            if (GitIgnoreMatcher.Evaluate(assetPath, ignorePatterns))
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
        if (GitIgnoreMatcher.Evaluate(folder, ignorePatterns))
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
            if (!GitIgnoreMatcher.Evaluate(assetPath, ignorePatterns))
                return true;
        }

        return false;
    }

    private static List<string> CollectSceneAssetPaths(string folder, AssetCollectionSetting setting)
    {
        var scenePaths = new List<string>();
        List<string> ignorePatterns = setting?.GetEffectiveIgnorePatterns();
        if (GitIgnoreMatcher.Evaluate(folder, ignorePatterns))
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
            if (GitIgnoreMatcher.Evaluate(assetPath, ignorePatterns))
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
        setting.RawFileRules ??= new RawFileRules();
        setting.SharePolicy ??= new SharePolicyConfig();
        setting.AssetAddressEntries ??= new List<AssetAddressEntry>();
        setting.Groups ??= new List<AssetCollectionGroup>();
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

    private void UpdateAssetAddressEntry(string assetGuid, string address, List<string> labels)
    {
        if (_curateSetting == null || string.IsNullOrEmpty(assetGuid))
            return;

        string normalizedAddress = (address ?? string.Empty).Trim();
        List<string> normalizedLabels = labels ?? new List<string>();
        AssetAddressEntry existing = _curateSetting.FindAssetAddressEntry(assetGuid);

        if (string.IsNullOrEmpty(normalizedAddress) && normalizedLabels.Count == 0)
        {
            if (existing != null)
                _curateSetting.AssetAddressEntries.Remove(existing);
            return;
        }

        AssetAddressEntry target = existing ?? _curateSetting.GetOrCreateAssetAddressEntry(assetGuid);
        target.Address = normalizedAddress;
        target.Labels = new List<string>(normalizedLabels);
    }

    private void ApplyAddressStyleToSelection(AssetAddressStyle style, string fallbackGuid, CollectedAssetInfo fallbackPreview)
    {
        HashSet<string> assetGuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string assetGuid in _selectedAssetGuids)
            assetGuids.Add(assetGuid);

        foreach (int groupIndex in _selectedGroupIndices)
        {
            AssetCollectionGroup group = GetGroupAt(groupIndex);
            List<CollectedAssetInfo> assets = GetAssetsForGroup(_curateResult, group?.GroupName);
            for (int i = 0; i < assets.Count; i++)
            {
                if (!string.IsNullOrEmpty(assets[i].AssetGUID))
                    assetGuids.Add(assets[i].AssetGUID);
            }
        }

        if (assetGuids.Count == 0 && !string.IsNullOrEmpty(fallbackGuid))
            assetGuids.Add(fallbackGuid);

        foreach (string assetGuid in assetGuids)
        {
            CollectedAssetInfo preview = FindPreviewAsset(assetGuid);
            if (preview == null && string.Equals(assetGuid, fallbackGuid, StringComparison.Ordinal))
                preview = fallbackPreview;
            if (preview == null)
                continue;

            AssetAddressEntry current = _curateSetting.FindAssetAddressEntry(assetGuid);
            UpdateAssetAddressEntry(assetGuid, GeneratePreviewAddress(preview, style), current?.Labels);
        }

        MarkCuratePreviewDirty();
    }

    private CollectedAssetInfo FindPreviewAssetForGroup(int groupIndex)
    {
        List<CollectedAssetInfo> assets = GetAssetsForGroup(_curateResult, GetGroupAt(groupIndex)?.GroupName);
        return assets.Count == 0 ? null : assets[0];
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
        clone.IgnorePatterns = CloneList(source?.IgnorePatterns);
        clone.Groups = CloneGroups(source?.Groups);
        clone.AssetAddressEntries = CloneAssetAddressEntries(source?.AssetAddressEntries);
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

    private static List<AssetAddressEntry> CloneAssetAddressEntries(List<AssetAddressEntry> source)
    {
        var entries = new List<AssetAddressEntry>();
        if (source == null)
            return entries;

        for (int i = 0; i < source.Count; i++)
        {
            AssetAddressEntry entry = source[i];
            if (entry == null)
                continue;

            entries.Add(new AssetAddressEntry
            {
                AssetGUID = entry.AssetGUID,
                Address = entry.Address,
                Labels = CloneList(entry.Labels)
            });
        }

        return entries;
    }

    private static List<AssetAddressEntry> CloneAssetAddressEntriesForGuids(List<AssetAddressEntry> source, HashSet<string> validGuids)
    {
        var entries = new List<AssetAddressEntry>();
        if (source == null || validGuids == null)
            return entries;

        for (int i = 0; i < source.Count; i++)
        {
            AssetAddressEntry entry = source[i];
            if (entry == null || string.IsNullOrEmpty(entry.AssetGUID) || !validGuids.Contains(entry.AssetGUID))
                continue;

            entries.Add(new AssetAddressEntry
            {
                AssetGUID = entry.AssetGUID,
                Address = entry.Address,
                Labels = CloneList(entry.Labels)
            });
        }

        return entries;
    }

    private static RawFileRules CloneRawFileRules(RawFileRules source)
    {
        if (source == null)
            return new RawFileRules();

        return new RawFileRules
        {
            Patterns = CloneList(source.Patterns)
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
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Editor-only 构建事实列表：以 BuildData/Summaries 的正式摘要为数据源，
/// 展示 Full/Hotfix 历史包与制品状态，并按用户确认删除制品（摘要长期保留）。
/// </summary>
/// <remarks>
/// 制品目录可删除但正式 Summary 保留；被 Hotfix Summary 用作基准的 Full 不允许删除。
/// </remarks>
public sealed class BuildPackageResultsView
{
    private readonly Func<string, List<string>> _listMatchingReports;
    private readonly Func<string, List<string>, int> _deleteMatchingReports;
    private readonly Action _reportsChanged;
    private readonly List<PackageEntry> _entries = new();
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private VisualElement _root;
    private VisualElement _list;
    private Label _statusLabel;

    public BuildPackageResultsView(
        Func<string, List<string>> listMatchingReports = null,
        Func<string, List<string>, int> deleteMatchingReports = null,
        Action reportsChanged = null)
    {
        _listMatchingReports = listMatchingReports;
        _deleteMatchingReports = deleteMatchingReports;
        _reportsChanged = reportsChanged;
    }

    public void Build(VisualElement root)
    {
        _root = root;
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;
        _root.Clear();

        var toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", Refresh, 70f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Packages Folder", RevealPackagesFolder, 110f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Delete Selected", DeleteSelected, 118f));
        toolbar.Add(BuildPipelineUI.Spacer());
        _statusLabel = BuildPipelineUI.ToolbarLabel(string.Empty);
        _statusLabel.style.minWidth = 180f;
        toolbar.Add(_statusLabel);
        _root.Add(toolbar);

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _list = new VisualElement();
        scroll.Add(_list);
        _root.Add(scroll);

        Refresh();
    }

    public void Refresh()
    {
        if (_list == null)
            return;

        LoadEntries();
        DrawList();
        UpdateStatus();
    }

    /// <summary>历史包列表来自正式 Summary；制品状态由 ArtifactRelativePath 指向的目录是否存在决定。</summary>
    private void LoadEntries()
    {
        _entries.Clear();
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        List<CompleteBuildSummary.SummaryDocument> documents = store.ReadAllSummaries();

        for (int i = 0; i < documents.Count; i++)
        {
            CompleteBuildSummary.SummaryDocument document = documents[i];
            if (document == null)
                continue;

            if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
                continue;
            if (!DateTime.TryParse(document.StartedAtUtc, null,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime buildTimeUtc))
            {
                buildTimeUtc = DateTime.MinValue;
            }

            string artifactDir = ResolveArtifactDir(document.ArtifactRelativePath);
            _entries.Add(new PackageEntry
            {
                SummaryId = document.BuildId,
                BackendId = document.BackendId,
                BuildType = document.BuildType,
                Version = version,
                BuildTimeUtc = buildTimeUtc,
                SizeBytes = SumSize(document.Files),
                ArtifactRelativePath = document.ArtifactRelativePath,
                ArtifactDir = artifactDir,
                ArtifactExists = !string.IsNullOrEmpty(artifactDir) && FileHelper.DirectoryExists(artifactDir)
            });
        }

        _entries.Sort((a, b) => b.BuildTimeUtc.CompareTo(a.BuildTimeUtc));

        var validIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _entries.Count; i++)
            validIds.Add(_entries[i].SummaryId);
        _selectedIds.RemoveWhere(id => !validIds.Contains(id));
    }

    private void DrawList()
    {
        _list.Clear();
        if (_entries.Count == 0)
        {
            _list.Add(CreateEmptyState("No build summaries found under " + BuildSummaryStore.CreateDefault().RootDir));
            return;
        }

        AddHeader();
        for (int i = 0; i < _entries.Count; i++)
            _list.Add(CreateRow(_entries[i]));
    }

    private void AddHeader()
    {
        var row = CreateDataRow();
        row.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
        row.Add(CreateCell("", 28f));
        AddHeaderCell(row, "Package", 250f);
        AddHeaderCell(row, "Version", 92f);
        AddHeaderCell(row, "Type", 70f);
        AddHeaderCell(row, "Build Time", 140f);
        AddHeaderCell(row, "State", 70f);
        AddHeaderCell(row, "Size", 82f);
        AddHeaderCell(row, "Path", 0f, true);
        _list.Add(row);
    }

    private VisualElement CreateRow(PackageEntry entry)
    {
        var row = CreateDataRow();

        var toggle = new Toggle { value = _selectedIds.Contains(entry.SummaryId) };
        toggle.style.width = 28f;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
                _selectedIds.Add(entry.SummaryId);
            else
                _selectedIds.Remove(entry.SummaryId);
            UpdateStatus();
        });
        row.Add(toggle);

        row.Add(CreateCell(entry.SummaryId, 250f));
        row.Add(CreateCell(entry.Version.GetReleaseVersionString(), 92f));
        row.Add(CreateCell(entry.BuildType, 70f));
        row.Add(CreateCell(entry.BuildTimeUtc == DateTime.MinValue
            ? "-"
            : entry.BuildTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), 140f));
        row.Add(CreateCell(entry.StateLabel, 70f));
        row.Add(CreateCell(entry.ArtifactExists ? FileHelper.FormatBytes(entry.SizeBytes) : "-", 82f));
        row.Add(CreateCell(entry.ArtifactDir, 0f, true));
        return row;
    }

    private void DeleteSelected()
    {
        var selected = new List<PackageEntry>();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_selectedIds.Contains(_entries[i].SummaryId))
                selected.Add(_entries[i]);
        }

        if (selected.Count == 0)
            return;

        var summary = new List<string>();
        for (int i = 0; i < selected.Count; i++)
        {
            PackageEntry entry = selected[i];
            summary.Add($"{entry.SummaryId} | {entry.Version.GetReleaseVersionString()} | {entry.BuildType}"
                        + (entry.ArtifactExists ? string.Empty : " | Missing"));
        }

        if (!EditorUtility.DisplayDialog(
                "Delete Package Artifacts",
                "删除选中的包目录（正式 Summary 保留，可用 Missing 状态继续定位）？\n\n"
                + string.Join("\n", summary),
                "Delete",
                "Cancel"))
            return;

        var failures = new List<string>();
        for (int i = 0; i < selected.Count; i++)
        {
            PackageEntry entry = selected[i];
            List<string> dependents = FindHotfixDependents(entry.SummaryId);
            if (dependents.Count > 0)
            {
                failures.Add($"{entry.SummaryId} 仍被 Hotfix 引用为基准: {string.Join(", ", dependents)}");
                continue;
            }

            if (!entry.ArtifactExists)
                continue;

            if (!IsSafeArtifactDir(entry.ArtifactDir))
            {
                Debug.LogError($"[BuildPackageResultsView] 拒绝删除项目根外的路径：{entry.ArtifactDir}");
                failures.Add("Unsafe package path: " + entry.ArtifactDir);
                continue;
            }

            if (!FileHelper.TryDeleteDirectory(entry.ArtifactDir, true))
            {
                Debug.LogError($"[BuildPackageResultsView] 删除 package 失败：{entry.ArtifactDir}");
                failures.Add("Package delete failed: " + entry.SummaryId);
                continue;
            }

            if (_deleteMatchingReports != null)
            {
                var failedReports = new List<string>();
                _deleteMatchingReports(entry.ArtifactDir, failedReports);
                for (int j = 0; j < failedReports.Count; j++)
                    failures.Add("Report delete failed: " + Path.GetFileName(failedReports[j]));
            }
        }

        _selectedIds.Clear();
        AssetDatabase.Refresh();
        if (failures.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Delete Conflict",
                "部分包未能删除：\n\n" + string.Join("\n", failures),
                "OK");
        }

        if (_reportsChanged != null)
            _reportsChanged();
        else
            Refresh();
    }

    /// <summary>找出把该 Summary 作为基准 Full 的 Hotfix 记录；非 Full 或无人引用时返回空。</summary>
    private static List<string> FindHotfixDependents(string summaryId)
    {
        var dependents = new List<string>();
        if (string.IsNullOrEmpty(summaryId))
            return dependents;

        List<CompleteBuildSummary.SummaryDocument> documents = BuildSummaryStore.CreateDefault().ReadAllSummaries();
        for (int i = 0; i < documents.Count; i++)
        {
            CompleteBuildSummary.SummaryDocument document = documents[i];
            if (document == null)
                continue;
            if (string.Equals(document.BaseFullSummaryId, summaryId, StringComparison.OrdinalIgnoreCase))
                dependents.Add(document.BuildId);
        }

        return dependents;
    }

    private static long SumSize(List<CompleteBuildSummary.SummaryFile> files)
    {
        long total = 0;
        if (files == null)
            return total;

        for (int i = 0; i < files.Count; i++)
        {
            if (files[i] != null)
                total += files[i].Size;
        }

        return total;
    }

    private static string ResolveArtifactDir(string artifactRelativePath)
    {
        if (string.IsNullOrEmpty(artifactRelativePath))
            return string.Empty;

        string root = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        return FYAssetPathUtility.NormalizePath(
            Path.Combine(root, artifactRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>删除目标必须是项目根下的子目录，且不能是根本身。</summary>
    private static bool IsSafeArtifactDir(string artifactDir)
    {
        if (string.IsNullOrEmpty(artifactDir))
            return false;

        string root = Path.GetFullPath(BuildPathManager.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(artifactDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null)
            return;

        int selectedCount = 0;
        long selectedBytes = 0;
        long totalBytes = 0;
        int missingCount = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            PackageEntry entry = _entries[i];
            if (!entry.ArtifactExists)
            {
                missingCount++;
                continue;
            }

            totalBytes += entry.SizeBytes;
            if (!_selectedIds.Contains(entry.SummaryId))
                continue;

            selectedCount++;
            selectedBytes += entry.SizeBytes;
        }

        _statusLabel.text = $"{_entries.Count} records / missing {missingCount} / {FileHelper.FormatBytes(totalBytes)}"
                            + $"    Selected {selectedCount} / {FileHelper.FormatBytes(selectedBytes)}";
    }

    private static void RevealPackagesFolder()
    {
        FileHelper.EnsureDirectory(BuildPathManager.PackagesDir);
        EditorUtility.RevealInFinder(BuildPathManager.PackagesDir);
    }

    private static VisualElement CreateDataRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.minHeight = 24f;
        row.style.alignItems = Align.Center;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = new Color(0f, 0f, 0f, 0.16f);
        row.style.paddingLeft = 4f;
        row.style.paddingRight = 4f;
        return row;
    }

    private static void AddHeaderCell(VisualElement row, string text, float width, bool grow = false)
    {
        Label label = CreateCell(text, width, grow);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(label);
    }

    private static Label CreateCell(string text, float width, bool grow = false)
    {
        var label = BuildPipelineUI.SmallText(text ?? string.Empty);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.marginRight = 8f;
        if (width > 0f)
        {
            label.style.width = width;
            label.style.flexShrink = 0f;
        }
        else if (grow)
        {
            label.style.flexGrow = 1f;
            label.style.minWidth = 0f;
        }
        return label;
    }

    private static Label CreateEmptyState(string text)
    {
        Label label = BuildPipelineUI.SmallText(text);
        label.style.paddingLeft = 8f;
        label.style.paddingRight = 8f;
        label.style.paddingTop = 8f;
        label.style.paddingBottom = 8f;
        label.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
        return label;
    }

    private sealed class PackageEntry
    {
        public string SummaryId;
        public string BackendId;
        public string BuildType;
        public VersionNumber Version;
        public DateTime BuildTimeUtc;
        public long SizeBytes;
        public string ArtifactRelativePath;
        public string ArtifactDir;
        public bool ArtifactExists;

        public string StateLabel => ArtifactExists ? "OK" : "Missing";
    }
}
#endif

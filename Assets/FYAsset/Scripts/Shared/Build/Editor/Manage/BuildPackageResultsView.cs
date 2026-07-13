#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Editor-only package output list and deletion controls.
/// </summary>
public sealed class BuildPackageResultsView
{
    private readonly Func<string, List<string>> _listMatchingReports;
    private readonly Func<string, List<string>, int> _deleteMatchingReports;
    private readonly Action _reportsChanged;
    private readonly List<PackageEntry> _entries = new();
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
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

    private void LoadEntries()
    {
        _entries.Clear();
        string packagesDir = BuildPathManager.PackagesDir;
        string[] dirs = FileHelper.GetDirectories(packagesDir, "Build_*");
        for (int i = 0; i < dirs.Length; i++)
        {
            if (!TryParsePackageFolder(dirs[i], out PackageEntry entry))
                continue;
            _entries.Add(entry);
        }

        _entries.Sort((a, b) => b.BuildTimeUtc.CompareTo(a.BuildTimeUtc));

        var validPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _entries.Count; i++)
            validPaths.Add(_entries[i].FullPath);
        _selectedPaths.RemoveWhere(path => !validPaths.Contains(path));
    }

    private void DrawList()
    {
        _list.Clear();
        if (_entries.Count == 0)
        {
            _list.Add(CreateEmptyState("No package folders found under " + BuildPathManager.PackagesDir));
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
        AddHeaderCell(row, "Package", 260f);
        AddHeaderCell(row, "Version", 92f);
        AddHeaderCell(row, "Build Time", 150f);
        AddHeaderCell(row, "Size", 90f);
        AddHeaderCell(row, "Path", 0f, true);
        _list.Add(row);
    }

    private VisualElement CreateRow(PackageEntry entry)
    {
        var row = CreateDataRow();

        var toggle = new Toggle { value = _selectedPaths.Contains(entry.FullPath) };
        toggle.style.width = 28f;
        toggle.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
                _selectedPaths.Add(entry.FullPath);
            else
                _selectedPaths.Remove(entry.FullPath);
            UpdateStatus();
        });
        row.Add(toggle);

        row.Add(CreateCell(entry.PackageName, 260f));
        row.Add(CreateCell(entry.Version.GetReleaseVersionString(), 92f));
        row.Add(CreateCell(entry.BuildTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), 150f));
        row.Add(CreateCell(FileHelper.FormatBytes(entry.SizeBytes), 90f));
        row.Add(CreateCell(entry.FullPath, 0f, true));
        return row;
    }

    private void DeleteSelected()
    {
        var selected = new List<PackageEntry>();
        long totalBytes = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            PackageEntry entry = _entries[i];
            if (!_selectedPaths.Contains(entry.FullPath))
                continue;

            selected.Add(entry);
            totalBytes += entry.SizeBytes;
        }

        if (selected.Count == 0)
            return;

        var names = new List<string>();
        int matchingReportCount = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            names.Add(selected[i].PackageName);
            matchingReportCount += _listMatchingReports?.Invoke(selected[i].FullPath).Count ?? 0;
        }

        string reportMessage = _listMatchingReports == null
            ? string.Empty
            : "\nMatching reports: " + matchingReportCount;
        if (!EditorUtility.DisplayDialog(
                "Delete Packages",
                (_listMatchingReports == null
                    ? "Delete selected package folders?\n\n"
                    : "Delete selected package folders and their matching reports?\n\n")
                + string.Join("\n", names)
                + "\n\nPackages: " + selected.Count
                + reportMessage
                + "\nTotal: " + FileHelper.FormatBytes(totalBytes),
                "Delete",
                "Cancel"))
            return;

        string root = BuildPathManager.PackagesDir;
        var failures = new List<string>();
        for (int i = 0; i < selected.Count; i++)
        {
            PackageEntry entry = selected[i];
            if (!IsSafePackagePath(root, entry.FullPath))
            {
                Debug.LogError($"[BuildPackageResultsView] Refused to delete path outside PackagesDir: {entry.FullPath}");
                failures.Add("Unsafe package path: " + entry.FullPath);
                continue;
            }

            if (!FileHelper.TryDeleteDirectory(entry.FullPath, true))
            {
                Debug.LogError($"[BuildPackageResultsView] Failed to delete package: {entry.FullPath}");
                failures.Add("Package delete failed: " + entry.PackageName);
                continue;
            }

            if (_deleteMatchingReports != null)
            {
                var failedReports = new List<string>();
                _deleteMatchingReports(entry.FullPath, failedReports);
                for (int j = 0; j < failedReports.Count; j++)
                    failures.Add("Report delete failed: " + Path.GetFileName(failedReports[j]));
            }
        }

        _selectedPaths.Clear();
        AssetDatabase.Refresh();
        if (failures.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Delete Conflict",
                "Some package/report state could not be synchronized:\n\n" + string.Join("\n", failures),
                "OK");
        }

        if (_reportsChanged != null)
            _reportsChanged();
        else
            Refresh();
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null)
            return;

        int selectedCount = 0;
        long selectedBytes = 0;
        long totalBytes = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            totalBytes += _entries[i].SizeBytes;
            if (!_selectedPaths.Contains(_entries[i].FullPath))
                continue;
            selectedCount++;
            selectedBytes += _entries[i].SizeBytes;
        }

        _statusLabel.text = $"{_entries.Count} packages / {FileHelper.FormatBytes(totalBytes)}    Selected {selectedCount} / {FileHelper.FormatBytes(selectedBytes)}";
    }

    private static bool TryParsePackageFolder(string path, out PackageEntry entry)
    {
        entry = null;
        string name = Path.GetFileName(path);
        const string prefix = "Build_";
        const int timestampLength = 14;
        int timestampStart = prefix.Length;
        int separatorIndex = timestampStart + timestampLength;

        if (string.IsNullOrEmpty(name)
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || separatorIndex >= name.Length
            || name[separatorIndex] != '_')
        {
            return false;
        }

        string timestamp = name.Substring(timestampStart, timestampLength);
        string versionText = name.Substring(separatorIndex + 1);
        if (!DateTime.TryParseExact(
                timestamp,
                BuildPackageRequest.PackageTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime buildTimeUtc))
            return false;

        if (!VersionNumber.TryParse(versionText, out VersionNumber version))
            return false;

        entry = new PackageEntry
        {
            PackageName = name,
            Version = version,
            BuildTimeUtc = buildTimeUtc,
            SizeBytes = FileHelper.GetDirectorySize(path),
            FullPath = Path.GetFullPath(path)
        };
        return true;
    }

    private static bool IsSafePackagePath(string packagesDir, string path)
    {
        if (string.IsNullOrEmpty(packagesDir) || string.IsNullOrEmpty(path))
            return false;

        string root = Path.GetFullPath(packagesDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
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
        public string PackageName;
        public VersionNumber Version;
        public DateTime BuildTimeUtc;
        public long SizeBytes;
        public string FullPath;
    }
}
#endif

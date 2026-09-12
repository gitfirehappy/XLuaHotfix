using System;

internal static class BackendLabelPanelTests
{
    public static void Run()
    {
        const string root = "Assets/FYAsset/Scripts/";
        foreach (string path in new[]
        {
            "AA/Build/Editor/AAProjectSelectionLabelPanel.cs",
            "AB/Build/Editor/ABPipeline/ABProjectSelectionLabelPanel.cs",
            "Shared/Build/Editor/Common/ProjectSelectionLabelPanelView.cs"
        })
            RepoAssert.True(!RepoSource.Exists(root + path), "Independent Labels pages must remain removed: " + path);

        string aa = RepoSource.Read(root + "AA/Build/Editor/AABuildPipelineWindow.cs");
        string ab = RepoSource.Read(root + "AB/Build/Editor/ABBuildPipelineWindow.cs");
        RepoAssert.NotContains(aa, "ProjectSelectionLabelPanel", "AA uses native Addressables editing");
        RepoAssert.NotContains(ab, "ProjectSelectionLabelPanel", "AB uses Collection editing");

        // T1 起标签只由 AssetOverrides 承载：断言“标签编辑只改 Curate candidate、只有 Save 才落盘”，
        // 不再断言已删除的 AssetEntries / 批量标签入口。
        string source = RepoSource.Read(root + "AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs");
        string overrides = Between(source, "private void DrawAssetOverridesEditor(", "private static Label CreateColumnLabel(");
        RepoAssert.Contains(overrides, "_curateSetting", "Labels require an editable candidate");
        RepoAssert.Contains(overrides, "GetOrCreateAssetOverride", "Labels update candidate metadata");
        RepoAssert.NotContains(overrides, "SaveAssets", "Batch editing must not persist independently");
        RepoAssert.NotContains(overrides, "_setting.", "Batch editing must not write saved settings");
        RepoAssert.NotContains(source, "AddressableAsset", "AB must not write AA metadata");

        string save = Between(source, "private void SaveCollectors()", "private bool HasValidationError()");
        RepoAssert.Contains(save, "CloneAssetOverrides(_curateSetting.AssetOverrides)", "Save copies candidate entries");
        RepoAssert.Contains(save, "AssetDatabase.SaveAssets()", "Save owns persistence");
        string cancel = Between(source, "private void CancelCurate()", "private void RenderPreviewTree(");
        RepoAssert.Contains(cancel, "CloneSetting(_setting)", "Cancel reloads saved state");
        RepoAssert.NotContains(cancel, "SaveAssets", "Cancel must not persist candidate edits");
    }

    private static string Between(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        RepoAssert.True(first >= 0 && last > first, "Expected source boundary: " + start);
        return source.Substring(first, last - first);
    }
}

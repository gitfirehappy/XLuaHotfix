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

        string source = RepoSource.Read(root + "AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs");
        string apply = Between(source, "private string ApplyCandidateLabels(", "private static List<string> NormalizeLabels(");
        RepoAssert.Contains(apply, "WorkflowStage.Curate", "Labels require an editable candidate");
        RepoAssert.Contains(apply, "FindPreviewAsset", "The full batch must belong to the candidate collection");
        RepoAssert.Contains(apply, "EnsureAssetEntry", "Labels update candidate metadata");
        RepoAssert.NotContains(apply, "SaveAssets", "Batch editing must not persist independently");
        RepoAssert.NotContains(apply, "_setting.", "Batch editing must not write saved settings");
        RepoAssert.NotContains(source, "AddressableAsset", "AB must not write AA metadata");

        string save = Between(source, "private void SaveCollectors()", "private bool CanSaveCollectors()");
        RepoAssert.Contains(save, "CloneAssetEntries(_curateSetting.AssetEntries)", "Save copies candidate entries");
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

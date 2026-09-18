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
            "Shared/Build/Common/Editor/ProjectSelectionLabelPanelView.cs"
        })
            RepoAssert.True(!RepoSource.Exists(root + path), "Independent Labels pages must remain removed: " + path);

        string aa = RepoSource.Read(root + "AA/Build/Editor/AABuildPipelineWindow.cs");
        string ab = RepoSource.Read(root + "AB/Build/Editor/ABBuildPipelineWindow.cs");
        RepoAssert.NotContains(aa, "ProjectSelectionLabelPanel", "AA uses native Addressables editing");
        RepoAssert.NotContains(ab, "ProjectSelectionLabelPanel", "AB uses Collection editing");

        string source = RepoSource.Read(root + "AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs");
        RepoAssert.Contains(source, "DrawAssetEditor", "Asset Details owns Address and Labels editing");
        RepoAssert.Contains(source, "UpdateAssetAddressEntry", "Details writes candidate metadata");
        RepoAssert.Contains(source, "AssetAddressEntries", "Save uses the new address entry collection");
        RepoAssert.Contains(source, "RegisterAssetContextMenu", "Asset rows expose short and long address actions");
        RepoAssert.Contains(source, "RegisterGroupContextMenu", "Group has a context menu for short and long names");
        RepoAssert.NotContains(source, "DrawAssetOverridesEditor", "Standalone Asset Overrides view is removed");
        RepoAssert.NotContains(source, "AddAddressOperationButtons", "Inline Apply Address buttons are removed");
        RepoAssert.NotContains(source, "ValidateCurate", "Manual Validate entry is removed");
        RepoAssert.NotContains(source, "setting.AddressStyle", "Collection Setting has no global AddressStyle");
        RepoAssert.NotContains(source, "ExcludedAssets", "Collection Setting has no ExcludedAssets editor");
        RepoAssert.NotContains(source, "Address Override", "Details uses Address directly");
        RepoAssert.NotContains(source, "Labels Override", "Details uses Labels directly");
        RepoAssert.NotContains(source, "AddressableAsset", "AB must not write AA metadata");

        string save = Between(source, "private void SaveCollectors()", "private bool HasValidationError()");
        RepoAssert.Contains(save, "CloneAssetAddressEntries(_curateSetting.AssetAddressEntries)", "Save copies candidate entries");
        RepoAssert.Contains(save, "AssetDatabase.SaveAssets()", "Save owns persistence");
        string cancel = Between(source, "private void CancelCurate()", "private void RenderPreviewTree(");
        RepoAssert.Contains(cancel, "LoadSetting(true)", "Cancel reloads saved state");
        RepoAssert.NotContains(cancel, "CloneSetting(_setting)", "Cancel must use the saved-setting reload path");
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

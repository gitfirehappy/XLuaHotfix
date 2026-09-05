using System;

internal static class BackendLabelPanelTests
{
    private const string SharedViewPath =
        "Assets/FYAsset/Scripts/Shared/Build/Editor/Shared/ProjectSelectionLabelPanelView.cs";
    private const string AAPanelPath =
        "Assets/FYAsset/Scripts/AA/Build/Editor/AAProjectSelectionLabelPanel.cs";
    private const string ABPanelPath =
        "Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/ABProjectSelectionLabelPanel.cs";

    public static void Run()
    {
        VerifyPanelsAreWiredIntoConcreteWindows();
        VerifyAAOwnsOnlyAddressablesWrites();
        VerifyABOwnsOnlyCollectorWrites();
        VerifySharedViewIsPresentationOnly();
    }

    private static void VerifyPanelsAreWiredIntoConcreteWindows()
    {
        RepoAssert.True(RepoSource.Exists(AAPanelPath), "AA Project Selection label panel must exist");
        RepoAssert.True(RepoSource.Exists(ABPanelPath), "AB Project Selection label panel must exist");
        RepoAssert.True(RepoSource.Exists(SharedViewPath), "shared presentation view must exist");

        string aaWindow = RepoSource.Read("Assets/FYAsset/Scripts/AA/Build/Editor/AABuildPipelineWindow.cs");
        string abWindow = RepoSource.Read("Assets/FYAsset/Scripts/AB/Build/Editor/ABBuildPipelineWindow.cs");
        RepoAssert.Contains(aaWindow, "new AAProjectSelectionLabelPanel()",
            "AA window must host its concrete label panel");
        RepoAssert.Contains(abWindow, "new ABProjectSelectionLabelPanel(assetsCollectionPanel)",
            "AB window must host its concrete panel with Curate-state guard");
    }

    private static void VerifyAAOwnsOnlyAddressablesWrites()
    {
        string source = RepoSource.Read(AAPanelPath);
        RepoAssert.Contains(source, "AddressableAssetSettingsDefaultObject.Settings",
            "AA panel must resolve the Addressables authority");
        RepoAssert.Contains(source, "FindAssetEntry", "AA panel must require existing Addressables entries");
        RepoAssert.Contains(source, "entry.IsFolder", "AA panel must reject folder entries defensively");
        RepoAssert.Contains(source, "SetLabel", "AA panel must write Addressables labels");
        RepoAssert.Contains(source, "Undo.RecordObject", "AA batch replacement must be undoable");
        RepoAssert.NotContains(source, "AssetCollectionSetting", "AA panel must not write AB collection metadata");
        RepoAssert.NotContains(source, "FYAssetBuildSettingsProvider.AB", "AA panel must not resolve AB settings");
    }

    private static void VerifyABOwnsOnlyCollectorWrites()
    {
        string source = RepoSource.Read(ABPanelPath);
        RepoAssert.Contains(source, "CollectorMutationUtility.LoadSetting()",
            "AB panel must resolve the active AssetCollectionSetting");
        RepoAssert.Contains(source, "FindAssetEntry", "AB panel must require existing collected AssetEntries");
        RepoAssert.Contains(source, "HasUnsavedChanges", "AB panel must guard unsaved Curate state");
        RepoAssert.Contains(source, "Undo.RecordObject", "AB batch replacement must be undoable");
        RepoAssert.NotContains(source, "AddressableAsset", "AB panel must not write Addressables metadata");

        string collectionPanel = RepoSource.Read(
            "Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs");
        RepoAssert.Contains(collectionPanel, "public bool HasUnsavedChanges",
            "AssetsCollectionPanel must expose a read-only conflict guard");
    }

    private static void VerifySharedViewIsPresentationOnly()
    {
        string source = RepoSource.Read(SharedViewPath);
        RepoAssert.Contains(source, "NormalizeLabels", "shared view may normalize presentation input");
        RepoAssert.Contains(source, "new HashSet<string>(StringComparer.OrdinalIgnoreCase)",
            "shared input must deduplicate labels with the same case-insensitive semantics as both runtime indexes");
        RepoAssert.Contains(source, "Selection.assetGUIDs", "shared view may read Project Selection");
        RepoAssert.Contains(source, "AssetDatabase.IsValidFolder",
            "shared selection must exclude Project folders before invoking a backend writer");
        RepoAssert.Contains(source, "EditorUtility.DisplayDialog",
            "empty replacement must require an explicit clear confirmation");
        RepoAssert.NotContains(source, "AddressableAsset", "shared view must not know AA write APIs");
        RepoAssert.NotContains(source, "AssetCollectionSetting", "shared view must not know AB write APIs");
        RepoAssert.NotContains(source, "BackendMode", "shared view must not switch backends");
        RepoAssert.NotContains(source, "interface I", "shared view must not define a shared backend writer interface");
    }
}

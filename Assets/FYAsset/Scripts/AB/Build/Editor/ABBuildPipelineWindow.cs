using UnityEditor;
using UnityEngine;

/// <summary>
/// AB 专用构建管线窗口。
/// </summary>
public sealed class ABBuildPipelineWindow : BuildPipelineWindowBase
{
    [MenuItem("FYAsset/Build/AB Build Pipeline")]
    public static void Open()
    {
        ABBuildPipelineWindow window = GetWindow<ABBuildPipelineWindow>();
        window.titleContent = new GUIContent("AB Build Pipeline");
        window.minSize = new Vector2(800f, 500f);
        window.Show();
    }

    protected override IBuildPipelinePanel[] CreatePanels()
    {
        var assetsCollectionPanel = new AssetsCollectionPanel();
        return new IBuildPipelinePanel[]
        {
            new SettingsPanel(),
            new ABConfigPanel(),
            assetsCollectionPanel,
            new PipelinePanel(
                "AB Build",
                () => FYAssetABSettings.Instance.BuildPipelineConfigPath,
                ABPipelineBackbone.CreateCoreSlots,
                "PipelinePanel",
                true,
                true,
                new BuildPanelActions
                {
                    BuildFull = ABBuildProjectManager.BuildFullPackage,
                    BuildHotfix = ABBuildProjectManager.BuildHotfix,
                    BuildStandalone = ABBuildProjectManager.BuildStandalonePackage,
                    LastBuildSuccess = () => ABBuildProjectManager.LastBuildSuccess,
                },
                ABPipelineConfigUpgrade.TryUpgrade,
                () => BuildProjectRunner.LastSummary),
            new ABReportPanel(),
            new PublishTargetPanel(BackendModeNames.AB, ApplyHotfixUrl, ABPackageManifestReader.Instance),
            new ABTestMaintenancePanel()
        };
    }

    private static void ApplyHotfixUrl(string url)
    {
        Undo.RecordObject(FYAssetABSettings.Instance, "Apply Hotfix URL");
        FYAssetABSettings.Instance.HotfixUrl = url;
        EditorUtility.SetDirty(FYAssetABSettings.Instance);
        AssetDatabase.SaveAssets();
    }
}

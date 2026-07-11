using UnityEditor;
using UnityEngine;

/// <summary>
/// AB-only build pipeline window.
/// </summary>
public sealed class ABBuildPipelineWindow : BuildPipelineWindowBase
{
    [MenuItem("Tools/Build/AB Build Pipeline")]
    public static void Open()
    {
        ABBuildPipelineWindow window = GetWindow<ABBuildPipelineWindow>();
        window.titleContent = new GUIContent("AB Build Pipeline");
        window.minSize = new Vector2(800f, 500f);
        window.Show();
    }

    protected override IBuildPipelinePanel[] CreatePanels()
    {
        return new IBuildPipelinePanel[]
        {
            new SettingsPanel(),
            new ABConfigPanel(),
            new AssetsCollectionPanel(),
            new PipelinePanel(
                "AB Build",
                () => FYAssetBuildSettingsProvider.AB.BuildPipelineConfigPath,
                BuildPipelineBackbone.CreateABTasks,
                "PipelinePanel",
                true,
                true,
                BackendMode.ABManifest),
            new ABReportPanel(),
            new RepositoryStatusPanel(BackendMode.ABManifest, "AB Repository")
        };
    }
}

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
            new ABProjectSelectionLabelPanel(assetsCollectionPanel),
            new PipelinePanel(
                "AB Build",
                () => FYAssetABSettings.Instance.BuildPipelineConfigPath,
                ABPipelineBackbone.CreateDefaultTasks,
                "PipelinePanel",
                true,
                true,
                new BuildPanelActions
                {
                    BuildFull = ABBuildProjectManager.BuildFullPackage,
                    BuildHotfix = ABBuildProjectManager.BuildHotfix,
                    BuildStandalone = ABBuildProjectManager.BuildStandalonePackage,
                    LastBuildSuccess = () => ABBuildProjectManager.LastBuildSuccess,
                }),
            new ABReportPanel(),
            new RepositoryStatusPanel("AB", "AB Repository", null, new ABRepositorySettingsSink(), new ABRepositoryPreviewProvider(), null, new ABRepositoryDataCleaner())
        };
    }
}

/// <summary>AB 侧启动数据清理：供共享 Repository 面板注入。</summary>
public sealed class ABRepositoryDataCleaner : IRepositoryDataCleaner
{
    public void ClearStartupData()
    {
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(UnityEngine.Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(UnityEngine.Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN));
    }
}

/// <summary>AB 侧 settings 落盘实现：供共享 Repository 面板注入。</summary>
public sealed class ABRepositorySettingsSink : IRepositorySettingsSink
{
    public void ApplyHotfixUrl(string url)
    {
        FYAssetABSettings settings = FYAssetABSettings.Instance;
        Undo.RecordObject(settings, "Apply Hotfix URL");
        settings.HotfixUrl = url;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}

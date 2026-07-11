using UnityEditor;

/// <summary>
/// Compatibility menu for callers that still select the editor backend through UseABBackend.
/// </summary>
public static class BuildPipelineWindowMenu
{
    [MenuItem(FYAssetSettings.BUILD_PIPELINE_WINDOW_MENU_PATH)]
    private static void OpenLegacy()
    {
        if (FYAssetSettings.Instance.UseABBackend)
            ABBuildPipelineWindow.Open();
        else
            AABuildPipelineWindow.Open();
    }
}

#if UNITY_EDITOR
using System;

/// <summary>
/// 旧 Editor 和 CLI 调用方的 compatibility facade。
/// 新增 backend 专用代码应直接调用 AABuildProjectManager 或 ABBuildProjectManager。
/// </summary>
public static class BuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        if (GetSelectedBackend() == BackendMode.ABManifest)
        {
            ABBuildProjectManager.BuildFullPackage(options);
            LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
            return;
        }

        AABuildProjectManager.BuildFullPackage(options);
        LastBuildSuccess = AABuildProjectManager.LastBuildSuccess;
    }

    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        if (GetSelectedBackend() == BackendMode.ABManifest)
        {
            ABBuildProjectManager.BuildHotfix(options);
            LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
            return;
        }

        AABuildProjectManager.BuildHotfix(options);
        LastBuildSuccess = AABuildProjectManager.LastBuildSuccess;
    }

    public static void BuildStandalonePackage(BuildExecutionOptions options = null)
    {
        // Standalone 当前仅支持 AB 管线
        ABBuildProjectManager.BuildStandalonePackage(options);
        LastBuildSuccess = ABBuildProjectManager.LastBuildSuccess;
    }

    public static void ResetGroupsToOriginal()
    {
        if (GetSelectedBackend() == BackendMode.ABManifest)
            ABBuildProjectManager.ResetGroupsToOriginal();
        else
            AABuildProjectManager.ResetGroupsToOriginal();
    }

    private static BackendMode GetSelectedBackend()
    {
        FYAssetBackendSettings settings = UnityEditor.AssetDatabase.LoadAssetAtPath<FYAssetBackendSettings>(
            FYAssetBackendSettings.DEFAULT_ASSET_PATH);
        if (settings == null)
            throw new InvalidOperationException(
                $"FYAssetBackendSettings not found: {FYAssetBackendSettings.DEFAULT_ASSET_PATH}");
        if (!BackendModeNames.IsValid(settings.Backend))
            throw new InvalidOperationException("FYAssetBackendSettings 未选择有效 BackendMode。");
        return settings.Backend;
    }
}
#endif

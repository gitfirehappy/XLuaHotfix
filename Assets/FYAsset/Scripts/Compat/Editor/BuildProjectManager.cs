#if UNITY_EDITOR
using System;

/// <summary>
/// 旧 Editor 和 CLI 调用方的 compatibility facade。
/// 新增 backend 专用代码应直接调用 AABuildProjectManager 或 ABBuildProjectManager。
/// </summary>
public static class BuildProjectManager
{
    public static BuildResult BuildFullPackage(BuildExecutionOptions options = null)
        => GetSelectedBackend() == BackendMode.ABManifest
            ? ABBuildProjectManager.BuildFullPackage(options)
            : AABuildProjectManager.BuildFullPackage(options);

    public static BuildResult BuildHotfix(BuildExecutionOptions options = null)
        => GetSelectedBackend() == BackendMode.ABManifest
            ? ABBuildProjectManager.BuildHotfix(options)
            : AABuildProjectManager.BuildHotfix(options);

    public static BuildResult BuildStandalonePackage(BuildExecutionOptions options = null)
        => ABBuildProjectManager.BuildStandalonePackage(options);

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

#if UNITY_EDITOR
using System.Threading.Tasks;

/// <summary>
/// BuildProjectManager 使用的构建后端接口。
/// 编排层负责版本、菜单入口和发布后处理；后端负责实际构建与产物导出。
/// </summary>
public interface IBuildBackend
{
    Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType);
    Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType, BuildExecutionOptions options);
    void OrganizeOutput(string outputDir, VersionNumber version);
    void GeneratePackageManifest(string outputDir, VersionNumber version);
}

/// <summary>
/// BuildProjectManager 暴露给后端的构建类型。
/// </summary>
public enum BuildType
{
    Full,
    Hotfix
}
#endif
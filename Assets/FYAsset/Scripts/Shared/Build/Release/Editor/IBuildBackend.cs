#if UNITY_EDITOR
using System.Threading.Tasks;

/// <summary>
/// BuildProjectManager 使用的构建后端接口。
/// 编排层负责版本、菜单入口和发布后处理；后端只负责执行已准备好的 Task 图。
/// </summary>
public interface IBuildBackend
{
    /// <summary>baseline package（本地启动数据）staging / 校验 / 应用契约；由后端模块提供。</summary>
    IBaselinePackageHandler BaselineHandler { get; }

    Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options);
}

/// <summary>
/// BuildProjectManager 暴露给后端的构建类型。
/// </summary>
public enum BuildType
{
    Full,
    Hotfix,
    Standalone
}
#endif

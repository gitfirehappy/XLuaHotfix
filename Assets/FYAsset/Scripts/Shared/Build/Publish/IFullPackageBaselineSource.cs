using System;

/// <summary>
/// 基准 Full 包解析接缝：发布流程用它把稀疏 Hotfix 补齐成完整目标包。
/// </summary>
/// <remarks>
/// 稀疏 Hotfix 缺少服务器事实时，基准只能由构建 Summary 的明确路径解析；解析失败即拒绝发布。
/// Shared 不读取 Summary，由编辑器侧实现注入。
/// </remarks>
public interface IFullPackageBaselineSource
{
    /// <summary>
    /// 解析本次发布对应的本地基准 Full 包目录。
    /// 返回 false 表示基准不可确定，<paramref name="error"/> 说明原因，发布方据此报告来源不足。
    /// </summary>
    bool TryResolveBaselinePackageDir(PublishRequest request, out string packageDir, out string error);
}

/// <summary>
/// 基准 Full 解析入口注册表：编辑器实现自注册，测试与工具可以注入替身或显式覆盖。
/// </summary>
public static class FullPackageBaselineSourceRegistry
{
    private static IFullPackageBaselineSource _registered;

    /// <summary>注册（覆盖）编辑器侧解析入口；传 null 表示清除。</summary>
    public static void Register(IFullPackageBaselineSource source)
    {
        _registered = source;
    }

    /// <summary>取得本次请求使用的解析入口：请求显式注入优先，其次为已注册实现；都没有时返回 null。</summary>
    public static IFullPackageBaselineSource Resolve(PublishRequest request)
    {
        if (request != null && request.FullPackageBaselineSource != null)
            return request.FullPackageBaselineSource;

        return _registered;
    }
}

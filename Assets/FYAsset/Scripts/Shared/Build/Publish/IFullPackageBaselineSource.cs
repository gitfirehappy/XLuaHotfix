using System;

/// <summary>
/// 基准 Full 包解析接缝：发布流程用它把稀疏 Hotfix 补齐成完整目标包。
/// </summary>
/// <remarks>
/// 计划 T7 的本地 Full fallback：
/// 1. Hotfix 包只携带“目标清单 + 相对基准 Full 的变化内容”，服务器事实不可用时必须回到本地基准 Full 取字节；
/// 2. 基准只能由构建事实（Summary 的 BaseFullSummaryId → ArtifactRelativePath）推导，
///    不得靠扫描目录猜测身份；解析失败即视为来源不足，发布必须失败；
/// 3. Shared 侧不引用 Summary/Runner，因此编辑器实现（读取 BuildSummaryStore）在 Compat 侧注入本接口；
/// 4. 本文件与 IPackageManifestReader 同样不带 UNITY_EDITOR 包裹：发布请求是运行时可见的数据持有者，
///    它引用的接缝类型也必须运行时可见。
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

#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// baseline package（本地启动数据）staging / 校验 / 应用的后端契约。
/// 由各构建后端实现；TaskExportLocalBuildData 只持有中性导出流程。
/// 管线内通过 BuildContext 注入（后端 BuildAsync 塞入），发布后处理由编排层直接传参。
/// </summary>
public interface IBaselinePackageHandler
{
    /// <summary>
    /// 后端 manifest 文件名清单（json/bin）。用于发布事务的完整性校验：
    /// 机制留在 Shared，后端通过属性注入自己的文件名，避免 Shared 出现按后端取值的行为分支。
    /// </summary>
    IReadOnlyList<string> RequiredManifestFileNames { get; }

    /// <summary>把后端 baseline manifest 文件从构建输出暂存到 stageRoot（bundles 由共享流程负责）。</summary>
    void StageBaselineFiles(BuildPackageRequest request, string stageRoot);

    /// <summary>校验 stageRoot 中后端 baseline manifest 齐全可解析，返回待校验 Bundle 清单；失败抛异常。</summary>
    IReadOnlyList<BundleDownloadItem> LoadStagedBaselineBundles(string stageRoot);

    /// <summary>把 stage 中的后端 manifest 应用到 StreamingAssets，并清理另一后端的遗留文件。</summary>
    void ApplyStagedBaseline(string stageRoot);
}
#endif

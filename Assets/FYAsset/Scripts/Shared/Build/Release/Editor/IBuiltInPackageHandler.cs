#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 内置包（StreamingAssets 启动数据）staging / 校验 / 应用的后端契约。
/// </summary>
/// <remarks>
/// 定位（计划 T7）：Full / Standalone 构建需要把后端清单与内容落到 StreamingAssets，
/// 由各构建后端实现本接口，LocalBuildDataExporter 只持有中性的导出流程。
/// 这里描述的是“安装包内置的完整包”，与历史 baseline 指针语义无关：
/// 本接口不保存版本指针、不记录历史交付清单，也不参与发布。
/// </remarks>
public interface IBuiltInPackageHandler
{
    /// <summary>
    /// 后端 manifest 文件名清单（json/bin）。用于内置包导出与发布事务的完整性校验：
    /// 机制留在 Shared，后端通过属性注入自己的文件名，避免 Shared 出现按后端取值的行为分支。
    /// </summary>
    IReadOnlyList<string> RequiredManifestFileNames { get; }

    /// <summary>把后端 manifest 文件从构建输出暂存到 stageRoot（bundles 由共享流程负责）。</summary>
    void StageBuiltInFiles(BuildPackageRequest request, string stageRoot);

    /// <summary>校验 stageRoot 中后端 manifest 齐全可解析，返回待校验 Bundle 清单；失败抛异常。</summary>
    IReadOnlyList<BundleDownloadItem> LoadStagedBundles(string stageRoot);

    /// <summary>把 stage 中的后端 manifest 应用到 StreamingAssets，并清理另一后端的遗留文件。</summary>
    void ApplyStagedBuiltIn(string stageRoot);
}
#endif

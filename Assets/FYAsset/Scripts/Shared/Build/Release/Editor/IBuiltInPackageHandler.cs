#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 内置包（StreamingAssets 启动数据）staging / 校验 / 应用的后端契约。
/// </summary>
/// <remarks>
/// 内置包导出只描述安装包中的完整内容；Shared 流程负责通用目录处理，后端实现负责自己的清单文件。
/// </remarks>
public interface IBuiltInPackageHandler
{
    /// <summary>
    /// 后端 manifest 文件名清单（json/bin）。用于内置包导出与发布事务的完整性校验：
    /// 机制留在 Shared，后端通过属性注入自己的文件名，避免 Shared 出现按后端取值的行为分支。
    /// </summary>
    IReadOnlyList<string> RequiredManifestFileNames { get; }

    /// <summary>把后端 manifest 文件从构建输出暂存到 stageRoot（bundles 由共享流程负责）。</summary>
    void StageBuiltInFiles(BuildRequest request, string stageRoot);

    /// <summary>校验 stageRoot 中后端 manifest 齐全可解析，返回待校验 Bundle 清单；失败抛异常。</summary>
    IReadOnlyList<BundleDownloadItem> LoadStagedBundles(string stageRoot);

    /// <summary>把 stage 中的后端 manifest 应用到 StreamingAssets，并清理另一后端的遗留文件。</summary>
    void ApplyStagedBuiltIn(string stageRoot);
}
#endif

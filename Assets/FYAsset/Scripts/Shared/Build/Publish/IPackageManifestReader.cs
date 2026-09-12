using System.Collections.Generic;

/// <summary>
/// 后端清单读取契约：发布流程需要知道“服务器上那个包有哪些内容文件”。
/// </summary>
/// <remarks>
/// Shared 不依赖具体后端清单类型；返回的摘要名称统一为包根相对路径，清单不可用时由调用方决定是否完整上传。
/// </remarks>
public interface IPackageManifestReader
{
    /// <summary>
    /// 包根必须存在并随包发布的清单/索引文件（后端决定，例如 ABManifest.json/.bin、catalog.json）。
    /// 这些文件不参与内容 Diff：它们每次都随包发布，用于服务端与客户端确认包身份。
    /// </summary>
    IReadOnlyList<string> RequiredPackageFileNames { get; }

    /// <summary>
    /// 清单声明的物理内容所在目录名（包根下的直接子目录，例如 bundles）。
    /// 发布流程用它区分“必须被清单声明的包内容”与“包根元数据文件”。
    /// </summary>
    string ContentDirectoryName { get; }

    /// <summary>
    /// 读取包目录清单声明的内容文件摘要。
    /// 返回 false 表示清单缺失、不可解析或与目录不匹配；<paramref name="error"/> 说明原因。
    /// </summary>
    bool TryReadContentDigests(string packageDir, out IReadOnlyList<FileHelper.FileDigest> contents, out string error);
}

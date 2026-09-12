using System;
using System.Collections.Generic;

/// <summary>
/// 发布计划：读取服务器事实并与本地包比对后的无副作用结论。
/// </summary>
/// <remarks>
/// 它是发布事务的中间事实，供日志、面板与自检读取：
/// 计划本身不写任何文件；<see cref="IsFullUpload"/> 为 true 时说明服务器事实不可用，
/// 发布按“完整上传”退化，但仍然遵循“新目录校验通过后才写 PackageIndex”。
/// 计划 T7 补充：目标集合（<see cref="TargetFiles"/>）可能大于本地包目录内容——
/// 稀疏 Hotfix 只携带变化内容，未变化内容由服务器当前包或本地基准 Full 补齐。
/// </remarks>
public sealed class PublishPlan
{
    /// <summary>本次发布的包身份</summary>
    public PackageBuildIdentity Identity;

    /// <summary>服务器 PackageIndex 文件路径</summary>
    public string ServerPackageIndexPath;

    /// <summary>服务器上由 PackageIndex 指向的包目录；不可用时为空</summary>
    public string ServerPackageDir;

    /// <summary>服务器 PackageIndex 是否成功读取</summary>
    public bool ServerIndexReadable;

    /// <summary>服务器当前指向的包名；未知时为空</summary>
    public string ServerLatestPackage;

    /// <summary>服务器 Manifest 是否成功读取</summary>
    public bool ServerManifestReadable;

    /// <summary>退化为完整上传的原因；非空即表示本次不依赖任何服务器事实</summary>
    public string DegradeReason = string.Empty;

    /// <summary>本次要发布的本地文件集合（含清单等包根文件，不含构建过程产物）</summary>
    public List<FileDigest> LocalFiles = new();

    /// <summary>
    /// 本次发布要在服务器上呈现的完整目标文件集合（含清单等包根文件）。
    /// 非 Hotfix 包与自足 Hotfix 包等于 <see cref="LocalFiles"/>；
    /// 稀疏 Hotfix 包会追加清单声明而本地缺失的内容。所有校验与就位都以本集合为准。
    /// </summary>
    public List<FileDigest> TargetFiles = new();

    /// <summary>目标文件的字节来源绝对路径（键为包根相对路径）；来源为本地包目录、服务器当前包或基准 Full 包</summary>
    public Dictionary<string, string> FileSources = new(StringComparer.Ordinal);

    /// <summary>是否由本地基准 Full 包补齐了目标内容（目标包不是自足包）</summary>
    public bool AssembledFromBaselineFull;

    /// <summary>参与补齐的本地基准 Full 包目录；未使用时为空</summary>
    public string BaselineFullPackageDir = string.Empty;

    /// <summary>由本地包目录提供字节的目标文件数量</summary>
    public int LocalSourcedCount;

    /// <summary>由服务器当前包只读复用字节的目标文件数量</summary>
    public int ServerSourcedCount;

    /// <summary>由本地基准 Full 包补齐字节的目标文件数量</summary>
    public int BaselineSourcedCount;

    /// <summary>本次发布的完整目标文件集合；尚未组装时退回本地文件集合。</summary>
    public List<FileDigest> ResolveTargetFiles() =>
        TargetFiles != null && TargetFiles.Count > 0 ? TargetFiles : LocalFiles;

    /// <summary>
    /// 登记组装后的目标集合与逐文件来源。
    /// 只接收运行时可见的类型（FileDigest / string），来源统计由调用方按需写入本类型的字段。
    /// </summary>
    public void SetTargetFiles(List<FileDigest> targetFiles, Dictionary<string, string> fileSources)
    {
        if (targetFiles != null && targetFiles.Count > 0)
            TargetFiles = targetFiles;
        if (fileSources != null)
            FileSources = fileSources;
    }

    /// <summary>服务器 Manifest 声明的文件集合；退化时为空</summary>
    public List<FileDigest> ServerFiles = new();

    /// <summary>本地与服务器文件集合的无状态差异</summary>
    public FileDiff Diff = new();

    /// <summary>通过 Hash 命中、从服务器已有内容复用的文件（名称与服务器不同）</summary>
    public List<FileDigest> ReusedByHash = new();

    /// <summary>过程说明（服务器事实、退化原因、缓存漂移等），供日志与面板展示</summary>
    public List<string> Messages = new();

    /// <summary>是否退化为完整上传。</summary>
    public bool IsFullUpload => !string.IsNullOrEmpty(DegradeReason);

    /// <summary>
    /// 需要从本地写入服务器隔离目录的文件数量。
    /// 新增与修改都要写入，但内容 Hash 已在服务器命中的文件直接从服务器复用，不计入上传。
    /// </summary>
    public int UploadCount
    {
        get
        {
            if (Diff == null)
                return 0;

            int count = Diff.Added.Count + Diff.Modified.Count;
            for (int i = 0; i < ReusedByHash.Count; i++)
            {
                string name = ReusedByHash[i].Name;
                if (ContainsName(Diff.Added, name) || ContainsName(Diff.Modified, name))
                    count--;
            }

            return count;
        }
    }

    /// <summary>从服务器已有内容复用的文件数量（同名未变 + Hash 命中）。</summary>
    public int ReuseCount => (Diff == null ? 0 : Diff.Unchanged.Count) + ReusedByHash.Count;

    /// <summary>追加一条过程说明。</summary>
    public void AddMessage(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Messages.Add(message);
    }

    private static bool ContainsName(List<FileDigest> files, string name)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i].Name, name, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

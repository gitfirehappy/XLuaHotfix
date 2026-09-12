#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// 目标包组装计划：本次发布要在服务器上呈现的完整文件集合，以及每个文件的字节来源。
/// </summary>
/// <remarks>
/// 计划 T7 的组装事实（只读产物，不含任何写入动作）：
/// 1. <see cref="TargetFiles"/> 是“目标包应当包含什么”，它是唯一的内容契约；
/// 2. <see cref="FileSources"/> 是“每个文件从哪里取字节”，发布事务只按它搬运；
/// 3. 目标集合大于本地包目录是正常情况：稀疏 Hotfix 的未变化内容来自服务器当前包或基准 Full。
/// </remarks>
public sealed class PackageAssemblyPlan
{
    /// <summary>目标包必须包含的文件集合：本地包目录文件 + 清单声明但本地缺失的内容</summary>
    public List<FileDigest> TargetFiles = new();

    /// <summary>目标文件的字节来源绝对路径；键为包根相对路径</summary>
    public Dictionary<string, string> FileSources = new(StringComparer.Ordinal);

    /// <summary>过程说明（来源统计、基准 Full 补齐事实）</summary>
    public List<string> Messages = new();

    /// <summary>由本地包目录提供字节的目标文件数量</summary>
    public int LocalSourcedCount;

    /// <summary>由服务器当前包只读复用字节的目标文件数量</summary>
    public int ServerSourcedCount;

    /// <summary>由本地基准 Full 包补齐字节的目标文件数量</summary>
    public int BaselineSourcedCount;

    /// <summary>基准 Full 包目录；未参与组装时为空</summary>
    public string BaselinePackageDir = string.Empty;

    /// <summary>是否存在由基准 Full 补齐的文件（即目标包不是自足包）</summary>
    public bool AssembledFromBaselineFull => BaselineSourcedCount > 0;

    /// <summary>追加一条过程说明。</summary>
    public void AddMessage(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Messages.Add(message);
    }

    /// <summary>登记一个目标文件及其字节来源。</summary>
    public void AddTarget(in FileDigest file, string sourcePath)
    {
        if (string.IsNullOrEmpty(file.Name) || string.IsNullOrEmpty(sourcePath))
            return;

        TargetFiles.Add(file);
        FileSources[file.Name] = sourcePath;
    }
}

/// <summary>
/// 目标包来源决策：把本地包目录与清单声明的目标内容集合合并成完整目标包，并逐文件确定字节来源。
/// </summary>
/// <remarks>
/// 计划 T7 的组装规则（本类只读，不做任何文件写入）：
/// 1. 本地包目录内的文件全部进目标集合（发布口径与既有一致）；
/// 2. Hotfix 的清单已经是完整目标清单，因此清单声明而本地缺失的内容必须补齐：
///    先在服务器当前包内找同摘要内容（只读复用），再从本地基准 Full 包取（Summary.BaseFullSummaryId → ArtifactRelativePath）；
/// 3. 三处都取不到，或取到的字节与清单声明的 Hash/CRC/Size 不一致 → 返回“来源不足”，
///    调用方必须失败退出：不得写出部分目标包、不得写 PackageIndex、不得改动服务器旧包；
/// 4. 非 Hotfix 包（Full/Standalone）自带全部内容，保持既有“本地目录即目标集合”的口径。
/// </remarks>
public static class PackageTargetAssembler
{
    /// <summary>把本地包目录与清单声明合并成完整目标包组装计划。</summary>
    /// <param name="request">发布请求（提供本地包目录、清单读取器与包身份）</param>
    /// <param name="localFiles">本地包目录扫描结果</param>
    /// <param name="serverFiles">服务器当前包声明的文件集合；服务器事实不可用时为空</param>
    /// <param name="serverPackageDir">服务器当前包目录；不可用时为空</param>
    public static bool TryCreatePlan(
        PublishRequest request,
        IReadOnlyList<FileDigest> localFiles,
        IReadOnlyList<FileDigest> serverFiles,
        string serverPackageDir,
        out PackageAssemblyPlan plan,
        out string error)
    {
        plan = new PackageAssemblyPlan();
        error = string.Empty;

        if (request == null)
        {
            error = "发布请求为空。";
            return false;
        }

        if (localFiles == null)
        {
            error = "本地包文件集合为空。";
            return false;
        }

        var localByName = FileDiff.IndexByName(localFiles);
        for (int i = 0; i < localFiles.Count; i++)
        {
            FileDigest file = localFiles[i];
            if (string.IsNullOrEmpty(file.Name))
                continue;

            plan.AddTarget(file, FYAssetPathUtility.JoinFilePath(request.SourcePackageDir, file.Name));
            plan.LocalSourcedCount++;
        }

        if (!IsHotfix(request.Identity))
        {
            plan.AddMessage($"目标包文件集合取自本地包目录: 文件={plan.TargetFiles.Count}");
            return true;
        }

        if (request.ManifestReader == null)
        {
            error = "来源不足: 缺少后端清单读取器，无法确定 Hotfix 的目标内容集合。";
            return false;
        }

        if (!request.ManifestReader.TryReadContentDigests(
                request.SourcePackageDir, out IReadOnlyList<FileDigest> declared, out string manifestError))
        {
            error = $"来源不足: 本地 Hotfix 包清单不可读，无法确定目标内容集合 — {manifestError}";
            return false;
        }

        var serverByName = FileDiff.IndexByName(serverFiles);
        var serverByHash = FileDiff.IndexByHash(serverFiles);
        string baselineDir = string.Empty;
        string baselineError = string.Empty;
        string baselineFileError = string.Empty;
        bool baselineResolved = false;
        int declaredCount = 0;

        if (declared != null)
        {
            for (int i = 0; i < declared.Count; i++)
            {
                FileDigest content = declared[i];
                if (string.IsNullOrEmpty(content.Name))
                    continue;

                declaredCount++;
                if (!content.IsComplete)
                {
                    error = $"来源不足: Hotfix 清单声明的内容摘要不完整: '{content.Name}'";
                    return false;
                }

                // 本地已有同名文件：沿用本地口径；摘要与清单不一致时由隔离目录完整性校验拒绝。
                if (localByName.ContainsKey(content.Name))
                    continue;

                if (TryFindServerSource(content, serverByName, serverByHash, serverPackageDir, out string serverPath))
                {
                    plan.AddTarget(content, serverPath);
                    plan.ServerSourcedCount++;
                    continue;
                }

                if (!baselineResolved)
                {
                    baselineResolved = true;
                    baselineDir = ResolveBaselineDir(request, out baselineError);
                }

                if (!string.IsNullOrEmpty(baselineDir)
                    && TryFindBaselineSource(baselineDir, content, out string baselinePath, out baselineFileError))
                {
                    plan.AddTarget(content, baselinePath);
                    plan.BaselineSourcedCount++;
                    continue;
                }

                string baselineFact = !string.IsNullOrEmpty(baselineDir)
                    ? $"基准 Full 包内该内容不可用: {baselineFileError}（基准={baselineDir}）"
                    : $"基准 Full 包不可用: {baselineError}";
                error = $"来源不足: Hotfix 清单声明的内容在本地包目录缺失，且无法从服务器当前包或基准 Full 补齐: "
                        + $"{content.Name} — {baselineFact}";
                return false;
            }
        }

        plan.BaselinePackageDir = baselineDir;
        plan.AddMessage(
            $"Hotfix 目标内容集合: 清单声明={declaredCount}, 本地包目录={plan.LocalSourcedCount}, "
            + $"服务器复用={plan.ServerSourcedCount}, 基准 Full 补齐={plan.BaselineSourcedCount}");
        if (plan.AssembledFromBaselineFull)
            plan.AddMessage($"稀疏 Hotfix 已由本地基准 Full 组装成完整目标包: 基准={plan.BaselinePackageDir}");
        else
            plan.AddMessage("本地 Hotfix 包已包含清单声明的全部内容，未使用基准 Full。");

        return true;
    }

    /// <summary>包身份是否为 Hotfix 构建；构建类型是文本事实，Shared 不依赖 Editor 侧 BuildType 枚举。</summary>
    public static bool IsHotfix(PackageBuildIdentity identity)
    {
        return identity != null
               && string.Equals(identity.BuildType, "Hotfix", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在同一内容目录内比较 Hash/CRC/Size；名称不参与比较。</summary>
    private static bool SameContent(in FileDigest left, in FileDigest right)
    {
        return string.Equals(left.Hash, right.Hash, StringComparison.Ordinal)
               && left.CRC == right.CRC
               && left.Size == right.Size;
    }

    /// <summary>服务器当前包内能否提供该内容的字节：同名同摘要优先，其次 Hash 命中（名称不同）。</summary>
    private static bool TryFindServerSource(
        in FileDigest content,
        Dictionary<string, FileDigest> serverByName,
        Dictionary<string, FileDigest> serverByHash,
        string serverPackageDir,
        out string sourcePath)
    {
        sourcePath = string.Empty;
        if (string.IsNullOrEmpty(serverPackageDir) || !FileHelper.DirectoryExists(serverPackageDir))
            return false;

        if (serverByName.TryGetValue(content.Name, out FileDigest sameName) && SameContent(sameName, content))
        {
            string candidate = FYAssetPathUtility.JoinFilePath(serverPackageDir, sameName.Name);
            if (FileDigest.TryCreate(candidate, content.Name, out FileDigest digest) && SameContent(digest, content))
            {
                sourcePath = candidate;
                return true;
            }
        }

        if (serverByHash.TryGetValue(FileDiff.HashKey(content.Hash, content.Size), out FileDigest sameHash))
        {
            string candidate = FYAssetPathUtility.JoinFilePath(serverPackageDir, sameHash.Name);
            if (FileDigest.TryCreate(candidate, sameHash.Name, out FileDigest digest) && SameContent(digest, content))
            {
                sourcePath = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>基准 Full 包内取该内容：必须存在且 Hash/CRC/Size 与清单声明一致。</summary>
    private static bool TryFindBaselineSource(
        string baselineDir,
        in FileDigest content,
        out string sourcePath,
        out string error)
    {
        sourcePath = string.Empty;
        error = string.Empty;

        string candidate = FYAssetPathUtility.JoinFilePath(baselineDir, content.Name);
        if (!FileHelper.Exists(candidate))
        {
            error = "包内没有同名文件";
            return false;
        }

        if (!FileDigest.TryCreate(candidate, content.Name, out FileDigest digest))
        {
            error = "文件不可读";
            return false;
        }

        if (!SameContent(digest, content))
        {
            error = "文件与清单声明的 Hash/CRC/Size 不一致";
            return false;
        }

        sourcePath = candidate;
        return true;
    }

    /// <summary>解析本地基准 Full 包目录；返回空串表示不可用，<paramref name="error"/> 说明原因。</summary>
    private static string ResolveBaselineDir(PublishRequest request, out string error)
    {
        error = string.Empty;

        IFullPackageBaselineSource source = FullPackageBaselineSourceRegistry.Resolve(request);
        if (source == null)
        {
            error = "本地没有可用的基准 Full 解析入口（Summary 事实不可读）";
            return string.Empty;
        }

        if (!source.TryResolveBaselinePackageDir(request, out string packageDir, out string resolveError))
        {
            error = string.IsNullOrEmpty(resolveError) ? "基准 Full 解析失败" : resolveError;
            return string.Empty;
        }

        if (string.IsNullOrEmpty(packageDir) || !FileHelper.DirectoryExists(packageDir))
        {
            error = $"基准 Full 包目录不存在: {packageDir}";
            return string.Empty;
        }

        return packageDir;
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 发布器：本地组装并校验新的隔离包目录，最后生成并上传 PackageIndex。
/// </summary>
/// <remarks>
/// 计划 T7 的发布步骤（目录型目标）：
/// 1. 读取服务器 PackageIndex；
/// 2. 读取其指向的 Manifest；
/// 3. 与本次包文件集合做无状态 FileDigest Diff；
/// 4. 在新隔离目录复用服务器已有 Hash 内容，其余从本地包复制；
/// 5. 上传缺失/变化内容与完整 Manifest（目录型目标即复制到服务器根下的隔离目录）；
/// 6. 校验新目录逻辑完整性；
/// 7. 本地生成并**最后**上传新 PackageIndex。
/// 服务器查询失败或 Manifest 损坏时退化为完整上传；
/// 此时稀疏 Hotfix 不得直接上传包目录（那会得到一个不完整的目标包），
/// 而应由本地 Hotfix + 本地基准 Full 与服务器当前包组装出完整目标包；来源不足则拒绝发布。
/// 发布不删除任何旧包。
/// </remarks>
public static class BuildPublisher
{
    private const string LogPrefix = "[BuildPublisher]";

    /// <summary>
    /// 发布一个已构建完成的包目录。
    /// </summary>
    public static PushReceipt Push(PublishRequest request, IPushTarget target)
    {
        if (target == null)
            return Fail(null, "发布目标为空。");

        if (request == null)
            return Fail(target.Id, "发布请求为空。");

        if (!request.Validate(out string validationError))
            return Fail(target.Id, validationError);

        string serverRoot = null;
        if (target is IDirectoryPushTarget directoryTarget)
        {
            try
            {
                serverRoot = directoryTarget.ResolveBackendRoot(request.BackendKey);
            }
            catch (Exception ex)
            {
                return Fail(target.Id, $"服务器目录解析失败: {ex.Message}");
            }
        }

        return string.IsNullOrEmpty(serverRoot)
            ? PushFullUpload(request, target)
            : PushToDirectory(request, target, serverRoot);
    }

    /// <summary>目录型目标：执行完整发布事务（读取服务器事实、复用、校验、最后写索引）。</summary>
    private static PushReceipt PushToDirectory(PublishRequest request, IPushTarget target, string serverRoot)
    {
        request.UseServerRoot(serverRoot);

        if (!PackagePublishTransaction.TryCreate(request, serverRoot, out PackagePublishTransaction transaction, out string error))
            return Fail(target.Id, error);

        using (transaction)
        {
            try
            {
                PublishPlan plan = transaction.CreatePlan();
                transaction.Stage();
                transaction.VerifyStaged();
                transaction.Apply();
                transaction.WritePackageIndex();
                transaction.Commit();

                var receipt = new PushReceipt
                {
                    Success = true,
                    TargetId = target.Id,
                    TargetLocation = transaction.TargetPackageDir,
                    PushedAtUtc = DateTime.UtcNow.ToString("o"),
                    DegradedToFullUpload = plan.IsFullUpload,
                    AssembledFromBaselineFull = plan.AssembledFromBaselineFull,
                    BaselineFullPackageDir = plan.BaselineFullPackageDir ?? string.Empty,
                    UploadedCount = plan.UploadCount,
                    ReusedCount = plan.ReuseCount,
                    Messages = plan.Messages
                };

                Debug.Log($"{LogPrefix} 发布完成: Package={plan.Identity.PackageName}, "
                          + $"Target={transaction.TargetPackageDir}, 上传={receipt.UploadedCount}, 复用={receipt.ReusedCount}, "
                          + $"退化完整上传={receipt.DegradedToFullUpload}, 基准 Full 补齐={receipt.AssembledFromBaselineFull}");
                return receipt;
            }
            catch (Exception ex)
            {
                // using 会在退出时按逆序补偿：旧 PackageIndex 与旧包目录保持中断前状态。
                Debug.LogError($"{LogPrefix} 发布失败，已回滚: {ex}");
                return Fail(target.Id, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 非目录型目标：无法读取服务器事实，按完整上传处理，但 PackageIndex 仍由发布器最后交给目标上传。
    /// 完整上传的语义是“完整目标包”而不是“本地包目录”：稀疏 Hotfix 必须先由基准 Full 补齐，
    /// 否则会把缺少未变化内容的包发布成不完整包；来源不足时拒绝上传。
    /// </summary>
    private static PushReceipt PushFullUpload(PublishRequest request, IPushTarget target)
    {
        Debug.LogWarning($"{LogPrefix} 目标 '{target.Id}' 不支持目录级服务器事实访问，本次按完整上传处理。");

        if (!request.TryResolveIdentity(out PackageBuildIdentity identity, out string identityError))
            return Fail(target.Id, identityError);

        if (!identity.IsSafePackageName())
            return Fail(target.Id, $"包名不是合法目录名: '{identity.PackageName}'");

        if (!PackageFileScanner.TryScan(request.SourcePackageDir, out List<FileDigest> localFiles, out string scanError))
            return Fail(target.Id, $"本地包目录扫描失败: {scanError}");

        // 服务器事实不可读：目标集合只能由本地包目录与清单声明（必要时基准 Full）组装。
        if (!PackageTargetAssembler.TryCreatePlan(
                request,
                localFiles,
                null,
                null,
                out PackageAssemblyPlan assembly,
                out string assemblyError))
        {
            return Fail(target.Id, assemblyError);
        }

        string workRoot = FYAssetPathUtility.JoinFilePath(
            Path.GetTempPath(),
            "fyasset_push",
            Guid.NewGuid().ToString("N"));
        string stagedDir = FYAssetPathUtility.JoinFilePath(workRoot, "staged", identity.PackageName);

        try
        {
            FileHelper.EnsureDirectory(stagedDir);
            for (int i = 0; i < assembly.TargetFiles.Count; i++)
            {
                FileDigest file = assembly.TargetFiles[i];
                if (!assembly.FileSources.TryGetValue(file.Name, out string source) || string.IsNullOrEmpty(source))
                    return Fail(target.Id, $"来源不足: 目标文件没有可用字节来源: {file.Name}");

                string destination = FYAssetPathUtility.JoinFilePath(stagedDir, file.Name);
                FileHelper.CopyFile(source, destination, true);

                if (!FileDigest.TryCreate(destination, file.Name, out FileDigest copied) || !copied.Matches(file))
                    return Fail(target.Id, $"完整上传暂存校验失败: {file.Name}（来源={source}）");
            }

            var index = new PackageIndex
            {
                LatestPackage = identity.PackageName,
                LatestVersion = identity.Version,
                BackendMode = string.IsNullOrEmpty(identity.BackendId) ? request.BackendKey : identity.BackendId
            };

            PushReceipt receipt = target.Push(new PushPayload
            {
                Request = request,
                StagedPackageDir = stagedDir,
                PackageIndexJson = SerializationUtility.SerializeToJson(index, true),
                PackagesFolderName = request.PackagesFolderName
            });

            if (receipt == null)
                return Fail(target.Id, "发布目标未返回回执。");

            receipt.TargetId = string.IsNullOrEmpty(receipt.TargetId) ? target.Id : receipt.TargetId;
            receipt.DegradedToFullUpload = true;
            receipt.AssembledFromBaselineFull = assembly.AssembledFromBaselineFull;
            receipt.BaselineFullPackageDir = assembly.BaselinePackageDir;
            receipt.UploadedCount = assembly.TargetFiles.Count;
            for (int i = 0; i < assembly.Messages.Count; i++)
                receipt.Messages.Add(assembly.Messages[i]);
            return receipt;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix} 完整上传失败: {ex}");
            return Fail(target.Id, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(workRoot, true);
        }
    }

    private static PushReceipt Fail(string targetId, string reason)
    {
        Debug.LogError($"{LogPrefix} {reason}");
        return new PushReceipt
        {
            Success = false,
            TargetId = targetId ?? string.Empty,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }
}
#endif

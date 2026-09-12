#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 目录型发布事务：读取服务器事实 → 组装新的隔离包目录 → 校验逻辑完整性 → 就位 → 最后写 PackageIndex。
/// </summary>
/// <remarks>
/// 计划 T7 的发布语义：
/// 1. 服务器 PackageIndex 与其指向的 Manifest 是唯一远端事实；查询失败或 Manifest 损坏时退化为完整上传；
/// 2. 新目录先在服务器根下的隔离工作区组装：同名同 Hash 的内容直接复用服务器已有文件，
///    其余从本地包目录复制；Hash 命中但名称不同的内容也允许复用（复用后逐文件校验摘要）；
///    稀疏 Hotfix 的目标集合以包内完整清单声明为准：本地缺失的内容由服务器当前包或本地基准 Full 补齐，
///    三处都取不到（或字节与清单声明不一致）时以“来源不足”拒绝发布，且不写任何文件；
/// 3. 组装完成后校验新目录逻辑完整性（必填清单齐全、文件集合与目标计划一致、包内文件都被清单声明）；
/// 4. 校验通过才就位新包目录，并且**最后**才生成并上传 PackageIndex；
/// 5. 当前 PackageIndex 指向的包目录不可被覆盖；其他旧包一律不删除（清理是独立维护入口）；
///    本事务对服务器当前包只读：不会写入、删除或重命名其内的任何文件；
/// 6. 任一步骤失败按逆序补偿：旧 PackageIndex 与旧包目录保持中断前的状态。
/// </remarks>
public sealed class PackagePublishTransaction : IDisposable
{
    private readonly PublishRequest _request;
    private readonly string _serverRoot;
    private readonly string _packagesRoot;
    private readonly string _targetPackageDir;
    private readonly string _packageIndexPath;
    private readonly string _workRoot;
    private readonly string _stagedPackageDir;
    private readonly string _backupPackageDir;
    private readonly string _backupIndexPath;

    private bool _completed;
    private bool _targetBackedUp;
    private bool _targetReplaced;
    private bool _indexExisted;
    private bool _indexBackedUp;
    private bool _indexWritten;
    private bool _alreadyPublished;

    /// <summary>构造事务；服务器根与包身份必须有效，调用方应通过 <see cref="TryCreate"/> 建立实例。</summary>
    private PackagePublishTransaction(PublishRequest request, string serverRoot, PackageBuildIdentity identity)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(serverRoot))
            throw new ArgumentException("目录型发布需要服务器后端根目录。", nameof(serverRoot));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (!PackageBuildIdentity.TryParsePackageName(identity.PackageName, out _, out _, out string packageNameError))
            throw new InvalidOperationException(packageNameError);
        if (!PublishPathGuard.IsSafeSegment(request.PackagesFolderName))
            throw new InvalidOperationException($"包集合名不是合法单段目录名: '{request.PackagesFolderName}'");

        _serverRoot = FYAssetPathUtility.NormalizePath(Path.GetFullPath(serverRoot));
        _packagesRoot = FYAssetPathUtility.JoinFilePath(_serverRoot, _request.PackagesFolderName);
        _packageIndexPath = FYAssetPathUtility.JoinFilePath(_serverRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        _targetPackageDir = FYAssetPathUtility.JoinFilePath(_packagesRoot, identity.PackageName);
        _workRoot = FYAssetPathUtility.JoinFilePath(
            _serverRoot,
            PackageFileNames.PushWorkFolderName,
            identity.PackageName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _stagedPackageDir = FYAssetPathUtility.JoinFilePath(_workRoot, "staged", identity.PackageName);
        _backupPackageDir = FYAssetPathUtility.JoinFilePath(_workRoot, "backup", identity.PackageName);
        _backupIndexPath = FYAssetPathUtility.JoinFilePath(
            _workRoot,
            "backup",
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

        // 所有派生路径必须在服务器根内，且路径链不得包含符号链接/重解析点。
        if (!PublishPathGuard.IsContainedIn(_serverRoot, _packagesRoot)
            || !PublishPathGuard.IsContainedIn(_serverRoot, _targetPackageDir)
            || !PublishPathGuard.IsContainedIn(_serverRoot, _workRoot)
            || !PublishPathGuard.IsContainedIn(_serverRoot, _stagedPackageDir)
            || !PublishPathGuard.IsContainedIn(_serverRoot, _backupPackageDir))
        {
            throw new InvalidOperationException($"发布路径越出服务器根: {_serverRoot}");
        }

        if (!PublishPathGuard.HasNoReparsePoint(_serverRoot, out string reparseError))
            throw new InvalidOperationException(reparseError);
    }

    /// <summary>本次发布的包身份。</summary>
    public PackageBuildIdentity Identity { get; }

    /// <summary>发布计划；<see cref="CreatePlan"/> 之后可用。</summary>
    public PublishPlan Plan { get; private set; }

    /// <summary>隔离组装目录（新包目录的候选内容）。</summary>
    public string StagedPackageDir => _stagedPackageDir;

    /// <summary>本次发布的目标包目录（服务器上的不可变目录）。</summary>
    public string TargetPackageDir => _targetPackageDir;

    /// <summary>服务器 PackageIndex 文件路径。</summary>
    public string PackageIndexPath => _packageIndexPath;

    /// <summary>按发布请求建立事务；包身份无法解析时返回 false。</summary>
    public static bool TryCreate(PublishRequest request, string serverRoot, out PackagePublishTransaction transaction, out string error)
    {
        transaction = null;
        error = string.Empty;

        if (request == null)
        {
            error = "发布请求为空。";
            return false;
        }

        if (!request.Validate(out string validationError))
        {
            error = validationError;
            return false;
        }

        if (!request.TryResolveIdentity(out PackageBuildIdentity identity, out string identityError))
        {
            error = identityError;
            return false;
        }

        try
        {
            transaction = new PackagePublishTransaction(request, serverRoot, identity);
            return true;
        }
        catch (Exception ex)
        {
            transaction = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 第 1-4 步：读取服务器 PackageIndex 与其指向的 Manifest，扫描本地包并做无状态 Diff。
    /// 只读操作，不修改任何文件；稀疏 Hotfix 的来源不足也在这一步被拒绝。
    /// </summary>
    public PublishPlan CreatePlan()
    {
        Plan = new PublishPlan
        {
            Identity = Identity,
            ServerPackageIndexPath = _packageIndexPath
        };

        if (!PackageFileScanner.TryScan(_request.SourcePackageDir, out List<FileDigest> localFiles, out string scanError))
            throw new IOException($"本地包目录扫描失败: {scanError}");

        Plan.LocalFiles = localFiles;
        EnsureRequiredFilesPresent(Plan.LocalFiles, _request.SourcePackageDir, "本地包目录");

        ReadServerFacts(Plan);
        AssembleTargetSet(Plan);

        List<FileDigest> targetFiles = Plan.ResolveTargetFiles();
        Plan.Diff = FileDiff.Compute(Plan.ServerFiles, targetFiles);
        Plan.AddMessage(
            $"本地文件={Plan.LocalFiles.Count}, 目标文件={targetFiles.Count}, 服务器声明={Plan.ServerFiles.Count}, "
            + $"新增={Plan.Diff.Added.Count}, 修改={Plan.Diff.Modified.Count}, 未变={Plan.Diff.Unchanged.Count}, "
            + $"服务器移除={Plan.Diff.Removed.Count}");

        DescribeCacheDrift(Plan);
        return Plan;
    }

    /// <summary>
    /// 第 4.5 步：把本地包目录与清单声明的完整目标内容集合合并成目标包组装计划。
    /// 只读操作：稀疏 Hotfix 的来源不足（基准 Full 摘要不可读、包目录缺失、内容两处都找不到、
    /// 或字节与清单声明不一致）在此直接抛出，此时尚未写出任何文件。
    /// </summary>
    private void AssembleTargetSet(PublishPlan plan)
    {
        if (!PackageTargetAssembler.TryCreatePlan(
                _request,
                plan.LocalFiles,
                plan.ServerFiles,
                plan.ServerPackageDir,
                out PackageAssemblyPlan assembly,
                out string error))
        {
            throw new InvalidOperationException(error);
        }

        plan.SetTargetFiles(assembly.TargetFiles, assembly.FileSources);
        plan.AssembledFromBaselineFull = assembly.AssembledFromBaselineFull;
        plan.BaselineFullPackageDir = assembly.BaselinePackageDir;
        plan.LocalSourcedCount = assembly.LocalSourcedCount;
        plan.ServerSourcedCount = assembly.ServerSourcedCount;
        plan.BaselineSourcedCount = assembly.BaselineSourcedCount;
        for (int i = 0; i < assembly.Messages.Count; i++)
            plan.AddMessage(assembly.Messages[i]);
    }

    /// <summary>
    /// 第 5 步：在隔离目录组装新包内容，优先复用服务器已有 Hash 内容。
    /// </summary>
    public void Stage()
    {
        RequirePlan();
        FileHelper.TryDeleteDirectory(_workRoot, true);
        FileHelper.EnsureDirectory(_stagedPackageDir);

        List<FileDigest> targetFiles = Plan.ResolveTargetFiles();
        List<FileDigest> serverFiles = Plan.ServerFiles;
        var serverByName = FileDiff.IndexByName(serverFiles);
        var serverByHash = FileDiff.IndexByHash(serverFiles);
        var expectedNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < targetFiles.Count; i++)
        {
            FileDigest expected = targetFiles[i];
            if (!expectedNames.Add(expected.Name))
                throw new InvalidOperationException($"目标包存在重名文件: {expected.Name}");

            string source = ResolveStagingSource(expected, serverByName, serverByHash);
            string targetPath = FYAssetPathUtility.JoinFilePath(_stagedPackageDir, expected.Name);
            FileHelper.CopyFile(source, targetPath, true);

            if (!FileDigest.TryCreate(targetPath, expected.Name, out FileDigest staged) || !staged.Matches(expected))
            {
                throw new IOException(
                    $"隔离目录文件摘要与目标包声明不一致: {expected.Name}（来源={source}）");
            }
        }

        Plan.AddMessage(
            $"隔离目录已组装: 目标文件={targetFiles.Count}, 上传={Plan.UploadCount}, 复用={Plan.ReuseCount}（其中 Hash 命中={Plan.ReusedByHash.Count}）");
        if (Plan.IsFullUpload)
            Plan.AddMessage($"服务器事实不可用，按完整上传处理: {Plan.DegradeReason}");
    }

    /// <summary>
    /// 第 6 步：校验新目录逻辑完整性。未通过时不得进入就位与写 PackageIndex。
    /// </summary>
    public void VerifyStaged()
    {
        RequirePlan();
        if (!FileHelper.DirectoryExists(_stagedPackageDir))
            throw new DirectoryNotFoundException($"隔离目录不存在，尚未组装: {_stagedPackageDir}");

        EnsureRequiredFilesPresent(Plan.ResolveTargetFiles(), _stagedPackageDir, "隔离目录");

        if (!PackageFileScanner.TryScan(_stagedPackageDir, out List<FileDigest> stagedFiles, out string scanError))
            throw new IOException($"隔离目录扫描失败: {scanError}");

        var expectedByName = FileDiff.IndexByName(Plan.ResolveTargetFiles());
        if (stagedFiles.Count != expectedByName.Count)
            throw new InvalidOperationException(
                $"隔离目录文件数与发布计划不一致: 实际={stagedFiles.Count}, 计划={expectedByName.Count}");

        for (int i = 0; i < stagedFiles.Count; i++)
        {
            FileDigest staged = stagedFiles[i];
            if (!expectedByName.TryGetValue(staged.Name, out FileDigest expected))
                throw new InvalidOperationException($"隔离目录出现计划外文件: {staged.Name}");
            if (!staged.Matches(expected))
                throw new InvalidOperationException($"隔离目录文件内容与计划不一致: {staged.Name}");
        }

        VerifyDeclaredByManifest(stagedFiles);
        Plan.AddMessage($"隔离目录逻辑完整性校验通过: 文件={stagedFiles.Count}");
    }

    /// <summary>
    /// 第 7 步：把校验过的隔离目录就位为新的包目录。
    /// 当前 PackageIndex 指向的同名目录视为已发布内容，不允许被覆盖。
    /// </summary>
    public void Apply()
    {
        RequirePlan();
        FileHelper.EnsureDirectory(_packagesRoot);

        if (FileHelper.DirectoryExists(_targetPackageDir))
        {
            if (IsCurrentPublishedPackage())
            {
                if (ContentMatchesTarget())
                {
                    // 同一包的重复发布是幂等的：内容一致时不替换，避免触碰已发布内容。
                    _alreadyPublished = true;
                    Plan.AddMessage($"目标包目录内容与本次发布一致，按已发布处理: {Identity.PackageName}");
                    return;
                }

                throw new InvalidOperationException(
                    $"拒绝覆盖当前 PackageIndex 指向的包目录（已发布内容不可变）: {_targetPackageDir}");
            }

            MoveDirectory(_targetPackageDir, _backupPackageDir);
            _targetBackedUp = true;
        }

        MoveDirectory(_stagedPackageDir, _targetPackageDir);
        _targetReplaced = true;
    }

    /// <summary>
    /// 第 8 步：生成并上传新的 PackageIndex。必须在内容就位并通过校验之后执行。
    /// </summary>
    public void WritePackageIndex()
    {
        RequirePlan();
        FileHelper.EnsureDirectory(_serverRoot);

        _indexExisted = FileHelper.Exists(_packageIndexPath);
        if (_indexExisted)
        {
            FileHelper.CopyFile(_packageIndexPath, _backupIndexPath, true);
            _indexBackedUp = true;
        }

        var packageIndex = new PackageIndex
        {
            LatestPackage = Identity.PackageName,
            LatestVersion = Identity.Version,
            BackendMode = string.IsNullOrEmpty(Identity.BackendId) ? _request.BackendKey : Identity.BackendId
        };

        FileHelper.WriteAllTextAtomic(_packageIndexPath, SerializationUtility.SerializeToJson(packageIndex, true));
        _indexWritten = true;
        Plan.AddMessage($"PackageIndex 已最后写入: {_packageIndexPath}");
    }

    /// <summary>发布成功：记录发布缓存并释放隔离工作区。</summary>
    public void Commit()
    {
        if (_completed)
            return;

        _completed = true;
        SavePublishCache();
        CleanupWorkRoot();
    }

    /// <summary>发布失败：按逆序补偿写索引与目录替换，再释放隔离工作区。</summary>
    public void Rollback()
    {
        if (_completed)
            return;

        try
        {
            RestorePackageIndex();
            RestoreTargetPackage();
        }
        finally
        {
            _completed = true;
            CleanupWorkRoot();
        }
    }

    public void Dispose()
    {
        if (!_completed)
            Rollback();
    }

    /// <summary>读取服务器事实：索引 → 指向的包目录 → 后端 Manifest；任一步失败都退化为完整上传。</summary>
    private void ReadServerFacts(PublishPlan plan)
    {
        PackageIndex serverIndex = TryReadServerIndex(out string indexError);
        if (serverIndex == null)
        {
            Degrade(plan, $"服务器 PackageIndex 不可用: {indexError}");
            return;
        }

        plan.ServerIndexReadable = true;
        plan.ServerLatestPackage = serverIndex.LatestPackage ?? string.Empty;
        if (string.IsNullOrEmpty(serverIndex.LatestPackage))
        {
            Degrade(plan, "服务器 PackageIndex 未指向任何包");
            return;
        }

        if (serverIndex.LatestPackage.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
        {
            Degrade(plan, $"服务器 PackageIndex 的包名不是合法目录名: '{serverIndex.LatestPackage}'");
            return;
        }

        string serverPackageDir = FYAssetPathUtility.JoinFilePath(_packagesRoot, serverIndex.LatestPackage);
        plan.ServerPackageDir = serverPackageDir;
        if (!FileHelper.DirectoryExists(serverPackageDir))
        {
            Degrade(plan, $"服务器包目录不存在: {serverPackageDir}");
            return;
        }

        if (!_request.ManifestReader.TryReadContentDigests(serverPackageDir, out IReadOnlyList<FileDigest> contents, out string manifestError))
        {
            Degrade(plan, $"服务器 Manifest 损坏或缺失: {manifestError}");
            return;
        }

        var serverFiles = new List<FileDigest>();
        IReadOnlyList<string> required = _request.ManifestReader.RequiredPackageFileNames;
        for (int i = 0; i < required.Count; i++)
        {
            string name = required[i];
            if (string.IsNullOrEmpty(name))
                continue;

            string path = FYAssetPathUtility.JoinFilePath(serverPackageDir, name);
            if (!FileDigest.TryCreate(path, name.Replace('\\', '/'), out FileDigest digest))
            {
                Degrade(plan, $"服务器包缺少清单文件或不可读: {name}");
                return;
            }

            serverFiles.Add(digest);
        }

        if (contents != null)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i].IsComplete)
                    serverFiles.Add(contents[i]);
            }
        }

        if (!VerifyServerContentMatchesManifest(serverPackageDir, contents, out string driftError))
        {
            Degrade(plan, $"服务器包内容与其 Manifest 不一致: {driftError}");
            return;
        }

        plan.ServerManifestReadable = true;
        plan.ServerFiles = serverFiles;
        plan.AddMessage(
            $"服务器事实: 包={serverIndex.LatestPackage}, 版本={serverIndex.LatestVersion.GetReleaseVersionString()}, "
            + $"声明文件={serverFiles.Count}");
    }

    /// <summary>服务器包内实际存在的内容文件必须与 Manifest 声明一致，否则该事实不可复用。</summary>
    private bool VerifyServerContentMatchesManifest(
        string serverPackageDir,
        IReadOnlyList<FileDigest> declaredContents,
        out string error)
    {
        error = string.Empty;
        string contentDirectoryName = _request.ManifestReader.ContentDirectoryName;
        if (string.IsNullOrEmpty(contentDirectoryName))
            return true;

        string contentDirectory = FYAssetPathUtility.JoinFilePath(serverPackageDir, contentDirectoryName);
        if (!FileHelper.DirectoryExists(contentDirectory))
            return true;

        if (!PackageFileScanner.TryScanContentDirectory(
                serverPackageDir,
                contentDirectory,
                out List<FileDigest> physical,
                out string scanError))
        {
            error = scanError;
            return false;
        }

        var declaredByName = FileDiff.IndexByName(declaredContents);
        for (int i = 0; i < physical.Count; i++)
        {
            FileDigest file = physical[i];
            if (!declaredByName.TryGetValue(file.Name, out FileDigest declared))
            {
                error = $"存在清单未声明的文件: {file.Name}";
                return false;
            }

            if (!file.Matches(declared))
            {
                error = $"文件与清单摘要不一致: {file.Name}";
                return false;
            }
        }

        return true;
    }

    /// <summary>把计划标记为完整上传退化；服务器声明集合清空，避免使用不可信事实。</summary>
    private static void Degrade(PublishPlan plan, string reason)
    {
        plan.DegradeReason = reason;
        plan.ServerFiles = new List<FileDigest>();
        plan.ServerManifestReadable = false;
        plan.AddMessage($"退化为完整上传: {reason}");
    }

    private PackageIndex TryReadServerIndex(out string error)
    {
        error = string.Empty;
        if (!FileHelper.Exists(_packageIndexPath))
        {
            error = $"文件不存在: {_packageIndexPath}";
            return null;
        }

        try
        {
            string json = FileHelper.ReadAllText(_packageIndexPath);
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (index == null)
            {
                error = $"反序列化结果为空: {_packageIndexPath}";
                return null;
            }

            if (!VersionNumber.JsonHasObjectField(json, nameof(PackageIndex.LatestVersion)))
            {
                error = $"PackageIndex 缺少 LatestVersion 对象: {_packageIndexPath}";
                return null;
            }

            return index;
        }
        catch (Exception ex)
        {
            error = $"{_packageIndexPath} — {ex.Message}";
            return null;
        }
    }

    /// <summary>决定单个目标文件的来源：优先同名/Hash 复用服务器内容，其次组装计划指定的来源（本地包或基准 Full）。</summary>
    private string ResolveStagingSource(
        in FileDigest target,
        Dictionary<string, FileDigest> serverByName,
        Dictionary<string, FileDigest> serverByHash)
    {
        if (serverByName.TryGetValue(target.Name, out FileDigest sameName)
            && string.Equals(sameName.Hash, target.Hash, StringComparison.Ordinal)
            && sameName.CRC == target.CRC
            && sameName.Size == target.Size)
        {
            string reusePath = FYAssetPathUtility.JoinFilePath(Plan.ServerPackageDir, target.Name);
            if (FileDigest.TryCreate(reusePath, target.Name, out FileDigest actual) && actual.Matches(target))
                return reusePath;
        }

        string hashKey = FileDiff.HashKey(target.Hash, target.Size);
        if (serverByHash.TryGetValue(hashKey, out FileDigest sameHash)
            && !string.Equals(sameHash.Name, target.Name, StringComparison.Ordinal))
        {
            string reusePath = FYAssetPathUtility.JoinFilePath(Plan.ServerPackageDir, sameHash.Name);
            if (FileDigest.TryCreate(reusePath, sameHash.Name, out FileDigest actual) && actual.Matches(sameHash))
            {
                Plan.ReusedByHash.Add(target);
                Plan.AddMessage($"Hash 命中复用服务器内容: {target.Name} ← {sameHash.Name}");
                return reusePath;
            }
        }

        if (Plan.FileSources.TryGetValue(target.Name, out string plannedSource) && !string.IsNullOrEmpty(plannedSource))
            return plannedSource;

        return FYAssetPathUtility.JoinFilePath(_request.SourcePackageDir, target.Name);
    }

    /// <summary>必填清单文件必须存在且非空，否则该目录无法作为包使用。</summary>
    private void EnsureRequiredFilesPresent(IReadOnlyList<FileDigest> files, string rootDir, string description)
    {
        IReadOnlyList<string> required = _request.ManifestReader.RequiredPackageFileNames;
        if (required == null || required.Count == 0)
            throw new InvalidOperationException("后端未声明包清单文件名，无法校验包头完整性。");

        var byName = FileDiff.IndexByName(files);
        for (int i = 0; i < required.Count; i++)
        {
            string name = required[i];
            if (string.IsNullOrEmpty(name))
                continue;

            string normalized = name.Replace('\\', '/');
            if (!byName.ContainsKey(normalized))
                throw new FileNotFoundException($"{description}缺少清单文件: {name}", FYAssetPathUtility.JoinFilePath(rootDir, name));
        }
    }

    /// <summary>
    /// 包内容目录内的每个物理文件都必须被后端清单声明且摘要一致，
    /// 否则客户端能下载到文件却永远读不到它（清单才是下载与加载的契约）。
    /// 清单声明但未随包发布的文件是允许的：Hotfix 包只携带相对 Full 累计变化的内容。
    /// </summary>
    private void VerifyDeclaredByManifest(IReadOnlyList<FileDigest> stagedFiles)
    {
        if (!_request.ManifestReader.TryReadContentDigests(_stagedPackageDir, out IReadOnlyList<FileDigest> declared, out string error))
            throw new InvalidOperationException($"隔离目录清单不可解析: {error}");

        var declaredByName = FileDiff.IndexByName(declared);
        string contentPrefix = ResolveContentPrefix();

        for (int i = 0; i < stagedFiles.Count; i++)
        {
            FileDigest staged = stagedFiles[i];
            if (contentPrefix.Length == 0 || !staged.Name.StartsWith(contentPrefix, StringComparison.Ordinal))
                continue;

            if (!declaredByName.TryGetValue(staged.Name, out FileDigest declaredDigest))
                throw new InvalidOperationException($"包内容文件未被清单声明: {staged.Name}");

            if (!string.Equals(declaredDigest.Hash, staged.Hash, StringComparison.Ordinal)
                || declaredDigest.CRC != staged.CRC
                || declaredDigest.Size != staged.Size)
            {
                throw new InvalidOperationException($"包内容文件与清单摘要不一致: {staged.Name}");
            }
        }
    }

    /// <summary>内容目录前缀（含结尾 '/'）；内容目录名为空时返回空串表示不做内容归属校验。</summary>
    private string ResolveContentPrefix()
    {
        string directory = _request.ManifestReader.ContentDirectoryName;
        if (string.IsNullOrEmpty(directory))
            return string.Empty;

        return directory.Replace('\\', '/').TrimEnd('/') + "/";
    }

    private bool IsCurrentPublishedPackage()
    {
        PackageIndex serverIndex = TryReadServerIndex(out _);
        return serverIndex != null
               && string.Equals(serverIndex.LatestPackage, Identity.PackageName, StringComparison.Ordinal);
    }

    /// <summary>目标目录内容是否与本次计划完全一致（幂等重复发布判定）。</summary>
    private bool ContentMatchesTarget()
    {
        if (!PackageFileScanner.TryScan(_targetPackageDir, out List<FileDigest> existing, out _))
            return false;

        var expectedByName = FileDiff.IndexByName(Plan.ResolveTargetFiles());
        if (existing.Count != expectedByName.Count)
            return false;

        for (int i = 0; i < existing.Count; i++)
        {
            if (!expectedByName.TryGetValue(existing[i].Name, out FileDigest expected)
                || !existing[i].Matches(expected))
            {
                return false;
            }
        }

        return true;
    }

    private void DescribeCacheDrift(PublishPlan plan)
    {
        if (string.IsNullOrEmpty(_request.PublishCachePath))
            return;

        PublishCache cache = PublishCacheStore.Load(_request.PublishCachePath);
        if (cache == null)
        {
            plan.AddMessage("本地发布缓存缺失或损坏，本次完全以服务器事实为准");
            return;
        }

        string mismatch = PublishCacheStore.DescribeMismatch(cache.Files, plan.ServerFiles);
        if (!string.IsNullOrEmpty(mismatch))
            plan.AddMessage(mismatch + "（仅提示，不改变发布决定）");
    }

    private void SavePublishCache()
    {
        if (string.IsNullOrEmpty(_request.PublishCachePath))
            return;

        PublishCacheStore.Save(_request.PublishCachePath, new PublishCache
        {
            BackendId = string.IsNullOrEmpty(Identity.BackendId) ? _request.BackendKey : Identity.BackendId,
            TargetId = _request.TargetId ?? string.Empty,
            Files = new List<FileDigest>(Plan.ResolveTargetFiles()),
            LastPackageName = Identity.PackageName,
            PublishedAtUtc = DateTime.UtcNow.ToString("o")
        });
    }

    private void RestorePackageIndex()
    {
        if (!_indexWritten)
            return;

        if (_indexBackedUp && FileHelper.Exists(_backupIndexPath))
        {
            FileHelper.CopyFile(_backupIndexPath, _packageIndexPath, true);
            return;
        }

        if (!_indexExisted)
            FileHelper.TryDelete(_packageIndexPath);
    }

    private void RestoreTargetPackage()
    {
        if (_alreadyPublished || !_targetReplaced)
        {
            RestoreBackedUpTargetIfNeeded();
            return;
        }

        if (FileHelper.DirectoryExists(_targetPackageDir))
            FileHelper.TryDeleteDirectory(_targetPackageDir, true);
        RestoreBackedUpTargetIfNeeded();
    }

    private void RestoreBackedUpTargetIfNeeded()
    {
        if (!_targetBackedUp || !FileHelper.DirectoryExists(_backupPackageDir))
            return;
        MoveDirectory(_backupPackageDir, _targetPackageDir);
    }

    private void CleanupWorkRoot()
    {
        FileHelper.TryDeleteDirectory(_workRoot, true);
        string parent = Path.GetDirectoryName(_workRoot);
        if (FileHelper.DirectoryExists(parent)
            && FileHelper.GetDirectories(parent).Length == 0
            && FileHelper.GetFiles(parent).Length == 0)
        {
            FileHelper.TryDeleteDirectory(parent, false);
        }
    }

    private void RequirePlan()
    {
        if (Plan == null)
            throw new InvalidOperationException("发布计划尚未建立，请先调用 CreatePlan。");
    }

    private static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (!FileHelper.DirectoryExists(sourceDir))
            throw new DirectoryNotFoundException($"待移动目录不存在: {sourceDir}");

        FileHelper.EnsureDirectory(Path.GetDirectoryName(targetDir));
        if (FileHelper.DirectoryExists(targetDir))
            FileHelper.TryDeleteDirectory(targetDir, true);
        Directory.Move(sourceDir, targetDir);
    }
}
#endif

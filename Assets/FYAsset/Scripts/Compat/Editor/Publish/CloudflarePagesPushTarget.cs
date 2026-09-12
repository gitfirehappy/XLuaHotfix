#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 目录型目标不可用时的 Compat 发布胶水：先在本地组装服务根镜像，再通过 Wrangler 部署整个服务根目录。
/// </summary>
/// <remarks>
/// 本目标无法读取服务器上的既有 PackageIndex 与 Manifest，因此 BuildPublisher 会先按完整上传组装包目录；
/// 本类负责把该目录就位到服务根镜像，并且最后写入 PackageIndex，随后才执行 Wrangler 部署。
/// 部署失败时恢复旧 PackageIndex 与旧镜像内容：
/// 1. 旧同名包目录先备份再镜像，失败时原样放回（F09：失败不得把已存在的旧同名包删掉）；
/// 2. 本次新建的包目录（此前不存在）失败时删除，不留未部署的半成品；
/// 3. 备份放在服务根之外的临时目录：Wrangler 部署的是整个服务根，工作目录不能被一并上传。
/// 真实 Cloudflare 部署需要取得网络授权后实测；本类通过 <see cref="IWranglerCommandRunner"/> 接受注入，
/// 供发布门禁驱动失败/成功场景。
/// </remarks>
public sealed class CloudflarePagesPushTarget : IPushTarget
{
    private const int WranglerTimeoutMilliseconds = 10 * 60 * 1000;
    private const string HeadersFileName = "_headers";
    private readonly PushTargetConfig _config;
    private readonly IWranglerCommandRunner _runner;

    public string Id => _config.TargetId;

    public CloudflarePagesPushTarget(PushTargetConfig config)
        : this(config, ProcessWranglerCommandRunner.Instance)
    {
    }

    /// <summary>注入 Wrangler 调用接缝的构造：发布门禁用它注入部署失败/成功场景。</summary>
    public CloudflarePagesPushTarget(PushTargetConfig config, IWranglerCommandRunner runner)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public PushReceipt Push(PushPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Request == null)
            return Fail("PushPayload 缺少发布请求。", string.Empty);
        if (string.IsNullOrEmpty(payload.StagedPackageDir) || !Directory.Exists(payload.StagedPackageDir))
            return Fail($"待上传包目录不存在: {payload.StagedPackageDir}", string.Empty);

        string backendKey = payload.Request.BackendKey;
        if (!payload.Request.TryResolveIdentity(out PackageBuildIdentity identity, out string identityError))
            return Fail(identityError, string.Empty);

        string projectName = FYAssetSettings.Instance.ProjectName;
        if (string.IsNullOrWhiteSpace(projectName))
            return Fail("FYAssetSettings.ProjectName is required for Cloudflare Pages.", string.Empty);

        string wranglerPath = _runner.ResolveExecutable();
        if (string.IsNullOrEmpty(wranglerPath))
            return Fail("Wrangler was not found on PATH. Install and authenticate it before Cloudflare Push.", string.Empty);

        WranglerCommandResult versionResult = _runner.Run(wranglerPath, "--version", 30_000);
        if (!versionResult.Success)
            return Fail($"Wrangler preflight failed: {versionResult.Message}", string.Empty);

        string serviceRoot = _config.ResolveServiceRoot();
        string backendRoot = _config.ResolveBackendRoot(backendKey);
        string headersPath = FYAssetPathUtility.JoinFilePath(serviceRoot, HeadersFileName);
        bool headersExisted = FileHelper.Exists(headersPath);
        string oldHeaders = headersExisted ? FileHelper.ReadAllText(headersPath) : string.Empty;

        string packageIndexPath = FYAssetPathUtility.JoinFilePath(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        bool indexExisted = FileHelper.Exists(packageIndexPath);
        string oldIndex = indexExisted ? FileHelper.ReadAllText(packageIndexPath) : string.Empty;
        string targetPackageDir = FYAssetPathUtility.JoinFilePath(
            FYAssetPathUtility.JoinFilePath(backendRoot, payload.PackagesFolderName),
            identity.PackageName);

        // 旧同名包目录必须先备份：失败时要把它原样放回，而不是删掉（F09 缺陷形态）。
        string backupPackageDir = string.Empty;
        bool targetExisted = FileHelper.DirectoryExists(targetPackageDir);
        bool packageTouched = false;

        try
        {
            if (targetExisted)
            {
                backupPackageDir = CreatePackageBackupPath(identity.PackageName);
                CopyDirectory(targetPackageDir, backupPackageDir);
            }

            // 镜像会先删除目标目录：从这里开始目标目录可能已被改动，失败补偿必须回滚镜像。
            packageTouched = true;
            MirrorPackage(payload.StagedPackageDir, targetPackageDir);

            // PackageIndex 必须在包内容就位之后、部署之前写入。
            if (!string.IsNullOrEmpty(payload.PackageIndexJson))
                FileHelper.WriteAllTextAtomic(packageIndexPath, payload.PackageIndexJson);

            WriteHeaders(headersPath);

            string arguments = BuildDeployArguments(serviceRoot, projectName);
            WranglerCommandResult deployResult = _runner.Run(wranglerPath, arguments, WranglerTimeoutMilliseconds);
            if (!deployResult.Success)
            {
                RestoreFailedPush(
                    packageIndexPath, indexExisted, oldIndex,
                    headersPath, headersExisted, oldHeaders,
                    targetPackageDir, targetExisted, backupPackageDir, packageTouched);
                return Fail($"Wrangler deploy failed: {deployResult.Message}", backendRoot);
            }

            Debug.Log($"[CloudflarePagesPushTarget] {deployResult.Message}");
            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = _config.GetHotfixUrl(backendKey),
                PushedAtUtc = DateTime.UtcNow.ToString("o"),
                DegradedToFullUpload = true
            };
        }
        catch (Exception ex)
        {
            RestoreFailedPush(
                packageIndexPath, indexExisted, oldIndex,
                headersPath, headersExisted, oldHeaders,
                targetPackageDir, targetExisted, backupPackageDir, packageTouched);
            Debug.LogError($"[CloudflarePagesPushTarget] Push 失败：{ex}");
            return Fail(ex.Message, backendRoot);
        }
        finally
        {
            if (!string.IsNullOrEmpty(backupPackageDir))
                FileHelper.TryDeleteDirectory(Path.GetDirectoryName(backupPackageDir), true);
        }
    }

    /// <summary>
    /// 失败补偿：恢复旧 PackageIndex 与旧服务根镜像内容。
    /// 旧同名包目录原样放回；本次新建的包目录（镜像前不存在）删除，避免留下未部署的半成品包。
    /// </summary>
    private static void RestoreFailedPush(
        string packageIndexPath,
        bool indexExisted,
        string oldIndex,
        string headersPath,
        bool headersExisted,
        string oldHeaders,
        string targetPackageDir,
        bool targetExisted,
        string backupPackageDir,
        bool packageTouched)
    {
        RestoreIndex(packageIndexPath, indexExisted, oldIndex);
        RestoreHeaders(headersPath, headersExisted, oldHeaders);

        if (!packageTouched)
            return;

        if (targetExisted && !string.IsNullOrEmpty(backupPackageDir) && FileHelper.DirectoryExists(backupPackageDir))
        {
            FileHelper.TryDeleteDirectory(targetPackageDir, true);
            CopyDirectory(backupPackageDir, targetPackageDir);
            return;
        }

        if (!targetExisted)
            FileHelper.TryDeleteDirectory(targetPackageDir, true);
    }

    /// <summary>
    /// 旧同名包目录的备份路径：放在服务根之外的临时目录。
    /// 不能放在服务根内——Wrangler 部署的是整个服务根，遗留在其内的备份目录会被一并上传。
    /// </summary>
    private static string CreatePackageBackupPath(string packageName)
    {
        return FYAssetPathUtility.JoinFilePath(
            FYAssetPathUtility.JoinFilePath(
                Path.GetTempPath(),
                "fyasset_cloudflare_push",
                Guid.NewGuid().ToString("N")),
            packageName);
    }

    /// <summary>把目录内所有文件复制到目标目录（同名覆盖）。</summary>
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        FileHelper.EnsureDirectory(targetDir);
        string[] files = FileHelper.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relative = FYAssetPathUtility.GetRelativeFilePath(sourceDir, files[i]);
            FileHelper.CopyFile(files[i], FYAssetPathUtility.JoinFilePath(targetDir, relative), true);
        }
    }

    private static void MirrorPackage(string stagedPackageDir, string targetPackageDir)
    {
        FileHelper.EnsureDirectory(Path.GetDirectoryName(targetPackageDir));
        if (FileHelper.DirectoryExists(targetPackageDir))
            FileHelper.TryDeleteDirectory(targetPackageDir, true);

        string[] files = FileHelper.GetFiles(stagedPackageDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relative = FYAssetPathUtility.GetRelativeFilePath(stagedPackageDir, files[i]);
            FileHelper.CopyFile(files[i], FYAssetPathUtility.JoinFilePath(targetPackageDir, relative), true);
        }
    }

    private static void RestoreIndex(string path, bool existed, string content)
    {
        if (existed)
            FileHelper.WriteAllTextAtomic(path, content);
        else
            FileHelper.TryDelete(path);
    }

    private static void WriteHeaders(string path)
    {
        const string content =
            "/AA/PackageIndex.json\n" +
            "  Cache-Control: no-store\n\n" +
            "/AB/PackageIndex.json\n" +
            "  Cache-Control: no-store\n\n" +
            "/AA/Packages/*\n" +
            "  Cache-Control: public, max-age=31536000, immutable\n\n" +
            "/AB/Packages/*\n" +
            "  Cache-Control: public, max-age=31536000, immutable\n";
        FileHelper.WriteAllTextAtomic(path, content);
    }

    private static void RestoreHeaders(string path, bool existed, string content)
    {
        if (existed)
            FileHelper.WriteAllTextAtomic(path, content);
        else
            FileHelper.TryDelete(path);
    }

    private PushReceipt Fail(string reason, string location)
    {
        Debug.LogError($"[CloudflarePagesPushTarget] {reason}");
        return new PushReceipt
        {
            Success = false,
            TargetId = Id,
            TargetLocation = location,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }

    /// <summary>
    /// Wrangler 部署命令参数。供自检与测试验证或复用。
    /// </summary>
    internal static string BuildDeployArguments(string serviceRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(serviceRoot))
            throw new ArgumentException("Cloudflare service root is empty.", nameof(serviceRoot));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("FYAssetSettings.ProjectName is empty.", nameof(projectName));

        return $"pages deploy {QuoteArgument(serviceRoot)} --project-name {QuoteArgument(projectName)} --branch main";
    }

    /// <summary>
    /// 在 PATH 上查找可执行文件；找不到返回空串，由调用方决定报错。
    /// 实际实现位于 <see cref="ProcessWranglerCommandRunner"/>，本方法保留为自检入口。
    /// </summary>
    internal static string FindExecutableOnPath(string command) =>
        ProcessWranglerCommandRunner.FindExecutableOnPath(command);

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}

/// <summary>
/// 为 CLI 与测试创建发布目标：包含 LocalDirectory 与 Compat 侧 Cloudflare Pages 部署胶水。
/// 构建窗口的发布面板只支持目录型目标，与该入口保持分离。
/// </summary>
public static class CompatPushTargetFactory
{
    public static IPushTarget CreateFull(PushTargetConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return config.Type switch
        {
            PushTargetType.LocalDirectory => new LocalDirectoryPushTarget(config),
            PushTargetType.CloudflarePages => new CloudflarePagesPushTarget(config),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Type), config.Type, "Unsupported push target type."),
        };
    }
}
#endif

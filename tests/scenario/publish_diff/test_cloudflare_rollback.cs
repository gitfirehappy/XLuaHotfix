using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Cloudflare 发布回滚门禁：部署失败恢复旧索引和服务根镜像，稀疏 Hotfix 在上传前由本地基准 Full 补齐。
/// </summary>
internal static class CloudflareRollbackTests
{
    private const string BackendKey = "AB";
    private const string OldPackageName = "Build_20260909120000_1.0.0";

    public static void Run()
    {
        DeployFailureRestoresIndexAndExistingSameNamePackage();
        DeployFailureRemovesNewPackageDirectory();
        SuccessfulDeployMirrorsPackageAndIndex();
        SparseHotfixIsAssembledBeforeCloudflareUpload();
        SparseHotfixWithoutBaselineDoesNotReachCloudflare();
    }

    /// <summary>非目录型目标没有服务器事实：稀疏 Hotfix 必须先补齐为完整目标包，再交给 Wrangler 上传。</summary>
    private static void SparseHotfixIsAssembledBeforeCloudflareUpload()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixIsAssembledBeforeCloudflareUpload));
        var fixture = new CloudflarePushFixture(workspace);
        fixture.WriteOldIndex("old-index");
        SparsePackageFixture sparse = CreateSparsePackageFixture(workspace);

        var runner = TestWranglerCommandRunner.DeploySucceeds();
        PushReceipt receipt = fixture.PublishThroughPublisher(
            sparse.HotfixDir, sparse.HotfixPackageName, runner,
            TestFullPackageBaselineSource.Available(sparse.BaseFullDir));

        Check.True(receipt.Success, $"Cloudflare 完整上传必须用组装好的完整目标包: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "非目录型目标没有服务器事实，必须标记为完整上传退化");
        Check.True(receipt.AssembledFromBaselineFull, "回执必须标记由基准 Full 补齐");
        Check.True(runner.HasDeployCall, "补齐成功后才应执行部署");

        string published = fixture.TargetPackageDir(sparse.HotfixPackageName);
        Check.Equal("content-a", File.ReadAllText(Path.Combine(published, "bundles", "a.bundle")),
            "上传的包必须包含基准 Full 提供的未变化内容");
        Check.Equal("content-b", File.ReadAllText(Path.Combine(published, "bundles", "b.bundle")),
            "上传的包必须包含本地 Hotfix 的变化内容");
        Check.Equal(sparse.HotfixPackageName,
            SerializationUtility.DeserializeJson<PackageIndex>(File.ReadAllText(fixture.PackageIndexPath)).LatestPackage,
            "PackageIndex 必须指向本次 Hotfix 包");
    }

    /// <summary>来源不足时不得把稀疏包上传到 Cloudflare：不执行部署，也不留下任何镜像变更。</summary>
    private static void SparseHotfixWithoutBaselineDoesNotReachCloudflare()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixWithoutBaselineDoesNotReachCloudflare));
        var fixture = new CloudflarePushFixture(workspace);
        fixture.WriteOldIndex("old-index");
        SparsePackageFixture sparse = CreateSparsePackageFixture(workspace);
        byte[] indexBefore = File.ReadAllBytes(fixture.PackageIndexPath);

        var runner = TestWranglerCommandRunner.DeploySucceeds();
        PushReceipt receipt = fixture.PublishThroughPublisher(
            sparse.HotfixDir, sparse.HotfixPackageName, runner, baselineSource: null);

        Check.True(!receipt.Success, "缺少基准 Full 时不得上传稀疏 Hotfix");
        Check.Contains(receipt.FailureReason, "来源不足", "失败原因必须指出来源不足");
        Check.True(!runner.HasDeployCall, "来源不足时不得执行部署");
        Check.True(!Directory.Exists(fixture.TargetPackageDir(sparse.HotfixPackageName)),
            "来源不足时不得在服务根镜像里留下新包目录");
        Check.True(File.ReadAllBytes(fixture.PackageIndexPath).AsSpan().SequenceEqual(indexBefore),
            "来源不足时不得改写 PackageIndex");
        Check.True(!File.Exists(Path.Combine(fixture.ServiceRoot, "_headers")), "来源不足时不得写 _headers");
    }

    /// <summary>构造稀疏 Hotfix 夹具：基准 Full 含 a+b，Hotfix 只携带 b 但声明 a+b。</summary>
    private static SparsePackageFixture CreateSparsePackageFixture(TempWorkspace workspace)
    {
        const string baseFullName = "Build_20260909120000_1.0.0";
        const string hotfixName = "Build_20260909120001_1.0.1";

        string baseFullDir = workspace.CreateDir("local/" + baseFullName);
        FileFixtures.Write(baseFullDir, "bundles/a.bundle", "content-a");
        FileFixtures.Write(baseFullDir, TestManifestReader.ManifestFileName, "full-manifest");

        string hotfixDir = workspace.CreateDir("local/" + hotfixName);
        FileFixtures.Write(hotfixDir, "bundles/b.bundle", "content-b");
        TestManifestReader.WriteManifest(hotfixDir, new List<FileHelper.FileDigest>
        {
            FileFixtures.Digest(baseFullDir, "bundles/a.bundle"),
            FileFixtures.Digest(hotfixDir, "bundles/b.bundle")
        });

        return new SparsePackageFixture(baseFullDir, hotfixDir, hotfixName);
    }

    /// <summary>部署失败后：旧索引字节恢复、旧同名包目录仍在且内容不变、失败回执明确。</summary>
    private static void DeployFailureRestoresIndexAndExistingSameNamePackage()
    {
        using var workspace = new TempWorkspace(nameof(DeployFailureRestoresIndexAndExistingSameNamePackage));
        var fixture = new CloudflarePushFixture(workspace);
        fixture.CreateOldPackage(OldPackageName, "old-content");
        fixture.WriteOldIndex("old-index");

        Dictionary<string, byte[]> packageBefore = PublishContext.SnapshotDirectory(fixture.TargetPackageDir(OldPackageName));
        byte[] indexBefore = File.ReadAllBytes(fixture.PackageIndexPath);
        string stagedDir = fixture.CreateStagedPackage("new-content");

        var runner = TestWranglerCommandRunner.DeployFails("deploy boom");
        PushReceipt receipt = fixture.Push(stagedDir, OldPackageName, "new-index", runner);

        Check.True(!receipt.Success, "Wrangler 部署失败时发布必须失败");
        Check.Contains(receipt.FailureReason, "Wrangler deploy failed", "失败回执必须明确指出部署失败");
        Check.Contains(receipt.FailureReason, "deploy boom", "失败回执必须带出 Wrangler 输出");
        Check.True(File.ReadAllBytes(fixture.PackageIndexPath).AsSpan().SequenceEqual(indexBefore),
            "部署失败必须把旧 PackageIndex 按字节恢复");
        Check.True(Directory.Exists(fixture.TargetPackageDir(OldPackageName)),
            "部署失败绝不能删掉已存在的旧同名包目录");
        CheckSnapshotEquals(packageBefore, PublishContext.SnapshotDirectory(fixture.TargetPackageDir(OldPackageName)),
            "部署失败必须恢复旧同名包目录的全部内容");
        Check.True(!File.Exists(Path.Combine(fixture.ServiceRoot, "_headers")),
            "部署失败必须恢复旧服务根镜像（_headers 此前不存在就不得留下）");
        Check.True(runner.HasDeployCall, "部署命令确实被执行过（失败来自部署结果而不是前置检查）");
    }

    /// <summary>部署失败后：本次新建的包目录（此前不存在）必须被清除，不留未部署的半成品。</summary>
    private static void DeployFailureRemovesNewPackageDirectory()
    {
        using var workspace = new TempWorkspace(nameof(DeployFailureRemovesNewPackageDirectory));
        var fixture = new CloudflarePushFixture(workspace);
        fixture.CreateOldPackage(OldPackageName, "old-content");
        fixture.WriteOldIndex("old-index");

        const string newPackageName = "Build_20260909120001_1.0.1";
        byte[] indexBefore = File.ReadAllBytes(fixture.PackageIndexPath);
        string stagedDir = fixture.CreateStagedPackage("new-content");

        var runner = TestWranglerCommandRunner.DeployFails("deploy boom");
        PushReceipt receipt = fixture.Push(stagedDir, newPackageName, "new-index", runner);

        Check.True(!receipt.Success, "Wrangler 部署失败时发布必须失败");
        Check.True(!Directory.Exists(fixture.TargetPackageDir(newPackageName)),
            "部署失败必须清除本次新建的包目录");
        Check.True(Directory.Exists(fixture.TargetPackageDir(OldPackageName)), "旧包目录不得被牵连删除");
        Check.True(File.ReadAllBytes(fixture.PackageIndexPath).AsSpan().SequenceEqual(indexBefore),
            "部署失败必须把旧 PackageIndex 按字节恢复");
    }

    /// <summary>部署成功：包内容与索引按发布意图就位（对照用例，证明门禁不是恒失败）。</summary>
    private static void SuccessfulDeployMirrorsPackageAndIndex()
    {
        using var workspace = new TempWorkspace(nameof(SuccessfulDeployMirrorsPackageAndIndex));
        var fixture = new CloudflarePushFixture(workspace);
        fixture.CreateOldPackage(OldPackageName, "old-content");
        fixture.WriteOldIndex("old-index");
        string stagedDir = fixture.CreateStagedPackage("new-content");

        var runner = TestWranglerCommandRunner.DeploySucceeds();
        PushReceipt receipt = fixture.Push(stagedDir, OldPackageName, "new-index", runner);

        Check.True(receipt.Success, $"部署成功时发布应成功: {receipt.FailureReason}");
        Check.Equal("new-index", File.ReadAllText(fixture.PackageIndexPath), "部署成功必须写入新 PackageIndex");
        Check.Equal("new-content",
            File.ReadAllText(Path.Combine(fixture.TargetPackageDir(OldPackageName), "bundles", "a.bundle")),
            "部署成功必须镜像新包内容");
        Check.True(File.Exists(Path.Combine(fixture.ServiceRoot, "_headers")), "部署成功必须写出 _headers");
    }

    private static void CheckSnapshotEquals(
        Dictionary<string, byte[]> expected,
        Dictionary<string, byte[]> actual,
        string message)
    {
        Check.Equal(expected.Count, actual.Count, message + "（文件数）");
        foreach (KeyValuePair<string, byte[]> pair in expected)
        {
            Check.True(actual.TryGetValue(pair.Key, out byte[] bytes), $"{message}（缺少 {pair.Key}）");
            Check.True(bytes.AsSpan().SequenceEqual(pair.Value), $"{message}（内容变化 {pair.Key}）");
        }
    }
}

/// <summary>Cloudflare 发布场景夹具：服务根镜像、旧包、旧索引与待上传包目录。</summary>
internal sealed class CloudflarePushFixture
{
    private const string BackendKey = "AB";

    private readonly TempWorkspace _workspace;
    private readonly PushTargetConfig _config;

    public CloudflarePushFixture(TempWorkspace workspace)
    {
        _workspace = workspace;
        ServiceRoot = workspace.CreateDir("service");
        _config = new PushTargetConfig
        {
            TargetId = "cloudflare-test",
            Name = "cloudflare-test",
            Type = PushTargetType.CloudflarePages,
            Path = ServiceRoot,
            PublicBaseUrl = "https://example.test"
        };
    }

    public string ServiceRoot { get; }

    public string BackendRoot => Path.Combine(ServiceRoot, BackendKey);

    public string PackageIndexPath => Path.Combine(BackendRoot, "PackageIndex.json");

    public string TargetPackageDir(string packageName) =>
        Path.Combine(BackendRoot, "Packages", packageName);

    public void CreateOldPackage(string packageName, string content)
    {
        FileFixtures.Write(TargetPackageDir(packageName), "bundles/a.bundle", content);
        FileFixtures.Write(TargetPackageDir(packageName), TestManifestReader.ManifestFileName, "old-manifest");
    }

    public void WriteOldIndex(string content)
    {
        Directory.CreateDirectory(BackendRoot);
        File.WriteAllText(PackageIndexPath, content, new System.Text.UTF8Encoding(false));
    }

    /// <summary>构造待上传包目录（模拟 BuildPublisher 组装后的暂存目录）。</summary>
    public string CreateStagedPackage(string content)
    {
        string stagedDir = _workspace.Path("staged");
        Directory.CreateDirectory(stagedDir);
        FileFixtures.Write(stagedDir, "bundles/a.bundle", content);
        FileFixtures.Write(stagedDir, TestManifestReader.ManifestFileName, "new-manifest");
        return stagedDir;
    }

    public PushReceipt Push(string stagedDir, string packageName, string indexJson, IWranglerCommandRunner runner)
    {
        var request = new PublishRequest
        {
            BackendKey = BackendKey,
            SourcePackageDir = stagedDir,
            TargetId = _config.TargetId,
            ManifestReader = new TestManifestReader(),
            Identity = new PackageBuildIdentity
            {
                PackageName = packageName,
                Version = VersionNumber.Parse("1.0.0"),
                BackendId = BackendKey,
                BuildType = "Hotfix"
            }
        };

        var target = new CloudflarePagesPushTarget(_config, runner);
        return target.Push(new PushPayload
        {
            Request = request,
            StagedPackageDir = stagedDir,
            PackageIndexJson = indexJson,
            PackagesFolderName = "Packages"
        });
    }

    /// <summary>走 BuildPublisher 的完整上传路径（非目录型目标）：服务器事实不可读，必须由发布器先组装目标包。</summary>
    public PushReceipt PublishThroughPublisher(
        string sourcePackageDir,
        string packageName,
        IWranglerCommandRunner runner,
        IFullPackageBaselineSource baselineSource)
    {
        var request = new PublishRequest
        {
            BackendKey = BackendKey,
            SourcePackageDir = sourcePackageDir,
            TargetId = _config.TargetId,
            ManifestReader = new TestManifestReader(),
            FullPackageBaselineSource = baselineSource,
            Identity = new PackageBuildIdentity
            {
                PackageName = packageName,
                Version = VersionNumber.Parse("1.0.1"),
                BackendId = BackendKey,
                BuildType = "Hotfix"
            }
        };

        return BuildPublisher.Push(request, new CloudflarePagesPushTarget(_config, runner));
    }
}

/// <summary>稀疏 Hotfix 场景的包目录事实。</summary>
internal sealed class SparsePackageFixture
{
    public SparsePackageFixture(string baseFullDir, string hotfixDir, string hotfixPackageName)
    {
        BaseFullDir = baseFullDir;
        HotfixDir = hotfixDir;
        HotfixPackageName = hotfixPackageName;
    }

    /// <summary>本地基准 Full 包目录（未变化内容的字节来源）</summary>
    public string BaseFullDir { get; }

    /// <summary>本地稀疏 Hotfix 包目录（只携带变化内容与完整目标清单）</summary>
    public string HotfixDir { get; }

    public string HotfixPackageName { get; }
}

/// <summary>测试用 Wrangler 调用替身：预检成功，部署按注入结果返回。</summary>
internal sealed class TestWranglerCommandRunner : IWranglerCommandRunner
{
    private readonly bool _deploySucceeds;
    private readonly string _deployMessage;

    private TestWranglerCommandRunner(bool deploySucceeds, string deployMessage)
    {
        _deploySucceeds = deploySucceeds;
        _deployMessage = deployMessage;
    }

    public static TestWranglerCommandRunner DeploySucceeds() => new TestWranglerCommandRunner(true, "deployed");

    public static TestWranglerCommandRunner DeployFails(string message) => new TestWranglerCommandRunner(false, message);

    /// <summary>是否执行过部署命令。</summary>
    public bool HasDeployCall { get; private set; }

    public string ResolveExecutable() => "wrangler";

    public WranglerCommandResult Run(string wranglerPath, string arguments, int timeoutMilliseconds)
    {
        if (arguments != null && arguments.Contains("pages deploy"))
        {
            HasDeployCall = true;
            return _deploySucceeds
                ? WranglerCommandResult.Ok(_deployMessage)
                : WranglerCommandResult.Fail(_deployMessage);
        }

        return WranglerCommandResult.Ok("wrangler 1.0.0");
    }
}

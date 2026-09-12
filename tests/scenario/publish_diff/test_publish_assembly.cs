using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 完整目标包组装门禁：服务器事实不可用时，稀疏 Hotfix 由本地基准 Full 补齐；来源不足不得产生副作用。
/// </summary>
internal static class PublishAssemblyTests
{
    public static void Run()
    {
        SparseHotfixOnCorruptServerIsAssembledFromBaseFull();
        SparseHotfixWithoutBaselineResolverFailsWithNoSideEffects();
        SparseHotfixWithUnresolvableBaselineFailsWithNoSideEffects();
        SparseHotfixWithBaselineDigestMismatchFailsWithNoSideEffects();
        SparseHotfixReusesAvailableServerPackageReadOnly();
        SparseHotfixWithContentMissingEverywhereFails();
        SelfContainedHotfixOnCorruptServerDoesNotNeedBaseline();
    }

    /// <summary>服务器包 Manifest 损坏时，稀疏 Hotfix 必须由本地基准 Full 补齐成完整目标包。</summary>
    private static void SparseHotfixOnCorruptServerIsAssembledFromBaseFull()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixOnCorruptServerIsAssembledFromBaseFull));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.WriteContent("b.bundle", "content-b");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");

        // 破坏服务器当前包清单：服务器事实不可信，发布必须退化为完整上传。
        File.WriteAllText(
            Path.Combine(context.PackageDir(baseFull.PackageName), TestManifestReader.ManifestFileName),
            "{ broken");
        Dictionary<string, byte[]> oldPackageBefore =
            PublishContext.SnapshotDirectory(context.PackageDir(baseFull.PackageName));

        // 稀疏 Hotfix：目标清单声明 a+b，但只随包携带变化的 b；a 的字节只能来自基准 Full。
        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b-changed");
        hotfix.DeclareContent("a.bundle", FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle"));
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix, TestFullPackageBaselineSource.Available(baseFull.SourceDir));

        Check.True(receipt.Success, $"损坏服务器上发布稀疏 Hotfix 应成功: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "服务器事实不可信必须标记为退化");
        Check.True(receipt.AssembledFromBaselineFull, "回执必须标记由基准 Full 补齐");
        Check.Equal(Path.GetFullPath(baseFull.SourceDir), Path.GetFullPath(receipt.BaselineFullPackageDir),
            "回执必须记录实际使用的基准 Full 包目录");
        Check.Equal(3, receipt.UploadedCount, "完整目标包（清单 + a + b）都要写入服务器");
        Check.Contains(string.Join("\n", receipt.Messages), "基准 Full 补齐=1", "过程说明必须给出补齐数量");

        string published = context.PackageDir(hotfix.PackageName);
        Check.Equal("content-a", File.ReadAllText(Path.Combine(published, "bundles", "a.bundle")),
            "未变化内容必须由基准 Full 补齐进目标包");
        Check.Equal("content-b-changed", File.ReadAllText(Path.Combine(published, "bundles", "b.bundle")),
            "变化内容必须来自本地 Hotfix 包");
        Check.Equal(File.ReadAllText(Path.Combine(hotfix.SourceDir, TestManifestReader.ManifestFileName)),
            File.ReadAllText(Path.Combine(published, TestManifestReader.ManifestFileName)),
            "完整目标清单必须随包发布");

        Dictionary<string, byte[]> oldPackageAfter =
            PublishContext.SnapshotDirectory(context.PackageDir(baseFull.PackageName));
        CheckSnapshotEquals(oldPackageBefore, oldPackageAfter, "服务器旧包目录必须保持只读不变");
        Check.Equal(hotfix.PackageName, context.ReadPackageIndex().LatestPackage, "PackageIndex 必须指向新包");
    }

    /// <summary>没有基准 Full 解析入口且本地包不完整时，必须失败而不是发布缺内容的包。</summary>
    private static void SparseHotfixWithoutBaselineResolverFailsWithNoSideEffects()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixWithoutBaselineResolverFailsWithNoSideEffects));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");

        File.WriteAllText(
            Path.Combine(context.PackageDir(baseFull.PackageName), TestManifestReader.ManifestFileName),
            "{ broken");
        byte[] indexBefore = File.ReadAllBytes(context.PackageIndexPath);
        Dictionary<string, byte[]> serverBefore = PublishContext.SnapshotDirectory(context.PackagesRoot);

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b");
        hotfix.DeclareContent("a.bundle", FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle"));
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix);

        Check.True(!receipt.Success, "缺少基准 Full 解析入口时稀疏 Hotfix 必须失败");
        Check.Contains(receipt.FailureReason, "来源不足", "失败原因必须指出来源不足");
        Check.Contains(receipt.FailureReason, "bundles/a.bundle", "失败原因必须指出缺失的内容");
        AssertNoSideEffects(context, indexBefore, serverBefore, hotfix.PackageName);
    }

    /// <summary>基准 Full 摘要不可读（解析失败）时同样必须失败。</summary>
    private static void SparseHotfixWithUnresolvableBaselineFailsWithNoSideEffects()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixWithUnresolvableBaselineFailsWithNoSideEffects));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");
        Directory.Delete(context.PackageDir(baseFull.PackageName), true);

        byte[] indexBefore = File.ReadAllBytes(context.PackageIndexPath);
        Dictionary<string, byte[]> serverBefore = PublishContext.SnapshotDirectory(context.PackagesRoot);

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b");
        hotfix.DeclareContent("a.bundle", FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle"));
        hotfix.Seal();

        var baseline = TestFullPackageBaselineSource.Unavailable("Summary Index 不可用");
        PushReceipt receipt = context.Publish(hotfix, baseline);

        Check.True(!receipt.Success, "基准 Full 摘要不可读时稀疏 Hotfix 必须失败");
        Check.Contains(receipt.FailureReason, "来源不足", "失败原因必须指出来源不足");
        Check.Contains(receipt.FailureReason, "Summary Index 不可用", "失败原因必须带出基准解析原因");
        Check.True(baseline.CallCount >= 1, "确实尝试过解析基准 Full");
        AssertNoSideEffects(context, indexBefore, serverBefore, hotfix.PackageName);
    }

    /// <summary>基准 Full 内同路径文件与清单声明摘要不一致时，不得把它当作内容来源。</summary>
    private static void SparseHotfixWithBaselineDigestMismatchFailsWithNoSideEffects()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixWithBaselineDigestMismatchFailsWithNoSideEffects));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");
        // 目标清单声明的摘要取自真正的 Full 制品，随后才构造被篡改的基准目录。
        FileHelper.FileDigest declaredA = FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle");

        // 服务器当前包清单同样损坏：这样未变化内容只能从基准 Full 取。
        File.WriteAllText(
            Path.Combine(context.PackageDir(baseFull.PackageName), TestManifestReader.ManifestFileName),
            "{ broken");
        byte[] indexBefore = File.ReadAllBytes(context.PackageIndexPath);
        Dictionary<string, byte[]> serverBefore = PublishContext.SnapshotDirectory(context.PackagesRoot);

        // 基准目录内的同路径文件已被改写：Hash/CRC/Size 与目标清单声明不符。
        TestPackage tamperedBaseline = context.CreatePackage("BaselineTampered", "1.0.0");
        tamperedBaseline.WriteContent("a.bundle", "tampered-content");

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b");
        hotfix.DeclareContent("a.bundle", declaredA);
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix, TestFullPackageBaselineSource.Available(tamperedBaseline.SourceDir));

        Check.True(!receipt.Success, "基准 Full 内容与清单声明不一致时必须失败");
        Check.Contains(receipt.FailureReason, "来源不足", "失败原因必须指出来源不足");
        Check.Contains(receipt.FailureReason, "不一致", "失败原因必须指出摘要不一致");
        AssertNoSideEffects(context, indexBefore, serverBefore, hotfix.PackageName);
    }

    /// <summary>服务器事实可用时只读复用服务器当前包内容，不得改动服务器当前包目录内的文件。</summary>
    private static void SparseHotfixReusesAvailableServerPackageReadOnly()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixReusesAvailableServerPackageReadOnly));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.WriteContent("b.bundle", "content-b");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");
        Dictionary<string, byte[]> serverPackageBefore =
            PublishContext.SnapshotDirectory(context.PackageDir(baseFull.PackageName));

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b-changed");
        hotfix.DeclareContent("a.bundle", FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle"));
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix);

        Check.True(receipt.Success, $"服务器可用时稀疏 Hotfix 应成功: {receipt.FailureReason}");
        Check.True(!receipt.DegradedToFullUpload, "服务器事实可用时不得标记为退化");
        Check.True(!receipt.AssembledFromBaselineFull, "服务器已提供未变化内容时不需要基准 Full");
        Check.Equal(2, receipt.UploadedCount, "只有变化的 b 与完整目标清单需要写入");
        Check.True(receipt.ReusedCount >= 1, "未变化的 a 应复用服务器当前包内容");

        string published = context.PackageDir(hotfix.PackageName);
        Check.Equal("content-a", File.ReadAllText(Path.Combine(published, "bundles", "a.bundle")),
            "未变化内容必须来自服务器当前包");
        CheckSnapshotEquals(serverPackageBefore,
            PublishContext.SnapshotDirectory(context.PackageDir(baseFull.PackageName)),
            "服务器当前包目录必须保持只读不变");
    }

    /// <summary>目标清单声明的内容在本地、服务器当前包与基准 Full 三处都缺失时必须失败。</summary>
    private static void SparseHotfixWithContentMissingEverywhereFails()
    {
        using var workspace = new TempWorkspace(nameof(SparseHotfixWithContentMissingEverywhereFails));
        var context = new PublishContext(workspace);

        TestPackage baseFull = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        baseFull.WriteContent("a.bundle", "content-a");
        baseFull.Seal();
        Check.True(context.Publish(baseFull).Success, "准备阶段：基准 Full 发布应成功");
        byte[] indexBefore = File.ReadAllBytes(context.PackageIndexPath);
        Dictionary<string, byte[]> serverBefore = PublishContext.SnapshotDirectory(context.PackagesRoot);

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("b.bundle", "content-b");
        hotfix.DeclareContent("a.bundle", FileFixtures.Digest(baseFull.SourceDir, "bundles/a.bundle"));
        hotfix.DeclareContent("c.bundle", FileFixtures.Fake("bundles/c.bundle", "no-such-hash", 7, 42));
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix, TestFullPackageBaselineSource.Available(baseFull.SourceDir));

        Check.True(!receipt.Success, "内容三处都缺失时必须失败");
        Check.Contains(receipt.FailureReason, "来源不足", "失败原因必须指出来源不足");
        Check.Contains(receipt.FailureReason, "bundles/c.bundle", "失败原因必须指出缺失的内容");
        AssertNoSideEffects(context, indexBefore, serverBefore, hotfix.PackageName);
    }

    /// <summary>自足 Hotfix（内容全部随包携带）不依赖基准 Full，即使服务器事实不可用也应成功。</summary>
    private static void SelfContainedHotfixOnCorruptServerDoesNotNeedBaseline()
    {
        using var workspace = new TempWorkspace(nameof(SelfContainedHotfixOnCorruptServerDoesNotNeedBaseline));
        var context = new PublishContext(workspace);

        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");

        File.WriteAllText(
            Path.Combine(context.PackageDir(first.PackageName), TestManifestReader.ManifestFileName),
            "{ broken");

        TestPackage hotfix = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        hotfix.WriteContent("a.bundle", "content-a");
        hotfix.WriteContent("b.bundle", "content-b");
        hotfix.Seal();

        PushReceipt receipt = context.Publish(hotfix);

        Check.True(receipt.Success, $"自足 Hotfix 在损坏服务器上应成功: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "服务器事实不可信必须标记为退化");
        Check.True(!receipt.AssembledFromBaselineFull, "自足 Hotfix 不得声明使用了基准 Full");
        Check.Equal(3, receipt.UploadedCount, "自足 Hotfix 的清单与全部内容都要写入服务器");
    }

    /// <summary>失败发布必须零副作用：索引字节不变、服务器包集合不变、不留下新包目录或隔离工作区。</summary>
    private static void AssertNoSideEffects(
        PublishContext context,
        byte[] indexBefore,
        Dictionary<string, byte[]> serverBefore,
        string newPackageName)
    {
        Check.True(File.ReadAllBytes(context.PackageIndexPath).AsSpan().SequenceEqual(indexBefore),
            "来源不足时不得改动 PackageIndex");
        Check.True(!Directory.Exists(context.PackageDir(newPackageName)), "来源不足时不得留下新包目录");
        CheckSnapshotEquals(serverBefore, PublishContext.SnapshotDirectory(context.PackagesRoot),
            "来源不足时服务器包集合必须保持原样");
        Check.True(!Directory.Exists(Path.Combine(context.BackendRoot, PackageFileNames.PushWorkFolderName)),
            "来源不足时不得在服务器根留下隔离工作区");
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

/// <summary>
/// 测试用基准 Full 解析替身：要么指向一个本地目录，要么固定失败并给出原因。
/// </summary>
internal sealed class TestFullPackageBaselineSource : IFullPackageBaselineSource
{
    private readonly string _packageDir;
    private readonly string _failureReason;

    private TestFullPackageBaselineSource(string packageDir, string failureReason)
    {
        _packageDir = packageDir;
        _failureReason = failureReason;
    }

    /// <summary>解析成功：基准 Full 包目录由调用方给出。</summary>
    public static TestFullPackageBaselineSource Available(string packageDir) =>
        new TestFullPackageBaselineSource(packageDir, null);

    /// <summary>解析失败：模拟 Summary 不可读或基准制品缺失。</summary>
    public static TestFullPackageBaselineSource Unavailable(string failureReason) =>
        new TestFullPackageBaselineSource(null, failureReason);

    /// <summary>被调用次数，用于确认确实尝试过解析基准。</summary>
    public int CallCount { get; private set; }

    public bool TryResolveBaselinePackageDir(PublishRequest request, out string packageDir, out string error)
    {
        CallCount++;
        packageDir = _packageDir ?? string.Empty;
        error = _failureReason ?? string.Empty;
        return _packageDir != null;
    }
}

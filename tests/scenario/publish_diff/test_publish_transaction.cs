using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 发布事务契约：服务器事实读取、Hash 复用、退化完整上传、校验未过不写索引、旧索引与旧包不被破坏。
/// </summary>
internal static class PublishTransactionTests
{

    public static void Run()
    {
        FirstPublishWithoutServerIndexDegradesToFullUpload();
        CorruptServerIndexDegradesToFullUpload();
        CorruptServerManifestDegradesAndKeepsOldPackage();
        HashHitReusesServerContent();
        StagedVerificationFailureKeepsOldPackageIndex();
        CurrentPackageIsImmutable();
        RepublishingIdenticalPackageIsIdempotent();
        InterruptedUploadKeepsOldIndexAndPackage();
        PublishCacheIsAuxiliary();
    }

    private static void FirstPublishWithoutServerIndexDegradesToFullUpload()
    {
        using var workspace = new TempWorkspace(nameof(FirstPublishWithoutServerIndexDegradesToFullUpload));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        package.WriteContent("a.bundle", "content-a");
        package.WriteContent("b.bundle", "content-b");
        package.Seal();

        PushReceipt receipt = context.Publish(package);

        Check.True(receipt.Success, $"首次发布应成功: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "服务器没有 PackageIndex 时必须标记为完整上传退化");
        // 本地文件集合包含清单本身与两个内容文件，全部由本地提供。
        Check.Equal(3, receipt.UploadedCount, "清单与全部内容文件都应由本地提供");
        Check.Equal(0, receipt.ReusedCount, "没有服务器事实时不得声明复用");
        Check.True(File.Exists(context.PackageIndexPath), "发布结束后必须存在 PackageIndex");
        Check.Equal("Build_20260909120000_1.0.0", context.ReadPackageIndex().LatestPackage, "PackageIndex 应指向本次包");

        string publishedDir = context.PackageDir("Build_20260909120000_1.0.0");
        Check.True(File.Exists(Path.Combine(publishedDir, "TestManifest.json")), "新包目录必须包含清单");
        Check.Equal("content-a", File.ReadAllText(Path.Combine(publishedDir, "bundles", "a.bundle")), "内容文件必须完整发布");
    }

    private static void CorruptServerIndexDegradesToFullUpload()
    {
        using var workspace = new TempWorkspace(nameof(CorruptServerIndexDegradesToFullUpload));
        var context = new PublishContext(workspace);
        Directory.CreateDirectory(context.BackendRoot);
        File.WriteAllText(context.PackageIndexPath, "{ this is not json");

        TestPackage package = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PushReceipt receipt = context.Publish(package);

        Check.True(receipt.Success, $"索引损坏时应退化为完整上传而不是失败: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "索引损坏必须标记为退化");
        Check.Equal("Build_20260909120000_1.0.0", context.ReadPackageIndex().LatestPackage, "退化发布后必须写入新的有效索引");
    }

    private static void CorruptServerManifestDegradesAndKeepsOldPackage()
    {
        using var workspace = new TempWorkspace(nameof(CorruptServerManifestDegradesAndKeepsOldPackage));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");

        // 破坏服务器上第一个包的清单，模拟服务器 Manifest 损坏。
        File.WriteAllText(Path.Combine(context.PackageDir("Build_20260909120000_1.0.0"), TestManifestReader.ManifestFileName), "{ broken");

        TestPackage second = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        second.WriteContent("a.bundle", "content-a");
        second.WriteContent("b.bundle", "content-b");
        second.Seal();

        PushReceipt receipt = context.Publish(second);

        Check.True(receipt.Success, $"服务器 Manifest 损坏时应退化成功: {receipt.FailureReason}");
        Check.True(receipt.DegradedToFullUpload, "服务器 Manifest 损坏必须标记为退化");
        Check.True(Directory.Exists(context.PackageDir("Build_20260909120000_1.0.0")), "发布不得删除旧包");
        Check.Equal("Build_20260909120001_1.0.1", context.ReadPackageIndex().LatestPackage, "索引应指向新包");
    }

    private static void HashHitReusesServerContent()
    {
        using var workspace = new TempWorkspace(nameof(HashHitReusesServerContent));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.WriteContent("c.bundle", "shared-content");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");

        // 第二次构建：a 未变，b 变化，d 是新文件但内容与服务器上的 c 完全相同。
        TestPackage second = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        second.WriteContent("a.bundle", "content-a");
        second.WriteContent("b.bundle", "content-b-changed");
        second.WriteContent("d.bundle", "shared-content");
        second.Seal();

        PushReceipt receipt = context.Publish(second);

        Check.True(receipt.Success, $"第二次发布应成功: {receipt.FailureReason}");
        bool hashReuse = false;
        for (int i = 0; i < receipt.Messages.Count; i++)
        {
            if (receipt.Messages[i].Contains("Hash 命中复用") && receipt.Messages[i].Contains("bundles/d"))
                hashReuse = true;
        }

        Check.True(hashReuse, "内容相同但名称不同的文件应报告 Hash 命中复用");
        // 需要写入服务器的：本次变化的 b 与随包发布的清单；d 通过 Hash 命中复用服务器内容。
        Check.Equal(2, receipt.UploadedCount, "只有本次变化的 b 与清单需要从本地写入服务器");
        Check.True(receipt.ReusedCount >= 2, "未变的 a 与 Hash 命中的 d 都应计入复用");

        string publishedDir = context.PackageDir("Build_20260909120001_1.0.1");
        Check.Equal("shared-content", File.ReadAllText(Path.Combine(publishedDir, "bundles", "d.bundle")),
            "复用后的内容必须与本地包一致");
        Check.True(Directory.Exists(context.PackageDir("Build_20260909120000_1.0.0")), "发布不得删除旧包");
    }

    private static void StagedVerificationFailureKeepsOldPackageIndex()
    {
        using var workspace = new TempWorkspace(nameof(StagedVerificationFailureKeepsOldPackageIndex));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");
        string indexBefore = File.ReadAllText(context.PackageIndexPath);

        // 本次清单声明的摘要与实际内容不一致：新目录逻辑校验必须失败。
        TestPackage second = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        second.WriteContent("a.bundle", "content-a");
        second.WriteContent("b.bundle", "content-b");
        second.Seal(manifestTamper: digest =>
            new FileHelper.FileDigest(digest.Name, "tampered-hash", digest.CRC, digest.Size));

        PushReceipt receipt = context.Publish(second);

        Check.True(!receipt.Success, "清单与实际内容不一致时发布必须失败");
        Check.Equal(indexBefore, File.ReadAllText(context.PackageIndexPath), "校验未过时不得改动 PackageIndex");
        Check.True(!Directory.Exists(context.PackageDir("Build_20260909120001_1.0.1")), "校验未过时不得留下新包目录");
        Check.True(Directory.Exists(context.PackageDir("Build_20260909120000_1.0.0")), "失败发布不得影响旧包");
    }

    private static void CurrentPackageIsImmutable()
    {
        using var workspace = new TempWorkspace(nameof(CurrentPackageIsImmutable));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");
        string indexBefore = File.ReadAllText(context.PackageIndexPath);
        string packageContentBefore = File.ReadAllText(Path.Combine(context.PackageDir("Build_20260909120000_1.0.0"), "bundles", "a.bundle"));

        // 同名包但内容不同：当前 PackageIndex 指向的包目录不可被覆盖。
        TestPackage replacement = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        replacement.WriteContent("a.bundle", "content-a-changed");
        replacement.Seal();

        PushReceipt receipt = context.Publish(replacement);

        Check.True(!receipt.Success, "覆盖当前已发布包必须被拒绝");
        Check.Equal(indexBefore, File.ReadAllText(context.PackageIndexPath), "拒绝覆盖时索引不得变化");
        Check.Equal(packageContentBefore,
            File.ReadAllText(Path.Combine(context.PackageDir("Build_20260909120000_1.0.0"), "bundles", "a.bundle")),
            "已发布包内容不得被改写");
    }

    private static void RepublishingIdenticalPackageIsIdempotent()
    {
        using var workspace = new TempWorkspace(nameof(RepublishingIdenticalPackageIsIdempotent));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        package.WriteContent("a.bundle", "content-a");
        package.Seal();
        Check.True(context.Publish(package).Success, "准备阶段：第一次发布应成功");

        PushReceipt again = context.Publish(package);

        Check.True(again.Success, $"内容一致的重发应幂等成功: {again.FailureReason}");
        Check.Equal("Build_20260909120000_1.0.0", context.ReadPackageIndex().LatestPackage, "幂等重发后索引仍指向同一包");
    }

    private static void InterruptedUploadKeepsOldIndexAndPackage()
    {
        using var workspace = new TempWorkspace(nameof(InterruptedUploadKeepsOldIndexAndPackage));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");
        byte[] indexBefore = File.ReadAllBytes(context.PackageIndexPath);

        // 直接驱动事务并在组装阶段中断：模拟上传中断。
        TestPackage second = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        second.WriteContent("a.bundle", "content-a");
        second.WriteContent("b.bundle", "content-b");
        second.Seal();

        var request = context.CreateRequest(second);
        Check.True(PackagePublishTransaction.TryCreate(request, context.BackendRoot, out PackagePublishTransaction transaction, out string error),
            $"事务应可建立: {error}");

        using (transaction)
        {
            transaction.CreatePlan();
            transaction.Stage();
            transaction.VerifyStaged();
            // 不执行 Apply / WritePackageIndex，直接退出 using：等价于上传中断。
        }

        Check.True(File.ReadAllBytes(context.PackageIndexPath).AsSpan().SequenceEqual(indexBefore),
            "中断上传不得破坏旧 PackageIndex");
        Check.True(Directory.Exists(context.PackageDir("Build_20260909120000_1.0.0")), "中断上传不得影响旧包");
        Check.True(!Directory.Exists(context.PackageDir("Build_20260909120001_1.0.1")), "中断上传不得留下半成品包目录");
    }

    private static void PublishCacheIsAuxiliary()
    {
        using var workspace = new TempWorkspace(nameof(PublishCacheIsAuxiliary));
        var context = new PublishContext(workspace);
        TestPackage first = context.CreatePackage("Build_20260909120000_1.0.0", "1.0.0");
        first.WriteContent("a.bundle", "content-a");
        first.Seal();
        Check.True(context.Publish(first).Success, "准备阶段：第一次发布应成功");
        Check.True(File.Exists(context.PublishCachePath), "发布成功应写入本地发布缓存");

        PublishCache cache = PublishCacheStore.Load(context.PublishCachePath);
        Check.True(cache != null, "发布缓存应可读取");
        Check.Equal("Build_20260909120000_1.0.0", cache.LastPackageName, "发布缓存应记录上次发布的包名");
        Check.True(cache.Files.Count >= 2, "发布缓存应记录发布文件集合");

        // 缓存损坏不得影响发布：删除为非法 JSON，下一次发布仍必须成功。
        File.WriteAllText(context.PublishCachePath, "{ corrupt");
        Check.True(PublishCacheStore.Load(context.PublishCachePath) == null, "损坏的发布缓存必须按“无缓存”处理");

        TestPackage second = context.CreatePackage("Build_20260909120001_1.0.1", "1.0.1");
        second.WriteContent("a.bundle", "content-a");
        second.Seal();
        PushReceipt receipt = context.Publish(second);
        Check.True(receipt.Success, $"缓存损坏不得影响发布: {receipt.FailureReason}");
        Check.True(PublishCacheStore.Load(context.PublishCachePath) != null, "成功发布后应写回新的发布缓存");
    }
}

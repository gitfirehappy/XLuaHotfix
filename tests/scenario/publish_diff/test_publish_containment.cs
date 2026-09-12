using System;
using System.IO;

/// <summary>
/// 发布输入边界契约（计划 T6）：包名与包集合名必须是受控单段目录名，发布时间戳与版本严格可解析，
/// 任何组装、就位与索引路径都不得越出声明的服务器根。
/// </summary>
internal static class PublishContainmentTests
{
    private const string PackageName = "Build_20260911120000_1.0.0";
    private const string Version = "1.0.0";

    public static void Run()
    {
        // 每个子用例独立报告：发布边界需要一次性看清全部失败项，而不是只看到第一个。
        var failures = new System.Collections.Generic.List<string>();
        void RunCase(string name, Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        RunCase(nameof(DotDotPackagesFolderNameIsRejected), DotDotPackagesFolderNameIsRejected);
        RunCase(nameof(DotDotPackageNameIsRejected), DotDotPackageNameIsRejected);
        RunCase(nameof(ForeignPackageNameIsRejected), ForeignPackageNameIsRejected);
        RunCase(nameof(InvalidTimestampIsRejected), InvalidTimestampIsRejected);
        RunCase(nameof(MultiSegmentPackagesFolderNameIsRejected), MultiSegmentPackagesFolderNameIsRejected);
        RunCase(nameof(ReparsePointServerRootIsRejected), ReparsePointServerRootIsRejected);

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"{failures.Count}/6 个发布边界子用例未满足 -> {string.Join(" | ", failures)}");
    }

    /// <summary>服务器根或其父链包含符号链接/重解析点时拒绝发布；环境无法创建链接时跳过。</summary>
    private static void ReparsePointServerRootIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(ReparsePointServerRootIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        string linkPath = Path.Combine(workspace.Root, "server_link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, context.ServerRoot);
        }
        catch (Exception)
        {
            // 当前环境没有创建符号链接的权限：跳过，不把环境限制当作失败。
            return;
        }

        var linkedTarget = new TestDirectoryTarget(linkPath);
        PushReceipt receipt = BuildPublisher.Push(context.CreateRequest(package), linkedTarget);
        Check.True(!receipt.Success, "服务器根包含符号链接时必须拒绝发布");
    }

    /// <summary>在服务器后端根写入哨兵：拒绝发布不得移动、删除或改写既有服务器内容。</summary>
    private static void PrepareSentinel(PublishContext context)
    {
        Directory.CreateDirectory(context.BackendRoot);
        File.WriteAllText(Path.Combine(context.BackendRoot, "sentinel.txt"), "sentinel");
    }

    /// <summary>断言发布被拒绝且服务器根未有副作用。</summary>
    private static void AssertRejectedWithoutSideEffects(PublishContext context, PushReceipt receipt)
    {
        Check.True(!receipt.Success, "非法包身份或包集合名必须被拒绝，而不是执行发布");
        string sentinel = Path.Combine(context.BackendRoot, "sentinel.txt");
        Check.True(File.Exists(sentinel), "拒绝发布不得移动或删除服务器根内容");
        Check.Equal("sentinel", File.ReadAllText(sentinel), "拒绝发布不得改写服务器根内容");
    }

    /// <summary>包集合名是单个目录段；".." 会把 TARGET 解析到服务器根之外。</summary>
    private static void DotDotPackagesFolderNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(DotDotPackagesFolderNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.PackagesFolderName = "..";
        PushReceipt receipt = BuildPublisher.Push(request, context.Target);

        AssertRejectedWithoutSideEffects(context, receipt);
        Check.True(
            !Directory.Exists(Path.Combine(workspace.Root, PackageName)),
            "目标包不得写入服务器根之外的目录");
    }

    /// <summary>包名 ".." 会把目标包解析为服务器根本身。</summary>
    private static void DotDotPackageNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(DotDotPackageNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity
        {
            PackageName = "..",
            Version = VersionNumber.Parse(Version),
            BackendId = "AB"
        };
        PushReceipt receipt = BuildPublisher.Push(request, context.Target);

        AssertRejectedWithoutSideEffects(context, receipt);
    }

    /// <summary>生产包名只接受 Build_{yyyyMMddHHmmss}_{version}。</summary>
    private static void ForeignPackageNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(ForeignPackageNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity
        {
            PackageName = "SomePackage",
            Version = VersionNumber.Parse(Version),
            BackendId = "AB"
        };
        PushReceipt receipt = BuildPublisher.Push(request, context.Target);

        AssertRejectedWithoutSideEffects(context, receipt);
    }

    /// <summary>时间戳必须是可严格解析的 14 位时间格式。</summary>
    private static void InvalidTimestampIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(InvalidTimestampIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity
        {
            PackageName = "Build_2026_1.0.0",
            Version = VersionNumber.Parse(Version),
            BackendId = "AB"
        };
        PushReceipt receipt = BuildPublisher.Push(request, context.Target);

        AssertRejectedWithoutSideEffects(context, receipt);
    }

    /// <summary>包集合名必须是单个安全目录段，不得包含分隔符或设备名。</summary>
    private static void MultiSegmentPackagesFolderNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(MultiSegmentPackagesFolderNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.PackagesFolderName = "nested/dir";
        PushReceipt receipt = BuildPublisher.Push(request, context.Target);

        AssertRejectedWithoutSideEffects(context, receipt);
    }
}

using System;
using System.IO;

/// <summary>
/// 发布输入边界契约：包名和包集合名必须是受控单段目录名，时间戳、版本和所有路径都必须严格受约束。
/// </summary>
internal static class PublishContainmentTests
{
    private const string PackageName = "Build_20260911120000_1.0.0";
    private const string Version = "1.0.0";

    public static void Run()
    {
        var failures = new System.Collections.Generic.List<string>();
        void RunCase(string name, Action run)
        {
            try { run(); }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }

        RunCase(nameof(DotDotPackagesFolderNameIsRejected), DotDotPackagesFolderNameIsRejected);
        RunCase(nameof(DotDotPackageNameIsRejected), DotDotPackageNameIsRejected);
        RunCase(nameof(ForeignPackageNameIsRejected), ForeignPackageNameIsRejected);
        RunCase(nameof(InvalidTimestampIsRejected), InvalidTimestampIsRejected);
        RunCase(nameof(MultiSegmentPackagesFolderNameIsRejected), MultiSegmentPackagesFolderNameIsRejected);
        RunCase(nameof(ReparsePointServerRootIsRejected), ReparsePointServerRootIsRejected);

        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count}/6 个发布边界子用例未满足 -> {string.Join(" | ", failures)}");
    }

    private static void ReparsePointServerRootIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(ReparsePointServerRootIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();

        string linkPath = Path.Combine(workspace.Root, "server_link");
        try { Directory.CreateSymbolicLink(linkPath, context.ServerRoot); }
        catch (Exception) { return; }

        var linkedTarget = new TestDirectoryTarget(linkPath);
        PublishResult result = PackagePublisher.Publish(context.CreateRequest(package), linkedTarget);
        Check.True(!result.Success, "服务器根包含符号链接时必须拒绝发布");
    }

    private static void PrepareSentinel(PublishContext context)
    {
        Directory.CreateDirectory(context.BackendRoot);
        File.WriteAllText(Path.Combine(context.BackendRoot, "sentinel.txt"), "sentinel");
    }

    private static void AssertRejectedWithoutSideEffects(PublishContext context, PublishResult result)
    {
        Check.True(!result.Success, "非法包身份或包集合名必须被拒绝，而不是执行发布");
        string sentinel = Path.Combine(context.BackendRoot, "sentinel.txt");
        Check.True(File.Exists(sentinel), "拒绝发布不得移动或删除服务器根内容");
        Check.Equal("sentinel", File.ReadAllText(sentinel), "拒绝发布不得改写服务器根内容");
    }

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
        PublishResult result = PackagePublisher.Publish(request, context.Target);
        AssertRejectedWithoutSideEffects(context, result);
        Check.True(!Directory.Exists(Path.Combine(workspace.Root, PackageName)), "目标包不得写入服务器根之外的目录");
    }

    private static void DotDotPackageNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(DotDotPackageNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();
        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity { PackageName = "..", Version = VersionNumber.Parse(Version), BackendId = "AB" };
        PublishResult result = PackagePublisher.Publish(request, context.Target);
        AssertRejectedWithoutSideEffects(context, result);
    }

    private static void ForeignPackageNameIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(ForeignPackageNameIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();
        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity { PackageName = "SomePackage", Version = VersionNumber.Parse(Version), BackendId = "AB" };
        PublishResult result = PackagePublisher.Publish(request, context.Target);
        AssertRejectedWithoutSideEffects(context, result);
    }

    private static void InvalidTimestampIsRejected()
    {
        using var workspace = new TempWorkspace(nameof(InvalidTimestampIsRejected));
        var context = new PublishContext(workspace);
        TestPackage package = context.CreatePackage(PackageName, Version);
        package.WriteContent("a.bundle", "content-a");
        package.Seal();
        PrepareSentinel(context);
        PublishRequest request = context.CreateRequest(package);
        request.Identity = new PackageBuildIdentity { PackageName = "Build_2026_1.0.0", Version = VersionNumber.Parse(Version), BackendId = "AB" };
        PublishResult result = PackagePublisher.Publish(request, context.Target);
        AssertRejectedWithoutSideEffects(context, result);
    }

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
        PublishResult result = PackagePublisher.Publish(request, context.Target);
        AssertRejectedWithoutSideEffects(context, result);
    }
}

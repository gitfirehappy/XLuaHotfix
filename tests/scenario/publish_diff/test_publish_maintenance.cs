using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 旧包清理维护契约：只删除不被当前 PackageIndex 指向的包，索引不可读时拒绝清理。
/// </summary>
internal static class PublishMaintenanceTests
{
    private const string Backend = "AB";
    private const string PackagesFolderName = "Packages";

    public static void Run()
    {
        DeletesOnlyUnreferencedPackages();
        RefusesWhenPackageIndexUnreadable();
        IdentityComesFromCallerSummary();
    }

    private static void DeletesOnlyUnreferencedPackages()
    {
        using var workspace = new TempWorkspace(nameof(DeletesOnlyUnreferencedPackages));
        string backendRoot = workspace.CreateDir(Backend);
        string packagesRoot = workspace.CreateDir($"{Backend}/{PackagesFolderName}");
        Directory.CreateDirectory(Path.Combine(packagesRoot, "Build_old"));
        Directory.CreateDirectory(Path.Combine(packagesRoot, "Build_current"));
        File.WriteAllText(Path.Combine(packagesRoot, "Build_old", "a.bundle"), "old");

        WritePackageIndex(backendRoot, "Build_current");

        PublishMaintenance.CleanupResult result =
            PublishMaintenance.DeleteUnreferencedPackages(backendRoot, PackagesFolderName);

        Check.True(result.Success, $"清理应成功: {result.FailureReason}");
        Check.Equal("Build_current", result.KeptPackage, "必须保留当前 PackageIndex 指向的包");
        Check.Equal(1, result.DeletedPackages.Count, "应删除一个旧包目录");
        Check.Equal("Build_old", result.DeletedPackages[0], "被删除的应是未被指向的旧包");
        Check.True(!Directory.Exists(Path.Combine(packagesRoot, "Build_old")), "旧包目录应已删除");
        Check.True(Directory.Exists(Path.Combine(packagesRoot, "Build_current")), "当前包目录必须保留");
    }

    private static void RefusesWhenPackageIndexUnreadable()
    {
        using var workspace = new TempWorkspace(nameof(RefusesWhenPackageIndexUnreadable));
        string backendRoot = workspace.CreateDir(Backend);
        string packagesRoot = workspace.CreateDir($"{Backend}/{PackagesFolderName}");
        Directory.CreateDirectory(Path.Combine(packagesRoot, "Build_1"));
        Directory.CreateDirectory(Path.Combine(packagesRoot, "Build_2"));

        PublishMaintenance.CleanupResult missing =
            PublishMaintenance.DeleteUnreferencedPackages(backendRoot, PackagesFolderName);
        Check.True(!missing.Success, "没有 PackageIndex 时必须拒绝清理");
        Check.True(Directory.Exists(Path.Combine(packagesRoot, "Build_1")), "拒绝清理时不得删除任何包");

        File.WriteAllText(Path.Combine(backendRoot, "PackageIndex.json"), "{ corrupt");
        PublishMaintenance.CleanupResult corrupt =
            PublishMaintenance.DeleteUnreferencedPackages(backendRoot, PackagesFolderName);
        Check.True(!corrupt.Success, "PackageIndex 损坏时必须拒绝清理");
        Check.True(!PublishMaintenance.TryReadCurrentPackage(
                Path.Combine(backendRoot, "PackageIndex.json"), out _, out string error),
            "损坏的 PackageIndex 不得给出当前包名");
        Check.Contains(error, "PackageIndex.json", "失败原因应指出索引路径");
    }

    /// <summary>包身份契约：由调用方从正式 Summary 注入；合法性由安全名与后端匹配校验承担。</summary>
    private static void IdentityComesFromCallerSummary()
    {
        var identity = new PackageBuildIdentity
        {
            PackageName = "Build_20260909120000_1.2.3",
            Version = VersionNumber.Parse("1.2.3"),
            BackendId = "AB",
            BuildType = "Hotfix"
        };
        Check.True(identity.IsSafePackageName(), "合法 Build_* 包名必须通过安全校验");
        Check.True(identity.MatchesBackend("AB"), "后端标识应匹配请求后端");
        Check.True(!identity.MatchesBackend("AA"), "后端标识不匹配时必须报告不一致");

        Check.True(!new PackageBuildIdentity { PackageName = ".." }.IsSafePackageName(), "点段包名必须被拒绝");
        Check.True(!new PackageBuildIdentity { PackageName = "CON" }.IsSafePackageName(), "Windows 设备名必须被拒绝");
        Check.True(!new PackageBuildIdentity { PackageName = "a/b" }.IsSafePackageName(), "含分隔符的包名必须被拒绝");
    }

    private static void WritePackageIndex(string backendRoot, string packageName)
    {
        var index = new PackageIndex
        {
            LatestPackage = packageName,
            LatestVersion = VersionNumber.Parse("1.0.0"),
            BackendMode = Backend
        };
        File.WriteAllText(Path.Combine(backendRoot, "PackageIndex.json"), SerializationUtility.SerializeToJson(index, true));
    }
}

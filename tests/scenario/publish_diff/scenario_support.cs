using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 发布与累计 Hotfix 场景的断言入口与应用外壳。
/// 场景只使用本地临时目录模拟服务器，禁止访问网络。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var suites = new (string Name, Action Run)[]
        {
            ("FileDiffClassification", FileDiffTests.Run),
            ("PublishTransactionDecisions", PublishTransactionTests.Run),
            ("PublishAssemblyDecisions", PublishAssemblyTests.Run),
            ("CloudflarePublishDecisions", CloudflareRollbackTests.Run),
            ("PublishContainmentRules", PublishContainmentTests.Run),
            ("PublishIdentitySources", PublishIdentitySourceTests.Run),
            ("PublishMaintenanceRules", PublishMaintenanceTests.Run)
        };

        int failures = 0;
        for (int i = 0; i < suites.Length; i++)
        {
            try
            {
                suites[i].Run();
                Console.WriteLine($"PASS {suites[i].Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {suites[i].Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"publish diff scenarios: {suites.Length - failures}/{suites.Length} passed");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>场景断言。失败即抛出并终止当前场景。</summary>
internal static class Check
{
    public static void True(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}（期望={expected}, 实际={actual}）");
    }

    public static void Contains(string source, string value, string message)
    {
        True(source != null && source.IndexOf(value, StringComparison.Ordinal) >= 0, $"{message}（未找到 '{value}'）");
    }
}

/// <summary>临时工作目录：每个场景一个独立根，场景结束后删除。</summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace(string name)
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fyasset_publish_diff",
            name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>把 '/'-分隔的相对路径解析成工作区内的绝对路径。</summary>
    public string Path(string relative) =>
        System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string CreateDir(string relative)
    {
        string path = Path(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relative, string content)
    {
        string path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (Exception)
        {
            // 临时目录清理失败不影响场景结论。
        }
    }
}

/// <summary>测试用文件集合：提供写文件、计算摘要与构造 FileDigest 的便捷方法。</summary>
internal static class FileFixtures
{
    /// <summary>写入文件并返回它的摘要（名称为包根相对路径）。</summary>
    public static FileDigest Write(string rootDir, string relativeName, string content)
    {
        string path = System.IO.Path.Combine(rootDir, relativeName.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return Digest(rootDir, relativeName);
    }

    /// <summary>计算已存在文件的摘要。</summary>
    public static FileDigest Digest(string rootDir, string relativeName)
    {
        string path = System.IO.Path.Combine(rootDir, relativeName.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!FileDigest.TryCreate(path, relativeName, out FileDigest digest))
            throw new InvalidOperationException($"无法计算摘要: {path}");
        return digest;
    }

    /// <summary>构造一个只有名称与 Hash 的摘要，用于纯分类断言。</summary>
    public static FileDigest Fake(string name, string hash, uint crc = 1, long size = 10) =>
        new FileDigest(name, hash, crc, size);

    /// <summary>读取文件文本。</summary>
    public static string ReadText(string rootDir, string relativeName) =>
        File.ReadAllText(System.IO.Path.Combine(rootDir, relativeName.Replace('/', System.IO.Path.DirectorySeparatorChar)));
}

/// <summary>场景内的一次发布会话：本地包目录、服务器目录与目标替身。</summary>
/// <remarks>发布事务与发布输入边界场景共用；包身份由调用方显式给出，等价于构建摘要给出的身份。</remarks>
internal sealed class PublishContext
{
    private const string Backend = "AB";

    private readonly TempWorkspace _workspace;

    public PublishContext(TempWorkspace workspace)
    {
        _workspace = workspace;
        ServerRoot = workspace.CreateDir("server");
        SourceRoot = workspace.CreateDir("source");
        PublishCachePath = workspace.Path($"cache/{Backend}/target.json");
        ManifestReader = new TestManifestReader();
        Target = new TestDirectoryTarget(ServerRoot);
    }

    public string ServerRoot { get; }
    public string SourceRoot { get; }
    public string PublishCachePath { get; }
    public TestManifestReader ManifestReader { get; }
    public TestDirectoryTarget Target { get; }

    public string BackendRoot => Target.ResolveBackendRoot(Backend);
    public string PackagesRoot => Path.Combine(BackendRoot, "Packages");
    public string PackageIndexPath => Path.Combine(BackendRoot, "PackageIndex.json");

    public TestPackage CreatePackage(string packageName, string version) =>
        new TestPackage(Path.Combine(SourceRoot, packageName), packageName, version, Backend);

    public string PackageDir(string packageName) => Path.Combine(PackagesRoot, packageName);

    public PublishRequest CreateRequest(TestPackage package, IFullPackageBaselineSource baselineSource = null) => new PublishRequest
    {
        BackendKey = Backend,
        SourcePackageDir = package.SourceDir,
        TargetId = Target.Id,
        ManifestReader = ManifestReader,
        PublishCachePath = PublishCachePath,
        // 基准 Full 解析入口由调用方注入；不注入时由编辑器注册表提供，场景中默认为无。
        FullPackageBaselineSource = baselineSource,
        // 身份由调用方注入，等价于发布 UI 从正式 Summary 解析出的包身份。
        Identity = new PackageBuildIdentity
        {
            PackageName = package.PackageName,
            Version = VersionNumber.Parse(package.Version),
            BackendId = Backend,
            BuildType = "Hotfix"
        }
    };

    public PushReceipt Publish(TestPackage package, IFullPackageBaselineSource baselineSource = null)
    {
        PushReceipt receipt = BuildPublisher.Push(CreateRequest(package, baselineSource), Target);
        Check.Equal(0, Target.PushCalls, "目录型目标不应走完整上传分支");
        return receipt;
    }

    /// <summary>目录快照（相对路径 → 字节），用于验证“服务器事实只读”：发布前后必须完全一致。</summary>
    public static Dictionary<string, byte[]> SnapshotDirectory(string rootDir)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!Directory.Exists(rootDir))
            return snapshot;

        string[] files = Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relative = files[i]
                .Substring(rootDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace('\\', '/');
            snapshot[relative] = File.ReadAllBytes(files[i]);
        }

        return snapshot;
    }

    public PackageIndex ReadPackageIndex() =>
        SerializationUtility.DeserializeJson<PackageIndex>(File.ReadAllText(PackageIndexPath));
}

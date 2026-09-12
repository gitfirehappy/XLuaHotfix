using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// T4「Summary 驱动的构建复用」纯 .NET 场景入口：验证历史制品按 Summary 索引复用的命中与失效矩阵、
/// 依赖事实回放、依赖闭合裁剪、指纹稳定性与内容逻辑名规则，不依赖 Unity。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var groups = new (string Name, Action<GateRun> Declare)[]
        {
            ("FingerprintStability", FingerprintStabilityTests.Declare),
            ("ArtifactReuse", ArtifactReuseTests.Declare),
            ("ReuseClosure", ReuseClosureTests.Declare),
            ("ContentNameRules", ContentNameRulesTests.Declare),
            ("DependencyIndexEquivalence", DependencyIndexEquivalenceTests.Declare)
        };

        int failures = 0;
        int checks = 0;
        int passedChecks = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            var run = new GateRun();
            try
            {
                groups[i].Declare(run);
                run.Execute(groups[i].Name);
                checks += run.Total;
                passedChecks += run.Passed;
                Console.WriteLine($"PASS {groups[i].Name} ({run.Passed}/{run.Total})");
            }
            catch (Exception ex)
            {
                checks += run.Total;
                passedChecks += run.Passed;
                failures++;
                Console.Error.WriteLine($"FAIL {groups[i].Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"build cache assertions: {passedChecks}/{checks} passed");
        Console.WriteLine($"build cache scenario groups: {groups.Length - failures}/{groups.Length} passed");
        return failures == 0 && checks > 0 && passedChecks == checks ? 0 : 1;
    }
}

/// <summary>
/// 子检查执行器：每个断言独立执行并单独报告 GREEN/RED，失败信息汇总后抛出，
/// 便于一次性看到全部差距。断言条数由 Total/Passed 汇总。
/// </summary>
internal sealed class GateRun
{
    private readonly List<(string Name, Action Run)> _checks = new List<(string, Action)>();

    public int Total { get; private set; }
    public int Passed { get; private set; }

    public void Check(string name, Action run) => _checks.Add((name, run));

    public void Execute(string groupName)
    {
        var failures = new List<string>();
        for (int i = 0; i < _checks.Count; i++)
        {
            try
            {
                _checks[i].Run();
                Passed++;
                Total++;
                Console.WriteLine($"  GREEN {_checks[i].Name}");
            }
            catch (Exception ex)
            {
                Total++;
                failures.Add($"{_checks[i].Name}: {ex.Message}");
                Console.WriteLine($"  RED   {_checks[i].Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{groupName}: {failures.Count}/{_checks.Count} 个子检查未满足 -> {string.Join(" | ", failures)}");
        }
    }
}

/// <summary>场景断言。失败时抛出携带定位信息的异常，由入口汇总。</summary>
internal static class Check
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}（期望={expected}, 实际={actual}）");
    }

    public static void Matches(string value, string pattern, string message)
    {
        if (value == null || !Regex.IsMatch(value, pattern))
            throw new InvalidOperationException($"{message}（实际={value ?? "<null>"}）");
    }

    public static void Contains(string source, string value, string message)
    {
        if (source == null || source.IndexOf(value, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException($"{message}（未找到 {value}）");
    }

    public static void NotContains(string source, string value, string message)
    {
        if (source != null && source.IndexOf(value, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"{message}（出现了 {value}）");
    }
}

/// <summary>
/// 每个用例独立使用的临时工作目录；用例结束即删除，仓库内不留产物。
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace(string name)
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "fyasset-build-cache-scenario",
            name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Resolve(string relativePath)
        => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string WriteText(string relativePath, string content)
    {
        string path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public string WriteBytes(string relativePath, byte[] content)
    {
        string path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, content);
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
            // 临时目录清理失败不影响断言结论。
        }
    }
}

/// <summary>
/// 历史制品复用夹具：在临时工作区内落地正式 Summary（复用索引）与历史包制品（字节来源）。
/// </summary>
internal static class ReuseFixture
{
    /// <summary>后端标识；Summary 按后端目录隔离。</summary>
    public const string Backend = "AB";

    /// <summary>目标平台事实。</summary>
    public const string Platform = "Standalone";

    /// <summary>与 Platform 不同的平台事实，用于平台失效用例。</summary>
    public const string OtherPlatform = "Android";

    /// <summary>构建配方指纹事实；真实值由 ABBuildContentFingerprint 计算。</summary>
    public const string Recipe = "recipe=3;platform=Standalone;compression=LZ4;unity=2022.3.62f3;backend=Mono";

    /// <summary>内容逻辑名事实。</summary>
    public const string ContentName = "ui_serialized_prefab_all";

    /// <summary>输入指纹事实。</summary>
    public const string Fingerprint = "fp-1";

    /// <summary>历史包目录（项目根下相对路径），包内内容位于 bundles/。</summary>
    public const string PackageRelativePath = "HotfixOutput/Packages/Full/AB/ReusePackage";

    /// <summary>把工作区设为项目根并建立摘要存储。</summary>
    public static BuildSummaryStore CreateStore(TempWorkspace workspace)
    {
        BuildPathManager.ProjectRoot = workspace.Root;
        return new BuildSummaryStore(workspace.Root);
    }

    /// <summary>在默认历史包目录内写入一个制品，返回其磁盘摘要。</summary>
    public static FileDigest WriteArtifact(TempWorkspace workspace, string fileName, byte[] content)
        => WriteArtifact(workspace, PackageRelativePath, fileName, content);

    /// <summary>在指定历史包目录内写入一个制品，返回其磁盘摘要。</summary>
    public static FileDigest WriteArtifact(
        TempWorkspace workspace,
        string packageRelativePath,
        string fileName,
        byte[] content)
    {
        string path = workspace.Resolve(
            string.Concat(packageRelativePath, "/", FYAssetSettings.BUNDLES_DIRECTORY_NAME, "/", fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, content);
        Check.True(FileDigest.TryCreate(path, fileName, out FileDigest digest), "夹具无法计算历史制品摘要");
        return digest;
    }

    /// <summary>构造一条内容复用事实；依赖事实按内容级输出文件名给出。</summary>
    public static SummaryContentFact Content(
        string contentIdentity,
        string inputFingerprint,
        in FileDigest digest,
        params string[] dependencyFileNames)
    {
        return new SummaryContentFact
        {
            ContentIdentity = contentIdentity,
            InputFingerprint = inputFingerprint,
            FileName = digest.Name,
            FileHash = digest.Hash,
            FileCRC = digest.CRC,
            FileSize = digest.Size,
            DependencyFileNames = new List<string>(dependencyFileNames ?? Array.Empty<string>())
        };
    }

    /// <summary>构造一份历史构建摘要骨架；默认与当前构建同平台、同配方且成功。</summary>
    public static CompleteBuildSummary Summary(
        string buildId,
        DateTime startedAtUtc,
        params SummaryContentFact[] contents)
        => Summary(buildId, Platform, Recipe, startedAtUtc, true, contents);

    /// <summary>构造一份历史构建摘要骨架。</summary>
    public static CompleteBuildSummary Summary(
        string buildId,
        string platform,
        string buildRecipeFingerprint,
        DateTime startedAtUtc,
        bool success,
        params SummaryContentFact[] contents)
    {
        var summary = new CompleteBuildSummary
        {
            BuildId = buildId,
            BackendId = Backend,
            Platform = platform,
            BuildRecipeFingerprint = buildRecipeFingerprint,
            ArtifactRelativePath = PackageRelativePath,
            StartedAt = startedAtUtc,
            FinishedAtUtc = startedAtUtc,
            Success = success,
            Statistics = new BuildStatistics()
        };

        for (int i = 0; i < contents.Length; i++)
            summary.Contents.Add(contents[i]);

        return summary;
    }

    /// <summary>写入正式摘要；不可覆盖已存在的不同内容摘要，写入失败立即报错。</summary>
    public static void Publish(BuildSummaryStore store, CompleteBuildSummary summary)
    {
        Check.True(store.TryWriteSummary(summary, out string error), $"写入正式摘要失败: {error}");
    }

    /// <summary>按当前构建事实调用复用服务；全部失效用例都通过覆盖 platform/recipe 表达。</summary>
    public static bool TryReuse(
        BuildSummaryStore store,
        string platform,
        string buildRecipeFingerprint,
        string contentIdentity,
        string inputFingerprint,
        string targetDirectory,
        out string fileName,
        out FileDigest digest,
        out List<string> dependencyFileNames,
        out string reason)
    {
        return BuildArtifactReuseService.TryReuse(
            store, Backend, platform, buildRecipeFingerprint, contentIdentity, inputFingerprint, targetDirectory,
            out fileName, out digest, out dependencyFileNames, out reason);
    }

    public static byte[] Bytes(int seed, int length)
    {
        var random = new Random(seed);
        var data = new byte[length];
        random.NextBytes(data);
        return data;
    }
}

/// <summary>
/// 仓库源码读取入口，用于内容逻辑名的源码级契约断言。
/// </summary>
internal static class RepoSource
{
    public static readonly string Root = FindRoot();

    public static string Read(string relativePath)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException($"受管源码文件不存在: {relativePath}", path);
        return File.ReadAllText(path);
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with Assets directory was not found.");
    }
}

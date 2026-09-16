using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>探针清单模型：与 FakePipeline 写入/读取的 ABManifest 一致。</summary>
internal sealed class FakeManifest
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }
    public List<FakeBundle> Bundles { get; set; } = new();
}

internal sealed class FakeBundle
{
    public string Name { get; set; }
    public long Size { get; set; }
    public uint Crc { get; set; }
}

internal sealed class FakePipeline : IHotfixPipeline
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { IncludeFields = true };

    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string BUNDLE_DIRECTORY_NAME = "Bundles";
    public const string EXTRA_FILE_NAME = "damaged.marker";

    public string ActivationFailureRootName;
    public List<string> MetadataWriteRoots = new();
    public FakeManifest RemoteManifest = new();

    public Task<HotfixStepResult> InitializeBackendAsync() => Task.FromResult(HotfixStepResult.Ok);

    public Task<HotfixPackageInspection> InspectPackageAsync(
        string packageRoot, PackageIndex expectedIndex, bool requirePackageDirectoryMatch = true)
    {
        FakeManifest manifest = ReadManifest(packageRoot);
        if (manifest == null)
            return Task.FromResult(HotfixPackageInspection.Incomplete(null, "manifest 缺失"));
        return Task.FromResult(HotfixPackageInspection.Inspect(
            packageRoot,
            expectedIndex,
            ToVersionInfo(manifest),
            true,
            string.Empty,
            requirePackageDirectoryMatch));
    }

    public Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        string remoteUrlRoot, int timeoutSeconds, int maxRetryCount, float retryBaseDelaySeconds)
        => Task.FromResult(ToVersionInfo(RemoteManifest));

    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
        => remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();

    public Task<HotfixStepResult> PersistRemoteMetadataAsync(
        HotfixContext ctx, int timeoutSeconds, int maxRetryCount, float retryBaseDelaySeconds)
    {
        MetadataWriteRoots.Add(ctx.TargetGUIDRoot);
        WriteManifest(ctx.TargetGUIDRoot, RemoteManifest);
        return Task.FromResult(HotfixStepResult.Ok);
    }

    public Task<HotfixStepResult> ActivatePackageAsync(string packageRoot)
    {
        if (string.Equals(Path.GetFileName(packageRoot), ActivationFailureRootName, StringComparison.Ordinal))
        {
            return Task.FromResult(HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, "探针注入的激活失败")));
        }
        return Task.FromResult(HotfixStepResult.Ok);
    }

    public static HotfixVersionInfo ToVersionInfo(FakeManifest manifest)
    {
        if (manifest == null)
            return null;
        var bundles = new List<BundleDownloadItem>();
        long total = 0;
        foreach (FakeBundle b in manifest.Bundles)
        {
            bundles.Add(new BundleDownloadItem { BundleName = b.Name, FileSize = b.Size, FileCRC = b.Crc });
            total += b.Size;
        }
        return new HotfixVersionInfo
        {
            ManifestHash = "hash",
            Version = new VersionNumber { Major = manifest.Major, Minor = manifest.Minor, Patch = manifest.Patch },
            BundleCount = bundles.Count,
            TotalSize = total,
            Bundles = bundles
        };
    }

    public static void WriteManifest(string packageRoot, FakeManifest manifest)
    {
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(
            Path.Combine(packageRoot, MANIFEST_FILE_NAME),
            JsonSerializer.Serialize(manifest, JsonOpts),
            new UTF8Encoding(false));
    }

    public static FakeManifest ReadManifest(string packageRoot)
    {
        string path = Path.Combine(packageRoot, MANIFEST_FILE_NAME);
        if (!File.Exists(path))
            return null;
        try { return JsonSerializer.Deserialize<FakeManifest>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }
    }

    public static void WriteBundle(string packageRoot, FakeBundle bundle, byte[] content)
    {
        string dir = Path.Combine(packageRoot, BUNDLE_DIRECTORY_NAME);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, bundle.Name), content);
    }
}

internal sealed class FakeFlow : HotfixFlowBase
{
    public readonly FakePipeline Pipeline = new();
    protected override string HotfixUrl => "http://server/hotfix";
    protected override string BackendModeName => "ABManifest";
    protected override int HotfixMaxRetryCount => 0;
    protected override float HotfixRetryBaseDelaySeconds => 0f;
    protected override int HotfixMetadataTimeoutSeconds => 1;
    protected override int HotfixBundleTimeoutSeconds => 1;
    protected override int GetActiveHandleCount() => 0;
    protected override RuntimeMessage ShutdownPackageManager() => null;
    protected override IHotfixPipeline CreatePipeline() => Pipeline;
    protected override Task<bool> FinishHotfix() => Task.FromResult(true);
}

internal static class HotfixFlowScenarioTests
{
    private const string BuiltInName = "Build_base";
    private const string HotfixName = "Build_hot";
    private const string NextName = "Build_next";

    private static readonly byte[] BundleContent = Encoding.UTF8.GetBytes("bundle-01");
    private static readonly byte[] OtherContent = Encoding.UTF8.GetBytes("bundle-02");

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { IncludeFields = true };
    private static int _failures;
    private static string _streaming;
    private static string _persistent;
    private static string _hotfixRoot;
    private static FakeManifest _remoteManifest;

    private static async Task<int> Main()
    {
        _streaming = Path.Combine(Path.GetTempPath(), "hotfix_flow_streaming");
        _persistent = Path.Combine(Path.GetTempPath(), "hotfix_flow_persistent");

        await ScenarioDamagedLocalFallsBackToBuiltIn();
        await ScenarioSamePackageRepairUsesIsolatedStaging();
        await ScenarioForwardFailureRestoresLocal();
        await ScenarioForwardSuccessDeletesOldPackage();
        await ScenarioRepairFailureKeepsDiagnostics();
        await ScenarioPrepareFailureKeepsStaging();
        await ScenarioCorruptPointerFileIsRemoved();
        await ScenarioRuntimeCheckPrepareApply();

        Console.WriteLine(_failures == 0
            ? "Hotfix flow scenarios: ALL PASS"
            : $"Hotfix flow scenarios: {_failures} FAILED");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>准备阶段下载失败：staging 保留诊断、正式路径不被创建、当前 Local 与指针不变。</summary>
    private static async Task ScenarioPrepareFailureKeepsStaging()
    {
        Console.WriteLine("Scenario 6: 准备失败 → staging 保留、不激活、当前 Local 不变");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        WriteLocalPackage(HotfixName, "1.1.0", BundleContent);
        SetRemote(NextName, "1.2.0", OtherContent);
        NetworkDownloader.RemoteFiles.Clear();

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();

        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "准备失败后当前内容仍是 Local");
        Check(RuntimePathManager.ActivePackageRoot == HotfixPackageRoot(HotfixName), "读取根仍是原 Local 包根");
        Check(!Directory.Exists(HotfixPackageRoot(NextName)), "正式目标路径未被创建");
        Check(Directory.Exists(StagingRoot(NextName)), "staging 失败产物保留");
        PackageIndex pointer = ReadPointer();
        Check(pointer != null && pointer.LatestPackage == HotfixName, "准备失败未改写本地指针");
    }

    /// <summary>指针文件本身损坏：移除指针并按 BuiltIn 启动，不把它当成可用本地内容。</summary>
    private static async Task ScenarioCorruptPointerFileIsRemoved()
    {
        Console.WriteLine("Scenario 7: 指针文件损坏 → 移除并按 BuiltIn 启动");
        ResetWorld("1.0.0");
        Directory.CreateDirectory(_hotfixRoot);
        File.WriteAllText(
            Path.Combine(_hotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME),
            "{ not-json",
            new UTF8Encoding(false));
        WriteLocalPackage(HotfixName, "1.1.0", BundleContent);
        NetworkDownloader.RemotePackageIndexJson = null;

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();

        Check(ReadPointer() == null, "损坏的指针文件已被移除");
        Check(flow.CurrentPackageRoot == flow.BuiltInPackageRoot, "当前候选内容为 BuiltIn");
        Check(RuntimePathManager.ActivePackageRoot == _streaming, "读取根为内置包根");
    }

    /// <summary>运行中 Check / Prepare / Apply：Apply 失败恢复原 Local，重试成功后删旧包。</summary>
    private static async Task ScenarioRuntimeCheckPrepareApply()
    {
        Console.WriteLine("Scenario 8: 运行中 Check/Prepare/Apply（失败恢复 + 重试成功）");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        WriteLocalPackage(HotfixName, "1.1.0", BundleContent);
        NetworkDownloader.RemotePackageIndexJson = null;

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();
        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "启动后使用本地包");

        SetRemote(NextName, "1.2.0", OtherContent);
        HotfixCheckResult check = await flow.CheckAsync();
        Check(check.HasUpdate && check.Action == HotfixStateAction.PrepareTarget, "Check 得到可准备的前向目标");

        HotfixStepResult prepared = await flow.PrepareAsync();
        Check(prepared.Success, "Prepare 成功");
        Check(flow.PreparedTargetName == NextName, "PreparedTargetName 为目标包名");
        Check(Directory.Exists(StagingRoot(NextName)), "目标内容准备在 staging");
        Check(!Directory.Exists(HotfixPackageRoot(NextName)), "准备阶段不创建正式目标目录");

        flow.Pipeline.ActivationFailureRootName = NextName;
        HotfixStepResult failed = await flow.ApplyAsync();
        Check(!failed.Success, "注入激活失败后 Apply 返回失败");
        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "Apply 失败后当前内容仍是 Local");
        Check(RuntimePathManager.ActivePackageRoot == HotfixPackageRoot(HotfixName), "读取根恢复为原 Local 包根");
        Check(!Directory.Exists(HotfixPackageRoot(NextName)), "失败目标未留在正式路径");
        Check(ReadPointer() != null && ReadPointer().LatestPackage == HotfixName, "失败路径未改写指针");

        flow.Pipeline.ActivationFailureRootName = null;
        HotfixCheckResult retryCheck = await flow.CheckAsync();
        Check(retryCheck.HasUpdate, "重试 Check 仍得到可准备目标");
        Check((await flow.PrepareAsync()).Success, "重试 Prepare 成功（复用 staging）");
        HotfixStepResult applied = await flow.ApplyAsync();
        Check(applied.Success, "重试 Apply 成功");
        Check(RuntimePathManager.ActivePackageRoot == HotfixPackageRoot(NextName), "读取根切到新包根");
        Check(!Directory.Exists(HotfixPackageRoot(HotfixName)), "旧 Local 包已删除");
        Check(
            !Directory.Exists(StagingRoot(NextName)) && !Directory.Exists(BackupRoot(NextName)),
            "staging/backup 已回收");
        Check(ReadPointer() != null && ReadPointer().LatestPackage == NextName, "成功后指针指向新包");
    }

    private static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "  PASS " : "  FAIL ") + message);
        if (!condition)
            _failures++;
    }

    private static VersionNumber ParseVersion(string text)
    {
        string[] parts = text.Split('.');
        return new VersionNumber { Major = int.Parse(parts[0]), Minor = int.Parse(parts[1]), Patch = int.Parse(parts[2]) };
    }

    private static uint ComputeCrc(byte[] content)
    {
        string temp = Path.Combine(Path.GetTempPath(), "hotfix_flow_crc.bin");
        File.WriteAllBytes(temp, content);
        uint crc = HashGenerator.GenerateFileCRC(temp);
        File.Delete(temp);
        return crc;
    }

    private static FakeBundle NewBundle(string name, byte[] content)
    {
        return new FakeBundle
        {
            Name = name,
            Size = content.Length,
            Crc = ComputeCrc(content)
        };
    }

    private static string HotfixPackageRoot(string name) => Path.Combine(_hotfixRoot, name);
    private static string StagingRoot(string name) => Path.Combine(_hotfixRoot, name + ".staging");
    private static string BackupRoot(string name) => Path.Combine(_hotfixRoot, name + ".backup");

    private static void ResetWorld(string builtInVersion)
    {
        if (Directory.Exists(_streaming)) Directory.Delete(_streaming, true);
        if (Directory.Exists(_persistent)) Directory.Delete(_persistent, true);
        Directory.CreateDirectory(_streaming);
        Directory.CreateDirectory(_persistent);

        var buildIndex = new BuildIndexData
        {
            BuildGUID = BuiltInName,
            BuildTime = "2026-09-11",
            IsDebug = true,
            Platform = "Windows",
            BackendMode = "ABManifest",
            Version = ParseVersion(builtInVersion),
            RuntimeMode = RuntimeMode.Online
        };
        File.WriteAllText(
            Path.Combine(_streaming, FYAssetSettings.BUILD_INDEX_FILENAME),
            JsonSerializer.Serialize(buildIndex, JsonOpts),
            new UTF8Encoding(false));

        FakeBundle bundle = NewBundle("a.bundle", BundleContent);
        var manifest = new FakeManifest
        {
            Major = ParseVersion(builtInVersion).Major,
            Minor = ParseVersion(builtInVersion).Minor,
            Patch = ParseVersion(builtInVersion).Patch,
            Bundles = new List<FakeBundle> { bundle }
        };
        FakePipeline.WriteManifest(_streaming, manifest);
        FakePipeline.WriteBundle(_streaming, bundle, BundleContent);

        _hotfixRoot = Path.Combine(_persistent, "HotfixFlowScenario", "Windows", "Debug", "Hotfix");
    }

    private static void WritePointer(string packageName, string version)
    {
        Directory.CreateDirectory(_hotfixRoot);
        var index = new PackageIndex
        {
            LatestPackage = packageName,
            LatestVersion = ParseVersion(version),
            BackendMode = "ABManifest"
        };
        File.WriteAllText(
            Path.Combine(_hotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME),
            JsonSerializer.Serialize(index, JsonOpts),
            new UTF8Encoding(false));
    }

    private static PackageIndex ReadPointer()
    {
        string path = Path.Combine(_hotfixRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<PackageIndex>(File.ReadAllText(path), JsonOpts);
    }

    private static void WriteLocalPackage(string name, string version, byte[] content)
    {
        string root = HotfixPackageRoot(name);
        FakeBundle bundle = NewBundle("a.bundle", content);
        var manifest = new FakeManifest
        {
            Major = ParseVersion(version).Major,
            Minor = ParseVersion(version).Minor,
            Patch = ParseVersion(version).Patch,
            Bundles = new List<FakeBundle> { bundle }
        };
        FakePipeline.WriteManifest(root, manifest);
        FakePipeline.WriteBundle(root, bundle, content);
    }

    private static void SetRemote(string packageName, string version, byte[] content)
    {
        FakeBundle bundle = NewBundle("a.bundle", content);
        _remoteManifest = new FakeManifest
        {
            Major = ParseVersion(version).Major,
            Minor = ParseVersion(version).Minor,
            Patch = ParseVersion(version).Patch,
            Bundles = new List<FakeBundle> { bundle }
        };
        var index = new PackageIndex
        {
            LatestPackage = packageName,
            LatestVersion = ParseVersion(version),
            BackendMode = "ABManifest"
        };
        NetworkDownloader.RemotePackageIndexJson = JsonSerializer.Serialize(index, JsonOpts);
        NetworkDownloader.RemoteFiles.Clear();
        NetworkDownloader.RemoteFiles[
            "http://server/hotfix/Packages/" + packageName + "/Bundles/" + bundle.Name] = content;
    }

    private static FakeFlow NewFlow()
    {
        var flow = new FakeFlow();
        flow.Pipeline.RemoteManifest = _remoteManifest;
        return flow;
    }

    /// <summary>损坏 Local 清指针：指针可信但包损坏，远端不可用时仍以 BuiltIn 启动。</summary>
    private static async Task ScenarioDamagedLocalFallsBackToBuiltIn()
    {
        Console.WriteLine("Scenario 1: 损坏 Local 清指针 → BuiltIn");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        Directory.CreateDirectory(HotfixPackageRoot(HotfixName));
        NetworkDownloader.RemotePackageIndexJson = null;

        string biPath = Path.Combine(_streaming, FYAssetSettings.BUILD_INDEX_FILENAME);
        string biJson = File.ReadAllText(biPath);
        Console.WriteLine("    [debug] streaming=" + Application.streamingAssetsPath);
        Console.WriteLine("    [debug] buildIndexJson=" + biJson);
        Console.WriteLine("    [debug] hasVersionField=" + VersionNumber.JsonHasObjectField(biJson, "Version"));

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();

        Check(ReadPointer() == null, "损坏包的本地指针已被移除");
        Check(flow.CurrentPackageRoot == flow.BuiltInPackageRoot, "当前候选内容回退 BuiltIn");
        Check(RuntimePathManager.ActivePackageRoot == _streaming, "读取根为内置包根");
        Check(Directory.Exists(HotfixPackageRoot(HotfixName)), "损坏目录保留为诊断物");
    }

    /// <summary>同包修复：staging 准备 → 换入正式根 → 删旧损坏目录，损坏目录的额外文件不进新包。</summary>
    private static async Task ScenarioSamePackageRepairUsesIsolatedStaging()
    {
        Console.WriteLine("Scenario 2: 同包修复走独立 staging 并成功换入");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        string damagedRoot = HotfixPackageRoot(HotfixName);
        Directory.CreateDirectory(damagedRoot);
        File.WriteAllText(Path.Combine(damagedRoot, FakePipeline.EXTRA_FILE_NAME), "old-damaged");
        SetRemote(HotfixName, "1.1.0", OtherContent);

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();

        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "修复成功后当前内容为 Local");
        Check(RuntimePathManager.ActivePackageRoot == damagedRoot, "读取根为正式包根");
        Check(File.Exists(Path.Combine(damagedRoot, FakePipeline.MANIFEST_FILE_NAME)), "正式包根上是新内容");
        Check(
            !File.Exists(Path.Combine(damagedRoot, FakePipeline.EXTRA_FILE_NAME)),
            "旧损坏目录未被原地覆盖（额外文件不存在）");
        Check(!Directory.Exists(StagingRoot(HotfixName)), "staging 已换出并被回收");
        Check(!Directory.Exists(BackupRoot(HotfixName)), "旧损坏目录 backup 已删除");
        Check(
            flow.Pipeline.MetadataWriteRoots.All(r => r == StagingRoot(HotfixName)),
            "元数据只写入 staging");
        PackageIndex pointer = ReadPointer();
        Check(pointer != null && pointer.LatestPackage == HotfixName, "成功后指针指向正式包");
    }

    /// <summary>前向更新激活失败：恢复原 Local，指针不变，失败产物回到 staging 保留诊断。</summary>
    private static async Task ScenarioForwardFailureRestoresLocal()
    {
        Console.WriteLine("Scenario 3: 前向更新失败 → 恢复 Local 且不写指针");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        WriteLocalPackage(HotfixName, "1.1.0", BundleContent);
        SetRemote(NextName, "1.2.0", OtherContent);

        FakeFlow flow = NewFlow();
        flow.Pipeline.ActivationFailureRootName = NextName;
        await flow.InitializeAsync();

        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "失败后当前内容仍是 Local");
        Check(RuntimePathManager.ActivePackageRoot == HotfixPackageRoot(HotfixName), "读取根恢复为原 Local 包根");
        Check(!Directory.Exists(HotfixPackageRoot(NextName)), "失败的目标内容未留在正式路径");
        Check(Directory.Exists(StagingRoot(NextName)), "失败产物保留在 staging 供诊断");
        PackageIndex pointer = ReadPointer();
        Check(pointer != null && pointer.LatestPackage == HotfixName, "失败路径未改写本地指针");
    }

    /// <summary>前向更新成功：正式根为新内容，旧包与 staging/backup 全部回收。</summary>
    private static async Task ScenarioForwardSuccessDeletesOldPackage()
    {
        Console.WriteLine("Scenario 4: 前向更新成功 → 删旧包");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        WriteLocalPackage(HotfixName, "1.1.0", BundleContent);
        SetRemote(NextName, "1.2.0", OtherContent);

        FakeFlow flow = NewFlow();
        await flow.InitializeAsync();

        Check(flow.CurrentPackageRoot != flow.BuiltInPackageRoot, "成功后当前内容为 Local");
        Check(RuntimePathManager.ActivePackageRoot == HotfixPackageRoot(NextName), "读取根切到新包根");
        Check(
            File.Exists(Path.Combine(HotfixPackageRoot(NextName), FakePipeline.MANIFEST_FILE_NAME)),
            "新包根内容就位");
        Check(!Directory.Exists(HotfixPackageRoot(HotfixName)), "旧 Local 包目录已删除");
        Check(
            !Directory.Exists(StagingRoot(NextName)) && !Directory.Exists(BackupRoot(NextName)),
            "staging/backup 已回收");
        PackageIndex pointer = ReadPointer();
        Check(pointer != null && pointer.LatestPackage == NextName, "成功后指针指向新包");
    }

    /// <summary>损坏 Local 的同包修复激活失败：继续 BuiltIn、无指针、staging 与损坏目录都保留。</summary>
    private static async Task ScenarioRepairFailureKeepsDiagnostics()
    {
        Console.WriteLine("Scenario 5: 同包修复失败 → BuiltIn + 诊断物保留");
        ResetWorld("1.0.0");
        WritePointer(HotfixName, "1.1.0");
        string damagedRoot = HotfixPackageRoot(HotfixName);
        Directory.CreateDirectory(damagedRoot);
        File.WriteAllText(Path.Combine(damagedRoot, FakePipeline.EXTRA_FILE_NAME), "old-damaged");
        SetRemote(HotfixName, "1.1.0", OtherContent);

        FakeFlow flow = NewFlow();
        flow.Pipeline.ActivationFailureRootName = HotfixName;
        await flow.InitializeAsync();

        Check(flow.CurrentPackageRoot == flow.BuiltInPackageRoot, "修复失败后使用 BuiltIn");
        Check(RuntimePathManager.ActivePackageRoot == _streaming, "读取根为内置包根");
        Check(ReadPointer() == null, "修复失败后没有本地指针");
        Check(
            File.Exists(Path.Combine(damagedRoot, FakePipeline.EXTRA_FILE_NAME)),
            "旧损坏目录内容原样保留");
        Check(Directory.Exists(StagingRoot(HotfixName)), "staging 失败产物保留");
        Check(!Directory.Exists(BackupRoot(HotfixName)), "backup 不残留");
    }
}

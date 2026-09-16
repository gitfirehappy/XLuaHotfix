using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Summary 驱动的历史制品复用契约：
/// 命中（相同身份+指纹+制品存在+摘要一致 → 复制成功）、失效矩阵（身份/指纹/配方/平台/制品缺失/摘要不符 → 拒绝且给 reason）、
/// Summary 缺失或损坏只返回失败不抛异常。
/// </summary>
internal static class ArtifactReuseTests
{
    public static void Declare(GateRun run)
    {
        run.Check("HitCopiesArtifactAndKeepsDependencyFacts", HitCopiesArtifactAndKeepsDependencyFacts);
        run.Check("NewestMatchingSummaryWins", NewestMatchingSummaryWins);
        run.Check("FallsBackToOlderSummaryWhenNewestArtifactMissing", FallsBackToOlderSummaryWhenNewestArtifactMissing);
        run.Check("CopiedArtifactSummaryIsReVerified", CopiedArtifactSummaryIsReVerified);
        run.Check("ContentNameMismatchIsRejected", ContentNameMismatchIsRejected);
        run.Check("InputFingerprintMismatchIsRejected", InputFingerprintMismatchIsRejected);
        run.Check("RecipeMismatchIsRejected", RecipeMismatchIsRejected);
        run.Check("PlatformMismatchIsRejected", PlatformMismatchIsRejected);
        run.Check("FailedSummaryIsNotAReuseCandidate", FailedSummaryIsNotAReuseCandidate);
        run.Check("MissingArtifactIsRejected", MissingArtifactIsRejected);
        run.Check("TruncatedArtifactIsRejected", TruncatedArtifactIsRejected);
        run.Check("ArtifactDigestMismatchIsRejected", ArtifactDigestMismatchIsRejected);
        run.Check("IncompleteRecordedDigestIsRejected", IncompleteRecordedDigestIsRejected);
        run.Check("MissingRecordedDependencyFactsIsRejected", MissingRecordedDependencyFactsIsRejected);
        run.Check("MissingArtifactPathIsRejected", MissingArtifactPathIsRejected);
        run.Check("MultipleArtifactRecordsAreRejected", MultipleArtifactRecordsAreRejected);
        run.Check("MissingSummaryDirectoryIsRejectedWithoutThrowing", MissingSummaryDirectoryIsRejectedWithoutThrowing);
        run.Check("CorruptSummaryOnlyLosesOptimization", CorruptSummaryOnlyLosesOptimization);
        run.Check("RejectedCandidateDoesNotCopyAnything", RejectedCandidateDoesNotCopyAnything);
        run.Check("ReuseNeverThrows", ReuseNeverThrows);
    }

    private static void HitCopiesArtifactAndKeepsDependencyFacts()
    {
        using var workspace = new TempWorkspace("reuse-hit");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        byte[] content = ReuseFixture.Bytes(11, 2048);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", content);
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1",
            DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest, "dep_a.bundle", "dep_b.bundle")));

        string targetDirectory = workspace.Resolve("out/hit");
        Check.True(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                targetDirectory, out string fileName, out FileHelper.FileDigest copied, out List<string> dependencies, out string reason),
            $"同身份、同指纹、制品摘要一致时必须命中（原因={reason}）");

        Check.Equal("ui.bundle", fileName, "复用命中的文件名必须沿用历史包内的物理名");
        Check.Equal(digest.Hash, copied.Hash, "返回的摘要必须来自实际复制到本地文件");
        Check.Equal(digest.Size, copied.Size, "返回的摘要必须包含真实字节数");
        Check.Equal(2, dependencies.Count, "必须回放 Summary 记录的依赖事实");
        Check.Equal("dep_a.bundle", dependencies[0], "依赖事实必须原样回放");
        Check.Equal("dep_b.bundle", dependencies[1], "依赖事实必须原样回放");

        string targetPath = Path.Combine(targetDirectory, "ui.bundle");
        Check.True(File.Exists(targetPath), "命中后必须把制品复制到本次输出目录");
        Check.Equal(content.Length, new FileInfo(targetPath).Length, "复制出的字节数必须与历史制品一致");
        Check.True(File.Exists(Path.Combine(workspace.Root, ReuseFixture.PackageRelativePath, "bundles", "ui.bundle")),
            "复用不得删除或移动历史包内的源制品");
    }

    private static void NewestMatchingSummaryWins()
    {
        using var workspace = new TempWorkspace("reuse-newest");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest older = ReuseFixture.WriteArtifact(workspace, "newer-probe.bundle", ReuseFixture.Bytes(12, 128));
        FileHelper.FileDigest newest = ReuseFixture.WriteArtifact(workspace, "newest.bundle", ReuseFixture.Bytes(13, 256));

        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-old", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, older)));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-new", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, newest)));

        Check.True(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/newest"), out string fileName, out _, out _, out string reason),
            $"存在多个候选时必须命中最新构建的制品（原因={reason}）");
        Check.Equal("newest.bundle", fileName, "必须按 StartedAtUtc 倒序取最新构建的制品");
    }

    private static void FallsBackToOlderSummaryWhenNewestArtifactMissing()
    {
        using var workspace = new TempWorkspace("reuse-fallback");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest older = ReuseFixture.WriteArtifact(workspace, "older.bundle", ReuseFixture.Bytes(14, 128));

        // 最新的 Summary 指向并不存在的制品；复用的失败只损失优化，应继续尝试更早的候选。
        var missing = new ContentReuseRecord
        {
            ContentName = ReuseFixture.ContentName,
            InputFingerprint = ReuseFixture.Fingerprint,
            FileName = "gone.bundle",
            Hash = "0123456789abcdef0123456789abcdef",
            CRC = 1,
            Size = 8
        };
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-newest-broken", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), missing));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-older", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, older)));

        Check.True(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/fallback"), out string fileName, out _, out _, out string reason),
            $"最新候选的制品缺失时必须回退到更早的可用候选（原因={reason}）");
        Check.Equal("older.bundle", fileName, "回退命中的必须是仍然存在且摘要一致的历史制品");
    }

    private static void CopiedArtifactSummaryIsReVerified()
    {
        using var workspace = new TempWorkspace("reuse-reverify");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        byte[] content = ReuseFixture.Bytes(15, 4096);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "shared.bundle", content);
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content("shared_content", ReuseFixture.Fingerprint, digest)));

        Check.True(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, "shared_content", ReuseFixture.Fingerprint,
                workspace.Resolve("out/reverify"), out _, out FileHelper.FileDigest copied, out List<string> dependencies, out string reason),
            $"命中必须返回复制后的校验结果（原因={reason}）");

        byte[] copiedBytes = File.ReadAllBytes(Path.Combine(workspace.Resolve("out/reverify"), copied.Name));
        Check.Equal(content.Length, copiedBytes.Length, "复制后的文件长度必须与记录的 Size 一致");
        Check.Equal(digest.Hash, copied.Hash, "返回的 Hash 必须是复制后重新校验的结果");
        Check.Equal(0, dependencies.Count, "空依赖事实必须原样回放而不是返回 null");
        Check.True(dependencies != null, "依赖事实不得为 null，否则依赖下标合并会缺条目");
    }

    private static void ContentNameMismatchIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-identity");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(16, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content("other_content", ReuseFixture.Fingerprint, digest)));

        string targetDirectory = workspace.Resolve("out/identity");
        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                targetDirectory, out _, out _, out _, out string reason),
            "内容身份不匹配时不得复用");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
        Check.False(File.Exists(Path.Combine(targetDirectory, "ui.bundle")), "拒绝时不得复制任何制品");
    }

    private static void InputFingerprintMismatchIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-fingerprint");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(17, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, "fp-old", digest)));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/fingerprint"), out _, out _, out _, out string reason),
            "输入指纹不同时不得复用");
        Check.Contains(reason, "指纹", "拒绝原因必须指向输入指纹不一致");
    }

    private static void RecipeMismatchIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-recipe");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(18, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", ReuseFixture.Platform, "recipe-other", DateTime.UtcNow,
            true, ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/recipe"), out _, out _, out _, out string reason),
            "构建配方不同时不得复用");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
    }

    private static void PlatformMismatchIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-platform");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(19, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", ReuseFixture.OtherPlatform, ReuseFixture.Recipe, DateTime.UtcNow,
            true, ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/platform"), out _, out _, out _, out string reason),
            "目标平台不同时不得复用");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
    }

    private static void FailedSummaryIsNotAReuseCandidate()
    {
        using var workspace = new TempWorkspace("reuse-failed");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(20, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", ReuseFixture.Platform, ReuseFixture.Recipe, DateTime.UtcNow,
            false, ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/failed"), out _, out _, out _, out string reason),
            "未成功的构建不得作为复用来源");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
    }

    private static void MissingArtifactIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-missing-artifact");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        byte[] content = ReuseFixture.Bytes(21, 512);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", content);
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        File.Delete(Path.Combine(workspace.Root, ReuseFixture.PackageRelativePath, "bundles", "ui.bundle"));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/missing"), out _, out _, out _, out string reason),
            "制品缺失时不得复用");
        Check.Contains(reason, "缺失", "拒绝原因必须指向制品缺失");
    }

    private static void TruncatedArtifactIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-truncated");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(22, 4096));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        string artifactPath = Path.Combine(workspace.Root, ReuseFixture.PackageRelativePath, "bundles", "ui.bundle");
        byte[] truncated = new byte[512];
        Array.Copy(File.ReadAllBytes(artifactPath), truncated, truncated.Length);
        File.WriteAllBytes(artifactPath, truncated);

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/truncated"), out _, out _, out _, out string reason),
            "制品被截断（Size/Hash 不符）时不得复用");
        Check.Contains(reason, "不一致", "拒绝原因必须指向制品与记录不一致");
    }

    private static void ArtifactDigestMismatchIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-digest-mismatch");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(23, 1024));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        // 同长度但内容不同的制品：Size 一致，Hash/CRC 必须拦住复用。
        string artifactPath = Path.Combine(workspace.Root, ReuseFixture.PackageRelativePath, "bundles", "ui.bundle");
        File.WriteAllBytes(artifactPath, ReuseFixture.Bytes(24, 1024));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/digest-mismatch"), out _, out _, out _, out string reason),
            "制品 Hash/CRC 与记录不一致时不得复用");
        Check.Contains(reason, "不一致", "拒绝原因必须指向制品与记录不一致");
    }

    private static void IncompleteRecordedDigestIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-incomplete-record");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(25, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                new FileHelper.FileDigest(digest.Name, string.Empty, digest.CRC, digest.Size))));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/incomplete"), out _, out _, out _, out string reason),
            "Summary 记录的制品摘要不完整时不得复用");
        Check.Contains(reason, "不完整", "拒绝原因必须指向记录不完整");
    }

    private static void MissingRecordedDependencyFactsIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-no-dependency-facts");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(31, 256));

        ContentReuseRecord fact = ReuseFixture.Content(
            ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest, "dep.bundle");
        fact.DependencyFileNames = null;
        ReuseFixture.Publish(store, ReuseFixture.Summary("build-1", DateTime.UtcNow, fact));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/no-dependency-facts"), out _, out _, out _, out string reason),
            "历史记录缺少依赖事实时不得复用，否则依赖下标会与全量构建不一致");
        Check.Contains(reason, "依赖事实", "拒绝原因必须指向依赖事实缺失");
    }

    private static void MissingArtifactPathIsRejected()
    {
        using var workspace = new TempWorkspace("reuse-no-artifact-path");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(26, 256));

        CompleteBuildSummary summary = ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest));
        summary.ArtifactRelativePath = string.Empty;
        ReuseFixture.Publish(store, summary);

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/no-path"), out _, out _, out _, out string reason),
            "Summary 缺少制品路径时不得复用（不得扫描目录猜测制品位置）");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
    }

    private static void MultipleArtifactRecordsAreRejected()
    {
        using var workspace = new TempWorkspace("reuse-multi-record");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest first = ReuseFixture.WriteArtifact(workspace, "a.bundle", ReuseFixture.Bytes(27, 128));
        FileHelper.FileDigest second = ReuseFixture.WriteArtifact(workspace, "b.bundle", ReuseFixture.Bytes(28, 128));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, first),
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, second)));

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/multi"), out _, out _, out _, out string reason),
            "同一内容有多条同身份制品记录时不得复用（索引不可信）");
        Check.Contains(reason, "多条", "拒绝原因必须指向重复记录");
    }

    private static void MissingSummaryDirectoryIsRejectedWithoutThrowing()
    {
        using var workspace = new TempWorkspace("reuse-no-summaries");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);

        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/empty"), out _, out _, out _, out string reason),
            "没有任何正式摘要时不得复用");
        Check.True(!string.IsNullOrEmpty(reason), "拒绝必须给出 reason");
    }

    private static void CorruptSummaryOnlyLosesOptimization()
    {
        using var workspace = new TempWorkspace("reuse-corrupt-summary");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(29, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        // 同一后端目录里的损坏摘要必须被跳过，不能影响可读摘要或抛出异常。
        workspace.WriteText("BuildData/Summaries/AB/broken.json", "{ this is not a summary");

        Check.True(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                workspace.Resolve("out/corrupt"), out _, out _, out _, out string reason),
            $"损坏的摘要只损失优化，不得阻断可读摘要的复用（原因={reason}）");

        using var emptyWorkspace = new TempWorkspace("reuse-corrupt-only");
        BuildSummaryStore emptyStore = ReuseFixture.CreateStore(emptyWorkspace);
        emptyWorkspace.WriteText("BuildData/Summaries/AB/broken.json", "{ this is not a summary");

        Check.False(
            ReuseFixture.TryReuse(
                emptyStore, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                emptyWorkspace.Resolve("out/corrupt-only"), out _, out _, out _, out string corruptOnlyReason),
            "只有损坏摘要时不得复用，但必须返回失败而不是抛异常");
        Check.True(!string.IsNullOrEmpty(corruptOnlyReason), "拒绝必须给出 reason");
    }

    private static void RejectedCandidateDoesNotCopyAnything()
    {
        using var workspace = new TempWorkspace("reuse-no-copy");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(30, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", ReuseFixture.Platform, "recipe-other", DateTime.UtcNow,
            true, ReuseFixture.Content(ReuseFixture.ContentName, ReuseFixture.Fingerprint, digest)));

        string targetDirectory = workspace.Resolve("out/no-copy");
        Check.False(
            ReuseFixture.TryReuse(
                store, ReuseFixture.Platform, ReuseFixture.Recipe, ReuseFixture.ContentName, ReuseFixture.Fingerprint,
                targetDirectory, out string fileName, out _, out _, out _),
            "配方不匹配时不得复用");
        Check.True(fileName == null, "未命中时不得给出文件名");
        Check.False(Directory.Exists(targetDirectory), "未命中时不得在本次输出目录留下任何内容");
    }

    private static void ReuseNeverThrows()
    {
        using var workspace = new TempWorkspace("reuse-defensive");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        string target = workspace.Resolve("out/defensive");

        Check.False(ReuseFixture.TryReuse(store, ReuseFixture.Platform, ReuseFixture.Recipe,
            ReuseFixture.ContentName, ReuseFixture.Fingerprint, target, out _, out _, out _, out string nullStoreReason),
            "存储为 null 时必须返回失败");
        Check.True(!string.IsNullOrEmpty(nullStoreReason), "存储缺失必须给出 reason");

        Check.False(ReuseFixture.TryReuse(store, ReuseFixture.Platform, ReuseFixture.Recipe,
            string.Empty, ReuseFixture.Fingerprint, target, out _, out _, out _, out string noIdentityReason),
            "内容身份为空时必须返回失败");
        Check.True(!string.IsNullOrEmpty(noIdentityReason), "内容身份缺失必须给出 reason");

        Check.False(ReuseFixture.TryReuse(store, ReuseFixture.Platform, ReuseFixture.Recipe,
            ReuseFixture.ContentName, string.Empty, target, out _, out _, out _, out string noFingerprintReason),
            "输入指纹为空时必须返回失败");
        Check.True(!string.IsNullOrEmpty(noFingerprintReason), "输入指纹缺失必须给出 reason");

        Check.False(ReuseFixture.TryReuse(store, ReuseFixture.Platform, ReuseFixture.Recipe,
            ReuseFixture.ContentName, ReuseFixture.Fingerprint, string.Empty, out _, out _, out _, out string noTargetReason),
            "目标目录为空时必须返回失败");
        Check.True(!string.IsNullOrEmpty(noTargetReason), "目标目录缺失必须给出 reason");

        string projectRoot = BuildPathManager.ProjectRoot;
        try
        {
            BuildPathManager.ProjectRoot = string.Empty;
            Check.False(ReuseFixture.TryReuse(store, ReuseFixture.Platform, ReuseFixture.Recipe,
                ReuseFixture.ContentName, ReuseFixture.Fingerprint, target, out _, out _, out _, out string noRootReason),
                "项目根不可用时必须返回失败");
            Check.True(!string.IsNullOrEmpty(noRootReason), "项目根不可用必须给出 reason");
        }
        finally
        {
            BuildPathManager.ProjectRoot = projectRoot;
        }
    }
}
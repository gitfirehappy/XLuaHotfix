using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 内容级依赖事实的持久化与「复用 + 重建 == 全量构建」等价性。
/// 被复用的内容不会出现在本轮 Unity AssetBundleManifest 里，只能靠正式 Summary 回放依赖事实；
/// 缺少或无法解析依赖事实的内容必须保守重建，否则依赖下标会与全量构建不同。
/// </summary>
internal static class DependencyIndexEquivalenceTests
{
    public static void Declare(GateRun run)
    {
        run.Check("MixedBuildProducesSameDependencyIndicesAsFullBuild", MixedBuildProducesSameDependencyIndicesAsFullBuild);
        run.Check("MixedBuildWithAllReusedKeepsFullIndices", MixedBuildWithAllReusedKeepsFullIndices);
        run.Check("ConflictingDependencyFactsAreRejected", ConflictingDependencyFactsAreRejected);
        run.Check("ReusedDependencyFactWithoutListIsNotResolvable", ReusedDependencyFactWithoutListIsNotResolvable);
        run.Check("UnknownReusedDependencyIsNotResolvable", UnknownReusedDependencyIsNotResolvable);
        run.Check("SummaryRoundTripKeepsContentDependencyFacts", SummaryRoundTripKeepsContentDependencyFacts);
        run.Check("ResolvedIndicesAreSortedDeduplicatedAndSelfFree", ResolvedIndicesAreSortedDeduplicatedAndSelfFree);
        run.Check("EmptyDependencyListIsValidFacts", EmptyDependencyListIsValidFacts);
    }

    /// <summary>
    /// 全量构建产出完整依赖事实；混合构建中复用内容回放 Summary 事实、重建内容取 Unity 事实，
    /// 合并后的依赖下标必须与全量构建逐个内容相等。
    /// </summary>
    private static void MixedBuildProducesSameDependencyIndicesAsFullBuild()
    {
        // 全量构建：ui_root → shared_atlas → shared_texture，sfx_root 无依赖
        var fullBuild = Facts(
            ("ui_root", new[] { "shared_atlas.bundle" }),
            ("shared_atlas.bundle", new[] { "shared_texture.bundle" }),
            ("shared_texture.bundle", new string[0]),
            ("sfx_root", new string[0]));

        var order = new List<string> { "ui_root", "sfx_root", "shared_atlas.bundle", "shared_texture.bundle" };
        Check.True(
            ContentDependencyIndexResolver.TryBuildFileIndex(order, out Dictionary<string, int> indexByFileName, out string indexReason),
            $"全量构建的文件名索引必须可建立（原因={indexReason}）");

        var fullIndices = ResolveAll(fullBuild, indexByFileName);

        // 混合构建：只有 ui_root 与 sfx_root 交给 Unity 重建，共享内容复用历史制品
        var rebuilt = Facts(("ui_root", new[] { "shared_atlas.bundle" }), ("sfx_root", new string[0]));
        var reused = Facts(
            ("shared_atlas.bundle", new[] { "shared_texture.bundle" }),
            ("shared_texture.bundle", new string[0]));

        Check.True(
            ContentDependencyIndexResolver.TryMergeDependencyNames(rebuilt, reused, out Dictionary<string, List<string>> merged, out List<string> problems),
            $"混合构建的两份依赖事实必须可合并（问题={string.Join(" | ", problems)}）");

        var mixedIndices = ResolveAll(ToFacts(merged), indexByFileName);

        Check.Equal(fullIndices.Count, mixedIndices.Count, "两种构建产出的内容数量必须一致");
        foreach (var pair in fullIndices)
        {
            Check.True(mixedIndices.ContainsKey(pair.Key), $"混合构建缺少内容 '{pair.Key}' 的依赖下标");
            Check.Equal(
                string.Join(",", pair.Value),
                string.Join(",", mixedIndices[pair.Key]),
                $"内容 '{pair.Key}' 的依赖下标必须与全量构建一致");
        }
    }

    /// <summary>全部内容命中的构建：依赖事实只来自 Summary 回放，下标仍必须与全量构建一致。</summary>
    private static void MixedBuildWithAllReusedKeepsFullIndices()
    {
        var fullBuild = Facts(
            ("root.bundle", new[] { "shared.bundle" }),
            ("shared.bundle", new string[0]));
        var order = new List<string> { "root.bundle", "shared.bundle" };
        Check.True(ContentDependencyIndexResolver.TryBuildFileIndex(order, out Dictionary<string, int> indexByFileName, out _), "文件名索引必须可建立");
        var fullIndices = ResolveAll(fullBuild, indexByFileName);

        Check.True(
            ContentDependencyIndexResolver.TryMergeDependencyNames(
                new Dictionary<string, IList<string>>(StringComparer.Ordinal),
                fullBuild,
                out Dictionary<string, List<string>> merged,
                out List<string> problems),
            $"全复用构建必须可合并（问题={string.Join(" | ", problems)}）");

        var reusedIndices = ResolveAll(ToFacts(merged), indexByFileName);
        Check.Equal(
            string.Join(",", fullIndices["root.bundle"]),
            string.Join(",", reusedIndices["root.bundle"]),
            "全复用构建的依赖下标必须与全量构建一致");
    }

    /// <summary>同一内容在两份来源给出不同依赖事实时必须报冲突，调用方据此放弃复用。</summary>
    private static void ConflictingDependencyFactsAreRejected()
    {
        var rebuilt = Facts(("a", new[] { "b.bundle" }));
        var reused = Facts(("a", new[] { "c.bundle" }));

        Check.False(
            ContentDependencyIndexResolver.TryMergeDependencyNames(rebuilt, reused, out _, out List<string> problems),
            "同一内容的两份依赖事实不一致时必须判定合并失败");
        Check.True(problems.Count > 0, "合并失败必须给出可定位的问题说明");
    }

    /// <summary>Summary 记录缺少依赖事实（null）时必须判定为不可解析，复用候选退回重建。</summary>
    private static void ReusedDependencyFactWithoutListIsNotResolvable()
    {
        Check.False(
            ContentDependencyIndexResolver.AreDependenciesResolvable(
                "ui_content", null, new[] { "ui.bundle" }, out string reason),
            "缺少依赖事实时不得判定为可解析");
        Check.True(!string.IsNullOrEmpty(reason), "不可解析必须给出原因");
    }

    /// <summary>Summary 记录的依赖名已不在本次内容集合中时不得复用，必须保守重建。</summary>
    private static void UnknownReusedDependencyIsNotResolvable()
    {
        Check.False(
            ContentDependencyIndexResolver.AreDependenciesResolvable(
                "ui_content", new[] { "gone.bundle" }, new[] { "ui_content", "other.bundle" }, out string reason),
            "依赖不在本次内容集合时必须判定为不可解析");
        Check.True(reason != null && reason.Contains("gone.bundle"), "原因必须包含无法解析的依赖名");

        Check.True(
            ContentDependencyIndexResolver.AreDependenciesResolvable(
                "ui_content", new[] { "other.bundle" }, new[] { "ui_content", "other.bundle" }, out _),
            "集合内存在的依赖名必须判定为可解析");
    }

    /// <summary>正式 Summary 必须把内容级依赖事实原样持久化，供后续构建回放。</summary>
    private static void SummaryRoundTripKeepsContentDependencyFacts()
    {
        using var workspace = new TempWorkspace("deps-summary-round-trip");
        BuildSummaryStore store = ReuseFixture.CreateStore(workspace);
        FileHelper.FileDigest digest = ReuseFixture.WriteArtifact(workspace, "ui.bundle", ReuseFixture.Bytes(32, 256));
        ReuseFixture.Publish(store, ReuseFixture.Summary(
            "build-1", DateTime.UtcNow,
            ReuseFixture.Content("ui_content", "fp-1", digest, "z.bundle", "a.bundle")));

        string json = File.ReadAllText(
            Path.Combine(store.RootDir, ReuseFixture.Backend, "build-1.json"), Encoding.UTF8);
        // 序列化器的大小写策略由生产端决定，此处只断言字段确实落盘。
        Check.True(
            json.IndexOf("dependencyFileNames", StringComparison.OrdinalIgnoreCase) >= 0,
            "写出的正式摘要必须带依赖事实字段");

        Check.True(
            store.TryReadSummaryDocument(ReuseFixture.Backend, "build-1", out CompleteBuildSummary.SummaryDocument document, out string error),
            $"写出的正式摘要必须可解析（原因={error}）");
        Check.Equal(1, document.Contents.Count, "解析后内容事实数量必须一致");

        List<string> dependencies = document.Contents[0].DependencyFileNames;
        Check.True(dependencies != null, "解析后必须保留依赖事实");
        Check.Equal(2, dependencies.Count, "解析后依赖数量必须一致");
        Check.Equal("z.bundle", dependencies[0], "依赖名必须按写入顺序保留");
        Check.Equal("a.bundle", dependencies[1], "依赖名必须按写入顺序保留");

        Check.True(
            ContentDependencyIndexResolver.AreDependenciesResolvable(
                "ui_content", dependencies, new[] { "ui.bundle", "a.bundle", "z.bundle" }, out _),
            "回放的依赖事实必须可用于可解析性校验");
    }

    /// <summary>依赖下标必须升序、去重、忽略自依赖。</summary>
    private static void ResolvedIndicesAreSortedDeduplicatedAndSelfFree()
    {
        var order = new List<string> { "self.bundle", "b.bundle", "a.bundle" };
        Check.True(ContentDependencyIndexResolver.TryBuildFileIndex(order, out Dictionary<string, int> indexByFileName, out _), "文件名索引必须可建立");

        Check.True(
            ContentDependencyIndexResolver.TryResolveDependencyIndices(
                "self.bundle",
                new[] { "b.bundle", "a.bundle", "b.bundle", "self.bundle" },
                indexByFileName,
                out int[] indices,
                out string failureReason),
            $"依赖下标必须可换算（原因={failureReason}）");

        Check.Equal(2, indices.Length, "重复与自依赖必须被剔除");
        Check.Equal(1, indices[0], "下标必须升序排列");
        Check.Equal(2, indices[1], "下标必须升序排列");

        Check.False(
            ContentDependencyIndexResolver.TryResolveDependencyIndices(
                "self.bundle", new[] { "missing.bundle" }, indexByFileName, out _, out string missingReason),
            "依赖名不存在时必须判定失败，避免写出错误下标");
        Check.True(missingReason != null && missingReason.Contains("missing.bundle"), "失败原因必须包含缺失依赖名");
    }

    /// <summary>空依赖集合是合法事实（叶子内容），不得被当成缺失。</summary>
    private static void EmptyDependencyListIsValidFacts()
    {
        Check.True(
            ContentDependencyIndexResolver.AreDependenciesResolvable(
                "leaf_content", new string[0], new[] { "leaf.bundle" }, out _),
            "空依赖集合必须判定为可解析");
    }

    private static Dictionary<string, int[]> ResolveAll(
        IReadOnlyDictionary<string, IList<string>> dependenciesByContent,
        Dictionary<string, int> indexByFileName)
    {
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var pair in dependenciesByContent)
        {
            var dependencyNames = new List<string>(pair.Value);
            Check.True(
                ContentDependencyIndexResolver.TryResolveDependencyIndices(
                    pair.Key, dependencyNames, indexByFileName, out int[] indices, out string failureReason),
                $"内容 '{pair.Key}' 的依赖下标必须可换算（原因={failureReason}）");
            result[pair.Key] = indices;
        }

        return result;
    }

    /// <summary>把合并结果转成合并入口的输入形状，便于统一走同一套下标换算。</summary>
    private static Dictionary<string, IList<string>> ToFacts(Dictionary<string, List<string>> merged)
    {
        var facts = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
        foreach (var pair in merged)
            facts[pair.Key] = new List<string>(pair.Value);
        return facts;
    }

    private static Dictionary<string, IList<string>> Facts(params (string Content, string[] Dependencies)[] entries)
    {
        var map = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
            map[entries[i].Content] = new List<string>(entries[i].Dependencies ?? Array.Empty<string>());
        return map;
    }
}

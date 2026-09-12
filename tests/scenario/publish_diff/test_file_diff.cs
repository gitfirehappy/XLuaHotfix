using System.Collections.Generic;

/// <summary>
/// 无状态 <see cref="FileDiff"/> 的分类契约：按名称配对，按 Hash/CRC/Size 判定未变或修改。
/// </summary>
internal static class FileDiffTests
{
    public static void Run()
    {
        ClassifiesAddedModifiedUnchangedRemoved();
        TreatsMissingSideAsEmpty();
        MatchesNamesCaseSensitively();
        CollectsChangedNames();
        IndexesByHashForReuse();
    }

    private static void ClassifiesAddedModifiedUnchangedRemoved()
    {
        var previous = new List<FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2"),
            FileFixtures.Fake("c", "h3")
        };
        var current = new List<FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2-changed"),
            FileFixtures.Fake("d", "h4")
        };

        FileDiff diff = FileDiff.Compute(previous, current);
        Check.Equal(1, diff.Added.Count, "新增应只有 d");
        Check.Equal("d", diff.Added[0].Name, "新增名称应为 d");
        Check.Equal(1, diff.Modified.Count, "修改应只有 b");
        Check.Equal("b", diff.Modified[0].Name, "修改名称应为 b");
        Check.Equal(1, diff.Unchanged.Count, "未变应只有 a");
        Check.Equal("a", diff.Unchanged[0].Name, "未变名称应为 a");
        Check.Equal(1, diff.Removed.Count, "移除应只有 c");
        Check.Equal("c", diff.Removed[0], "移除名称应为 c");
        Check.True(diff.HasContentChange, "存在新增或修改时应报告内容变化");
        Check.True(!diff.IsEmpty, "有差异时 IsEmpty 必须为 false");
    }

    private static void TreatsMissingSideAsEmpty()
    {
        var current = new List<FileDigest> { FileFixtures.Fake("a", "h1") };
        FileDiff firstDiff = FileDiff.Compute(null, current);
        Check.Equal(1, firstDiff.Added.Count, "没有历史集合时全部算新增");
        Check.Equal(0, firstDiff.Removed.Count, "没有历史集合时不应有移除");

        FileDiff emptyDiff = FileDiff.Compute(current, null);
        Check.Equal(1, emptyDiff.Removed.Count, "目标集合为空时全部算移除");
        Check.Equal(0, emptyDiff.Added.Count, "目标集合为空时不应有新增");

        FileDiff bothEmpty = FileDiff.Compute(new List<FileDigest>(), new List<FileDigest>());
        Check.True(bothEmpty.IsEmpty, "两侧都为空时应报告无差异");
    }

    private static void MatchesNamesCaseSensitively()
    {
        var previous = new List<FileDigest> { FileFixtures.Fake("Bundle", "h1") };
        var current = new List<FileDigest> { FileFixtures.Fake("bundle", "h1") };

        FileDiff diff = FileDiff.Compute(previous, current);
        Check.Equal(1, diff.Added.Count, "大小写不同的名称不得配对为同一文件");
        Check.Equal(1, diff.Removed.Count, "大小写不同的名称应各自出现在新增与移除");
    }

    private static void CollectsChangedNames()
    {
        var previous = new List<FileDigest> { FileFixtures.Fake("a", "h1"), FileFixtures.Fake("b", "h2") };
        var current = new List<FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2-new"),
            FileFixtures.Fake("c", "h3")
        };

        HashSet<string> changed = FileDiff.Compute(previous, current).CollectChangedNames();
        Check.Equal(2, changed.Count, "变化名称应包含新增与修改");
        Check.True(changed.Contains("b"), "修改文件应进入变化集合");
        Check.True(changed.Contains("c"), "新增文件应进入变化集合");
        Check.True(!changed.Contains("a"), "未变文件不得进入变化集合");
    }

    private static void IndexesByHashForReuse()
    {
        var files = new List<FileDigest>
        {
            FileFixtures.Fake("bundles/a", "same-hash", 7, 100),
            FileFixtures.Fake("bundles/b", "same-hash", 7, 100),
            FileFixtures.Fake("bundles/c", "other-hash", 8, 200)
        };

        Dictionary<string, FileDigest> byHash = FileDiff.IndexByHash(files);
        Check.Equal(2, byHash.Count, "相同 Hash 与大小的文件应合并为一个复用键");
        Check.True(byHash.ContainsKey(FileDiff.HashKey("same-hash", 100)), "Hash 复用键应包含 Hash 与大小");
        Check.Equal("bundles/a", byHash[FileDiff.HashKey("same-hash", 100)].Name, "同一内容应保留首个出现的名称");
    }
}

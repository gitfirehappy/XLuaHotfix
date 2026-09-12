using System.Collections.Generic;

internal static class FileHelperComparisonTests
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
        var previous = new List<FileHelper.FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2"),
            FileFixtures.Fake("c", "h3")
        };
        var current = new List<FileHelper.FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2-changed"),
            FileFixtures.Fake("d", "h4")
        };

        FileHelper.ComputeDiff(previous, current,
            out List<FileHelper.FileDigest> added,
            out List<FileHelper.FileDigest> modified,
            out List<FileHelper.FileDigest> unchanged,
            out List<string> removed);
        Check.Equal(1, added.Count, "新增应只有 d");
        Check.Equal("d", added[0].Name, "新增名称应为 d");
        Check.Equal(1, modified.Count, "修改应只有 b");
        Check.Equal("b", modified[0].Name, "修改名称应为 b");
        Check.Equal(1, unchanged.Count, "未变应只有 a");
        Check.Equal("a", unchanged[0].Name, "未变名称应为 a");
        Check.Equal(1, removed.Count, "移除应只有 c");
        Check.Equal("c", removed[0], "移除名称应为 c");
    }

    private static void TreatsMissingSideAsEmpty()
    {
        var current = new List<FileHelper.FileDigest> { FileFixtures.Fake("a", "h1") };
        FileHelper.ComputeDiff(null, current, out List<FileHelper.FileDigest> added, out _, out _, out List<string> removed);
        Check.Equal(1, added.Count, "没有历史集合时全部算新增");
        Check.Equal(0, removed.Count, "没有历史集合时不应有移除");

        FileHelper.ComputeDiff(current, null, out List<FileHelper.FileDigest> emptyAdded, out _, out _, out List<string> emptyRemoved);
        Check.Equal(1, emptyRemoved.Count, "目标集合为空时全部算移除");
        Check.Equal(0, emptyAdded.Count, "目标集合为空时不应有新增");

        FileHelper.ComputeDiff(new List<FileHelper.FileDigest>(), new List<FileHelper.FileDigest>(),
            out List<FileHelper.FileDigest> bothAdded, out _, out _, out List<string> bothRemoved);
        Check.Equal(0, bothAdded.Count + bothRemoved.Count, "两侧都为空时不应有差异");
    }

    private static void MatchesNamesCaseSensitively()
    {
        var previous = new List<FileHelper.FileDigest> { FileFixtures.Fake("Bundle", "h1") };
        var current = new List<FileHelper.FileDigest> { FileFixtures.Fake("bundle", "h1") };

        FileHelper.ComputeDiff(previous, current, out List<FileHelper.FileDigest> added, out _, out _, out List<string> removed);
        Check.Equal(1, added.Count, "大小写不同的名称不得配对为同一文件");
        Check.Equal(1, removed.Count, "大小写不同的名称应各自出现在新增与移除");
    }

    private static void CollectsChangedNames()
    {
        var previous = new List<FileHelper.FileDigest> { FileFixtures.Fake("a", "h1"), FileFixtures.Fake("b", "h2") };
        var current = new List<FileHelper.FileDigest>
        {
            FileFixtures.Fake("a", "h1"),
            FileFixtures.Fake("b", "h2-new"),
            FileFixtures.Fake("c", "h3")
        };

        FileHelper.ComputeDiff(previous, current, out List<FileHelper.FileDigest> added, out List<FileHelper.FileDigest> modified, out _, out _);
        HashSet<string> changed = FileHelper.CollectChangedNames(added, modified);
        Check.Equal(2, changed.Count, "变化名称应包含新增与修改");
        Check.True(changed.Contains("b"), "修改文件应进入变化集合");
        Check.True(changed.Contains("c"), "新增文件应进入变化集合");
        Check.True(!changed.Contains("a"), "未变文件不得进入变化集合");
    }

    private static void IndexesByHashForReuse()
    {
        var files = new List<FileHelper.FileDigest>
        {
            FileFixtures.Fake("bundles/a", "same-hash", 7, 100),
            FileFixtures.Fake("bundles/b", "same-hash", 7, 100),
            FileFixtures.Fake("bundles/c", "other-hash", 8, 200)
        };

        Dictionary<string, FileHelper.FileDigest> byHash = FileHelper.IndexByHash(files);
        Check.Equal(2, byHash.Count, "相同 Hash 与大小的文件应合并为一个复用键");
        Check.True(byHash.ContainsKey(FileHelper.HashKey("same-hash", 100)), "Hash 复用键应包含 Hash 与大小");
        Check.Equal("bundles/a", byHash[FileHelper.HashKey("same-hash", 100)].Name, "同一内容应保留首个出现的名称");
    }
}

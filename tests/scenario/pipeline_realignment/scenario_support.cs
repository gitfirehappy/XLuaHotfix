using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// 资源管线契约门禁入口。
/// 各门禁验证一个独立的结构或行为约束，失败时阻止场景通过，成功后作为回归保护。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var gates = new (string Name, Action Run)[]
        {
            ("HandleTokenBehavior", HandleTokenBehaviorTests.Run),
            ("T1CollectionContract", CollectionContractTests.Run),
            ("T2RawFileShareContract", RawFileShareContractTests.Run),
            ("T4ManifestContract", ManifestContractTests.Run),
            ("T6RunnerContract", RunnerContractTests.Run),
            ("T7BaselineExitContract", BaselineExitContractTests.Run),
            ("T8HotfixSwitchContract", HotfixSwitchContractTests.Run)
        };

        int failures = 0;
        for (int i = 0; i < gates.Length; i++)
        {
            try
            {
                gates[i].Run();
                Console.WriteLine($"PASS {gates[i].Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {gates[i].Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"pipeline realignment gates: {gates.Length - failures}/{gates.Length} passed");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>
/// 仓库源码读取入口。根目录从 <see cref="AppContext.BaseDirectory"/> 向上查找 Assets 得到，
/// 因此场景可以从任意工作目录执行。
/// </summary>
internal static class RepoSource
{
    public static readonly string Root = FindRoot();

    public static string Absolute(string relativePath) =>
        Path.Combine(Root, Normalize(relativePath));

    public static bool FileExists(string relativePath) => File.Exists(Absolute(relativePath));

    public static bool DirectoryExists(string relativePath) => Directory.Exists(Absolute(relativePath));

    /// <summary>读取文件原文；文件不存在时抛出，避免静默放过被删除的受管文件。</summary>
    public static string Read(string relativePath)
    {
        string path = Absolute(relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"受管源码文件不存在: {relativePath}", path);
        return File.ReadAllText(path);
    }

    /// <summary>读取文件并剥离注释与字符串字面量，用于符号存在性判断。</summary>
    public static string ReadCode(string relativePath) => Sanitize(Read(relativePath));

    /// <summary>递归枚举目录下的全部 .cs 文件，返回仓库相对路径（正斜杠）。</summary>
    public static List<string> CsFiles(string relativeDir)
    {
        var result = new List<string>();
        string root = Absolute(relativeDir);
        if (!Directory.Exists(root))
            return result;

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            result.Add(ToRelative(file));
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>递归拼接目录下全部 .cs 的净化源码，用于“整棵树都不得出现某符号”的判断。</summary>
    public static string CodeOfTree(string relativeDir)
    {
        var builder = new System.Text.StringBuilder();
        foreach (string file in CsFiles(relativeDir))
        {
            builder.Append('\n');
            builder.Append(Sanitize(File.ReadAllText(Absolute(file))));
        }
        return builder.ToString();
    }

    /// <summary>返回目录树中命中指定符号的文件列表，供失败信息定位。</summary>
    public static List<string> FilesContaining(string relativeDir, string token)
    {
        var hits = new List<string>();
        foreach (string file in CsFiles(relativeDir))
        {
            if (HasToken(Sanitize(File.ReadAllText(Absolute(file))), token))
                hits.Add(file);
        }
        return hits;
    }

    /// <summary>判断净化源码中是否出现完整单词形式的符号。</summary>
    public static bool HasToken(string code, string token) =>
        Regex.IsMatch(code, @"\b" + Regex.Escape(token) + @"\b");

    public static string ToRelative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// 剥离注释与字符串字面量。注释里提到旧符号不算实现引用，
    /// 但字符串字面量可能承载序列化键或资源路径，因此替换为空的引号对以保留结构。
    /// </summary>
    public static string Sanitize(string source)
    {
        source = Regex.Replace(source, @"//[^\n]*", " ");
        source = Regex.Replace(source, @"#(?:region|endregion)[^\n]*", " ");
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, "\"(?:[^\"\\\\]|\\\\.)*?\"", "\"\"");
        return source;
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with Assets directory was not found.");
    }
}

/// <summary>
/// 子契约独立执行并单独报告结果，最后统一汇总结论。
/// </summary>
internal static class GateChecks
{
    public static void RunAll(params (string Name, Action Run)[] checks)
    {
        var failures = new List<string>();
        for (int i = 0; i < checks.Length; i++)
        {
            try
            {
                checks[i].Run();
                Console.WriteLine($"  GREEN {checks[i].Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{checks[i].Name}: {ex.Message}");
                Console.WriteLine($"  RED   {checks[i].Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count}/{checks.Length} 个子契约未满足 -> {string.Join(" | ", failures)}");
    }
}

/// <summary>门禁断言。失败时抛出携带定位信息的异常，由入口汇总。</summary>
internal static class GateAssert{
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

    public static void Contains(string source, string value, string message) =>
        True(source.IndexOf(value, StringComparison.Ordinal) >= 0, message);

    public static void NotContains(string source, string value, string message) =>
        True(source.IndexOf(value, StringComparison.Ordinal) < 0, message);

    /// <summary>断言源码中存在完整单词形式的类型或成员名。</summary>
    public static void HasSymbol(string code, string symbol, string message) =>
        True(RepoSource.HasToken(code, symbol), message);

    /// <summary>断言源码中不存在完整单词形式的类型或成员名。</summary>
    public static void NoSymbol(string code, string symbol, string message) =>
        False(RepoSource.HasToken(code, symbol), message);

    /// <summary>断言受管文件存在；缺失说明目标结构尚未落地。</summary>
    public static void FileExists(string relativePath, string message) =>
        True(RepoSource.FileExists(relativePath), $"{message}（缺失文件: {relativePath}）");

    /// <summary>断言受管文件已删除。</summary>
    public static void FileMissing(string relativePath, string message) =>
        False(RepoSource.FileExists(relativePath), $"{message}（仍存在: {relativePath}）");

    /// <summary>断言目录已不存在。</summary>
    public static void DirectoryMissing(string relativePath, string message) =>
        False(RepoSource.DirectoryExists(relativePath), $"{message}（仍存在目录: {relativePath}）");

    /// <summary>断言目录树内不出现指定符号，失败信息列出全部命中文件。</summary>
    public static void TreeHasNoSymbol(string relativeDir, string symbol, string message)
    {
        List<string> hits = RepoSource.FilesContaining(relativeDir, symbol);
        True(hits.Count == 0, $"{message}（符号 {symbol} 命中: {string.Join(", ", hits)}）");
    }

    /// <summary>断言目录树内出现指定符号。</summary>
    public static void TreeHasSymbol(string relativeDir, string symbol, string message)
    {
        List<string> hits = RepoSource.FilesContaining(relativeDir, symbol);
        True(hits.Count > 0, $"{message}（符号 {symbol} 在 {relativeDir} 未找到）");
    }
}

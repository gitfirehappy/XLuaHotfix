using System;
using System.Collections.Generic;
using System.IO;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("UpperPackageBoundary", UpperPackageBoundaryTests.Run),
            ("BackendLabelPanels", BackendLabelPanelTests.Run),
            ("LabelParityRetirement", LabelParityRetirementTests.Run),
            ("StaleSerializedDependency", StaleSerializedDependencyTests.Run),
            ("ExportBoundary", ExportBoundaryTests.Run),
            ("XLuaFrameworkBoundary", XLuaFrameworkBoundaryTests.Run)
        };

        int failures = 0;
        for (int i = 0; i < tests.Length; i++)
        {
            try
            {
                tests[i].Run();
                Console.WriteLine($"PASS {tests[i].Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {tests[i].Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"S3 scenarios: {tests.Length - failures}/{tests.Length} passed");
        return failures == 0 ? 0 : 1;
    }
}

internal static class RepoSource
{
    public static readonly string Root = FindRoot();

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, Normalize(relativePath)));

    public static string[] ReadLines(string relativePath) =>
        File.ReadAllLines(Path.Combine(Root, Normalize(relativePath)));

    public static bool Exists(string relativePath) =>
        File.Exists(Path.Combine(Root, Normalize(relativePath)));

    public static bool DirectoryExists(string relativePath) =>
        Directory.Exists(Path.Combine(Root, Normalize(relativePath)));

    public static IEnumerable<string> EnumerateFiles(string relativePath, string searchPattern)
    {
        string root = Path.Combine(Root, Normalize(relativePath));
        if (!Directory.Exists(root)) yield break;

        foreach (string path in Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories))
            yield return path;
    }

    public static string ToRelative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

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

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}

internal static class RepoAssert
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Contains(string source, string value, string message) =>
        True(source.Contains(value, StringComparison.Ordinal), message);

    public static void NotContains(string source, string value, string message) =>
        True(!source.Contains(value, StringComparison.Ordinal), message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    public static void SetEqual(
        IReadOnlyDictionary<string, HashSet<string>> expected,
        IReadOnlyDictionary<string, HashSet<string>> actual,
        string message)
    {
        var expectedKeys = new HashSet<string>(expected.Keys, StringComparer.Ordinal);
        var actualKeys = new HashSet<string>(actual.Keys, StringComparer.Ordinal);
        if (!expectedKeys.SetEquals(actualKeys))
        {
            throw new InvalidOperationException(
                $"{message}: labels differ. expected=[{string.Join(",", expectedKeys)}], " +
                $"actual=[{string.Join(",", actualKeys)}]");
        }

        foreach (string label in expectedKeys)
        {
            if (!expected[label].SetEquals(actual[label]))
            {
                throw new InvalidOperationException(
                    $"{message}: label {label} differs. expected=[{string.Join(",", expected[label])}], " +
                    $"actual=[{string.Join(",", actual[label])}]");
            }
        }
    }
}

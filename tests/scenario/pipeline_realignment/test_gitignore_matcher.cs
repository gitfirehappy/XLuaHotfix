using System;
using System.Collections.Generic;

internal static class GitIgnoreMatcherTests
{
    public static void Run()
    {
        Assert(GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "Assets/UI/**" }), "anchored subtree matches");
        Assert(GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "*.png" }), "basename pattern matches");
        Assert(!GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "*.png", "!Assets/UI/icon.png" }), "negated rule restores path");
        Assert(GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "*.png", "!Assets/UI/icon.png", "Assets/UI/icon.png" }), "last matching rule wins");
        Assert(!GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "# comment", "*.json" }), "comments are ignored");
        Assert(GitIgnoreMatcher.Evaluate("Assets/Config/sub/file.json", new List<string> { "Config/" }), "directory pattern matches descendants");
        Assert(GitIgnoreMatcher.Evaluate("Assets/UI/icon.png", new List<string> { "/UI/icon.png" }), "root anchored pattern matches");
        Assert(!GitIgnoreMatcher.Evaluate("Assets/Other/UI/icon.png", new List<string> { "/UI/icon.png" }), "root anchored pattern rejects nested path");
        Console.WriteLine("  GREEN GitIgnoreMatcherRules (9/9)");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("GitIgnoreMatcher failed: " + name);
    }
}

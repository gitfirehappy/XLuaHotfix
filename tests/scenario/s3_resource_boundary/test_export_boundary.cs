using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// AA/AB 独立导出边界门禁（R 系列验收）。
/// 规则：Shared 不得引用任何后端声明类型；AA 不得引用 AB 声明类型；AB 不得引用 AA 声明类型。
/// 类型集合从源码声明自维护收集，新增后端类型自动纳管。
/// </summary>
internal static class ExportBoundaryTests
{
    private const string AaRoot = "Assets/FYAsset/Scripts/AA";
    private const string AbRoot = "Assets/FYAsset/Scripts/AB";
    private const string SharedRoot = "Assets/FYAsset/Scripts/Shared";
    private const string CompatRoot = "Assets/FYAsset/Scripts/Compat";

    // 只纳管跨文件可见的类型（public/internal）；私有嵌套类型不可跨树引用，收集会造成同名误判。
    private static readonly Regex TypeDecl = new(
        @"\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+|new\s+)*?(?:class|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    // 本文件自声明排除用更宽的模式（私有声明同名也不是引用）。
    private static readonly Regex AnyTypeDecl = new(
        @"\b(?:class|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    public static void Run()
    {
        var aaTypes = CollectDeclaredTypeNames(AaRoot);
        var abTypes = CollectDeclaredTypeNames(AbRoot);
        Console.WriteLine($"export boundary: AA types={aaTypes.Count}, AB types={abTypes.Count}");

        VerifySharedHasNoBackendTypes(SharedRoot, aaTypes, abTypes);
        VerifyNoCrossReferences(AaRoot, abTypes, "AB", stripSelfPrefix: "AA");
        VerifyNoCrossReferences(AbRoot, aaTypes, "AA", stripSelfPrefix: "AB");

        // 逆向约束（review 补盲）：AA/AB 与 Shared 都不得反向引用 Compat 胶水层，
        // 否则导出集仍需携带 Compat 才能编译。
        var compatTypes = CollectDeclaredTypeNames(CompatRoot);
        Console.WriteLine($"export boundary: Compat types={compatTypes.Count}");
        VerifyNoCompatReferences(AaRoot, compatTypes, "AA");
        VerifyNoCompatReferences(AbRoot, compatTypes, "AB");
        VerifyNoCompatReferences(SharedRoot, compatTypes, "Shared");
        VerifyLuaIndexOwnership();
        VerifyNonEditorSharedBuildDoesNotReferenceEditorOnlyTypes();
        VerifySharedDoesNotOwnBackendSelection();
        VerifyBackendModeRejectsUnknown();
    }

    private static void VerifyBackendModeRejectsUnknown()
    {
        bool threw = false;
        try
        {
            BackendModeNames.FromBackendMode((BackendMode)99);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        RepoAssert.True(threw, "unknown BackendMode must fail instead of defaulting to AA");
    }

    private static void VerifySharedDoesNotOwnBackendSelection()
    {
        foreach (string file in EnumerateSources(SharedRoot))
        {
            string norm = file.Replace((char)92, '/');
            RepoAssert.False(norm.EndsWith("/Build/BackendMode.cs", StringComparison.OrdinalIgnoreCase),
                "BackendMode must live in Compat, not Shared: " + file);

            string source = Sanitize(File.ReadAllText(file));
            RepoAssert.NotContains(source, "UseABBackend",
                "Shared must not contain the legacy backend selector: " + file);
            RepoAssert.NotContains(source, "BackendModeNames",
                "Shared must not reference Compat BackendModeNames: " + file);
            RepoAssert.False(Regex.IsMatch(source, @"\\bBackendMode\\s+[A-Za-z_]"),
                "Shared must not use the BackendMode enum type: " + file);
        }
    }
    private static void VerifyNonEditorSharedBuildDoesNotReferenceEditorOnlyTypes()
    {
        foreach (string file in EnumerateSources(SharedRoot + "/Build"))
        {
            string norm = file.Replace((char)92, '/');
            if (norm.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            string source = Sanitize(File.ReadAllText(file));
            RepoAssert.False(Regex.IsMatch(source, @"\bBuildPackageRequest\s+\w+"),
                "non-Editor Shared/Build file cannot use Editor-only type BuildPackageRequest: " + file);
            RepoAssert.False(Regex.IsMatch(source, @"\bBuildType\.(Full|Hotfix|Standalone)\b"),
                "non-Editor Shared/Build file cannot use Editor-only enum BuildType: " + file);
        }
    }

    private static void VerifyLuaIndexOwnership()
    {
        string[] roots = { AaRoot, AbRoot, SharedRoot };
        string[] forbiddenTerms =
        {
            "LuaScriptsIndex",
            "LuaScriptContainer",
            "LuaScriptsIndexBuilder",
            "LuaScriptsIndexBuildException"
        };

        for (int r = 0; r < roots.Length; r++)
        {
            foreach (string file in EnumerateSources(roots[r]))
            {
                string source = Sanitize(File.ReadAllText(file));
                for (int t = 0; t < forbiddenTerms.Length; t++)
                    RepoAssert.NotContains(source, forbiddenTerms[t],
                        $"{roots[r]} must not own LuaIndex semantics: {file}");
            }
        }

        const string compatTask =
            "Assets/FYAsset/Scripts/Compat/Editor/Build/LuaScriptsIndexBuildTask.cs";
        RepoAssert.True(RepoSource.Exists(compatTask),
            "LuaScriptsIndexBuildTask must be implemented in Compat/Editor/Build");

        string aaConfig = RepoSource.Read("Assets/Build/AABuildPipelineConfig.asset");
        string abConfig = RepoSource.Read("Assets/Build/BuildPipelineConfig.asset");
        RepoAssert.Contains(aaConfig, "TaskName: LuaScriptsIndexBuildTask",
            "AA config must inject LuaScriptsIndexBuildTask");
        RepoAssert.Contains(abConfig, "TaskName: LuaScriptsIndexBuildTask",
            "AB config must inject LuaScriptsIndexBuildTask");
        RepoAssert.True(
            IndexOfTaskEntry(aaConfig, "LuaScriptsIndexBuildTask")
            < IndexOfTaskEntry(aaConfig, "TaskScanAAHotfixDiff"),
            "AA LuaScriptsIndexBuildTask must run before Addressables source scan/build");
        RepoAssert.True(
            IndexOfTaskEntry(abConfig, "LuaScriptsIndexBuildTask")
            < IndexOfTaskEntry(abConfig, "TaskBuildBundles"),
            "AB LuaScriptsIndexBuildTask must run before TaskBuildBundles");

        string aaBackbone = RepoSource.Read(
            "Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/AAPipelineBackbone.cs");
        string abBackbone = RepoSource.Read(
            "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/ABPipelineBackbone.cs");
        RepoAssert.NotContains(aaBackbone, "LuaScriptsIndexBuildTask",
            "AA backbone must not absorb the Compat lua task");
        RepoAssert.NotContains(abBackbone, "LuaScriptsIndexBuildTask",
            "AB backbone must not absorb the Compat lua task");
    }

    private static int IndexOfTaskEntry(string yaml, string taskName)
    {
        int tasks = yaml.IndexOf("\n  Tasks:", StringComparison.Ordinal);
        RepoAssert.True(tasks >= 0, "pipeline config missing Tasks list");
        int index = yaml.IndexOf("- TaskName: " + taskName, tasks, StringComparison.Ordinal);
        RepoAssert.True(index >= 0, "pipeline config missing TaskName " + taskName);
        return index;
    }

    // 纯词表文件：BackendMode 枚举成员名与后端类型同名是刻意的协议词汇（中性标准 B 允许）。
    private static readonly HashSet<string> VocabularyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Build/BackendMode.cs",
    };

    private static void VerifySharedHasNoBackendTypes(string root, HashSet<string> aaTypes, HashSet<string> abTypes)
    {
        foreach (string file in EnumerateSources(root))
        {
            if (file.Contains("/Compat/", StringComparison.Ordinal))
                continue;
            if (IsVocabularyFile(file))
                continue;
            string source = Sanitize(File.ReadAllText(file));
            // 排除本文件自声明的类型名：同名局部类型属声明，不是对后端类型的引用。
            var localDecls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in AnyTypeDecl.Matches(File.ReadAllText(file)))
                localDecls.Add(m.Groups[1].Value);
            foreach (string type in aaTypes)
                if (!localDecls.Contains(type))
                    RepoAssert.False(ContainsToken(source, type),
                        $"Shared must not reference AA type {type}: {file}");
            foreach (string type in abTypes)
                if (!localDecls.Contains(type))
                    RepoAssert.False(ContainsToken(source, type),
                        $"Shared must not reference AB type {type}: {file}");
        }
    }

    private static void VerifyNoCompatReferences(string root, HashSet<string> compatTypes, string sideName)
    {
        foreach (string file in EnumerateSources(root))
        {
            if (IsVocabularyFile(file))
                continue;
            string source = Sanitize(File.ReadAllText(file));
            var localDecls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in AnyTypeDecl.Matches(File.ReadAllText(file)))
                localDecls.Add(m.Groups[1].Value);
            foreach (string type in compatTypes)
                if (!localDecls.Contains(type))
                    RepoAssert.False(ContainsToken(source, type),
                        $"{sideName} must not reference Compat type {type}: {file}");
        }
    }

    private static bool IsVocabularyFile(string file)
    {
        string norm = file.Replace((char)92, '/');
        foreach (string suffix in VocabularyFiles)
            if (norm.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void VerifyNoCrossReferences(string root, HashSet<string> foreignTypes, string foreignSide, string stripSelfPrefix)
    {
        foreach (string file in EnumerateSources(root))
        {
            string source = Sanitize(File.ReadAllText(file));
            var localDecls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in AnyTypeDecl.Matches(File.ReadAllText(file)))
                localDecls.Add(m.Groups[1].Value);
            foreach (string type in foreignTypes)
                if (!localDecls.Contains(type))
                    RepoAssert.False(ContainsToken(source, type),
                        $"{stripSelfPrefix} must not reference {foreignSide} type {type}: {file}");
        }
    }

    private static HashSet<string> CollectDeclaredTypeNames(string root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in EnumerateSources(root))
        {
            foreach (Match match in TypeDecl.Matches(File.ReadAllText(file)))
            {
                string name = match.Groups[1].Value;
                if (name.Length > 1)
                    result.Add(name);
            }
        }
        return result;
    }

    /// <summary>剥离注释/字符串字面量与 BackendMode 词表访问，减少误报。</summary>
    private static string Sanitize(string source)
    {
        source = Regex.Replace(source, @"//[^\n]*", " ");
        source = Regex.Replace(source, @"#(?:region|endregion)[^\n]*", " ");
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, "\\\"(?:[^\\\"\\\\]|\\\\.)*?\\\"", "\"\"");
        // Unity Addressables 嵌套枚举 BundledAssetGroupSchema.BundlePackingMode 与 AB 顶层枚举同名：限定访问不是引用 AB 类型。
        source = source.Replace("BundledAssetGroupSchema.BundlePackingMode", " ");
        source = source.Replace("BackendMode.ABManifest", " ").Replace("BackendMode.AA", " ");
        source = source.Replace("BackendModeNames.AB", " ").Replace("BackendModeNames.AA", " ");
        source = source.Replace(".BackendMode", ".");
        source = Regex.Replace(source, @"\bBackendMode(?=\s*[=;,.])", " ");
        return source;
    }

    private static bool ContainsToken(string source, string token)
    {
        return Regex.IsMatch(source, @"\b" + Regex.Escape(token) + @"\b");
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        string abs = Path.GetFullPath(root);
        if (!Directory.Exists(abs))
            yield break;
        foreach (string file in Directory.EnumerateFiles(abs, "*.cs", SearchOption.AllDirectories))
            yield return file.Replace((char)92, '/');
    }
}

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

    // 字段/常量/属性/方法声明名，用于把「与编辑器专用类型同名的成员」判为声明而不是引用。
    private static readonly Regex MemberDecl = new(
        @"\b(?:const\s+)?(?:string|int|uint|long|bool|float|double|char|object|var)\s+(\w+)\s*(?:=|;|\()",
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
        VerifyRuntimeFilesDoNotReferenceEditorOnlyTypes();
        VerifySharedDoesNotOwnBackendSelection();
        VerifyBackendModeRejectsUnknown();
    }

    /// <summary>
    /// 运行时可见文件不得引用「只在编辑器专用文件里声明」的类型。
    /// 编辑器专用文件 = 路径含 /Editor/，或整个文件被 #if UNITY_EDITOR 包裹；
    /// 它们不进入 Player 程序集。dotnet build 与 Unity 编辑器编译都定义了 UNITY_EDITOR，
    /// 因此这类缺陷只会在 Player 构建时暴露，必须由静态规则提前拦住。
    /// </summary>
    private static void VerifyRuntimeFilesDoNotReferenceEditorOnlyTypes()
    {
        const string fyRoot = "Assets/FYAsset/Scripts";
        var files = new List<string>(EnumerateSources(fyRoot));
        var editorOnlyDecls = new HashSet<string>(StringComparer.Ordinal);
        var runtimeDecls = new HashSet<string>(StringComparer.Ordinal);
        var editorOnlyByFile = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            bool editorOnly = IsEditorOnlyFile(file, text);
            editorOnlyByFile[file] = editorOnly;
            HashSet<string> target = editorOnly ? editorOnlyDecls : runtimeDecls;
            foreach (Match m in AnyTypeDecl.Matches(text))
                target.Add(m.Groups[1].Value);
        }

        // 同名类型只要在运行时侧也有声明，就不属于「编辑器专用类型」。
        editorOnlyDecls.ExceptWith(runtimeDecls);

        foreach (string file in files)
        {
            if (editorOnlyByFile[file])
                continue;

            string text = File.ReadAllText(file);
            string code = Sanitize(StripEditorGuardedRegions(text));
            var localDecls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in AnyTypeDecl.Matches(text))
                localDecls.Add(m.Groups[1].Value);
            // 字段/常量/成员名与某个编辑器专用类型同名时属声明而非引用（例如上下文键常量）。
            foreach (Match m in MemberDecl.Matches(text))
                localDecls.Add(m.Groups[1].Value);

            foreach (string type in editorOnlyDecls)
            {
                if (localDecls.Contains(type))
                    continue;
                RepoAssert.False(IsUnqualifiedToken(code, type),
                    $"Player 程序集可见的文件不得引用编辑器专用类型 {type}: {file}");
            }
        }
    }

    /// <summary>
    /// 匹配「未被 . 限定、也不处于成员赋值位置」的类型名。
    /// 成员访问（identity.BuildType）与对象初始化器里的成员名（new X { BuildType = … }）
    /// 都不是类型引用，必须排除，否则会把普通字段访问误判为编辑器专用类型引用。
    /// </summary>
    private static bool IsUnqualifiedToken(string source, string token)
    {
        foreach (Match match in Regex.Matches(source, @"(?<!\.)\b" + Regex.Escape(token) + @"\b"))
        {
            int i = match.Index + match.Length;
            while (i < source.Length && (source[i] == ' ' || source[i] == '\t'))
                i++;

            // 后面紧跟单个 '=' 说明是成员赋值（'==' 才是比较，需保留）。
            if (i < source.Length && source[i] == '='
                && (i + 1 >= source.Length || source[i + 1] != '='))
                continue;

            return true;
        }

        return false;
    }

    private static bool IsEditorOnlyFile(string file, string text)
    {
        string norm = file.Replace((char)92, '/');
        if (norm.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string stripped = Regex.Replace(text, @"//[^\n]*", " ");
        stripped = Regex.Replace(stripped, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        string first = null;
        string last = null;
        foreach (string raw in stripped.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (first == null)
                first = line;
            last = line;
        }

        return first != null
            && first.StartsWith("#if UNITY_EDITOR", StringComparison.Ordinal)
            && string.Equals(last, "#endif", StringComparison.Ordinal);
    }

    /// <summary>移除 #if UNITY_EDITOR 包裹的整段代码（含嵌套），避免编辑器专用分支被判为运行时引用。</summary>
    private static string StripEditorGuardedRegions(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        int depth = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.StartsWith("#if", StringComparison.Ordinal))
            {
                if (depth > 0)
                {
                    depth++;
                }
                else if (line.StartsWith("#if UNITY_EDITOR", StringComparison.Ordinal))
                {
                    depth = 1;
                }
                if (depth > 0)
                {
                    builder.Append('\n');
                    continue;
                }
            }
            else if (line.StartsWith("#endif", StringComparison.Ordinal) && depth > 0)
            {
                depth--;
                builder.Append('\n');
                continue;
            }

            builder.Append(depth > 0 ? "\n" : raw + "\n");
        }

        return builder.ToString();
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
        RepoAssert.Contains(aaConfig, "Tasks:",
            "AA pipeline config must keep the Tasks list that injects project glue tasks");
        RepoAssert.Contains(abConfig, "Tasks:",
            "AB pipeline config must keep the Tasks list that injects project glue tasks");
        RepoAssert.Contains(aaConfig, "TaskName: LuaScriptsIndexBuildTask",
            "AA config must inject LuaScriptsIndexBuildTask");
        RepoAssert.Contains(abConfig, "TaskName: LuaScriptsIndexBuildTask",
            "AB config must inject LuaScriptsIndexBuildTask");

        // T6 起主干阶段由后端 PipelineBackbone 固定定义，配置只能声明自定义 Task 的插入槽位。
        // 因此这里断言两件事的等价物：
        // 1) 配置里的自定义 Task 必须声明合法 Slot；
        // 2) LuaScriptsIndexBuildTask 的插入点不得晚于内容构建阶段（Slot 语义为“插入到该槽主干任务之前”）。
        string aaSlot = ExtractTaskSlot(aaConfig, "LuaScriptsIndexBuildTask");
        string abSlot = ExtractTaskSlot(abConfig, "LuaScriptsIndexBuildTask");
        RepoAssert.True(IndexOfSlot(AaSlotOrder, aaSlot) >= 0,
            $"AA LuaScriptsIndexBuildTask must declare a legal pipeline slot (actual='{aaSlot}')");
        RepoAssert.True(IndexOfSlot(AbSlotOrder, abSlot) >= 0,
            $"AB LuaScriptsIndexBuildTask must declare a legal pipeline slot (actual='{abSlot}')");
        RepoAssert.True(IndexOfSlot(AaSlotOrder, aaSlot) <= IndexOfSlot(AaSlotOrder, "BuildAAContent"),
            "AA LuaScriptsIndexBuildTask must be inserted before the Addressables content build stage");
        RepoAssert.True(IndexOfSlot(AbSlotOrder, abSlot) <= IndexOfSlot(AbSlotOrder, "BuildABContent"),
            "AB LuaScriptsIndexBuildTask must be inserted before the bundle content build stage");

        // 主干顺序不再由配置声明：配置文件里不得再出现任何旧主干 Task 名。
        string[] legacyBackboneNames = { "TaskPrepareContext", "TaskScanAAHotfixDiff", "TaskBuildAAContent", "TaskBuildBundles", "TaskWritePackageIndex" };
        for (int i = 0; i < legacyBackboneNames.Length; i++)
        {
            RepoAssert.NotContains(aaConfig, legacyBackboneNames[i],
                $"AA pipeline config must not declare backbone tasks: {legacyBackboneNames[i]}");
            RepoAssert.NotContains(abConfig, legacyBackboneNames[i],
                $"AB pipeline config must not declare backbone tasks: {legacyBackboneNames[i]}");
        }

        string aaBackbone = RepoSource.Read(
            "Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/AAPipelineBackbone.cs");
        string abBackbone = RepoSource.Read(
            "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/ABPipelineBackbone.cs");
        RepoAssert.NotContains(aaBackbone, "LuaScriptsIndexBuildTask",
            "AA backbone must not absorb the Compat lua task");
        RepoAssert.NotContains(abBackbone, "LuaScriptsIndexBuildTask",
            "AB backbone must not absorb the Compat lua task");
    }

    /// <summary>主干槽位顺序：自定义 Task 的 Slot 必须是其中之一。</summary>
    private static readonly string[] AaSlotOrder =
    {
        "Input", "PrepareAAInput", "BuildAAContent", "GenerateAAManifest", "VerifyAAContent", "ExportAAOutput"
    };

    /// <summary>主干槽位顺序：自定义 Task 的 Slot 必须是其中之一。</summary>
    private static readonly string[] AbSlotOrder =
    {
        "Input", "CollectABAssets", "AnalyzeABDependencies", "BuildABContent", "GenerateABManifest", "VerifyABContent", "ExportABOutput"
    };

    private static int IndexOfSlot(string[] slotOrder, string slot)
    {
        for (int i = 0; i < slotOrder.Length; i++)
            if (string.Equals(slotOrder[i], slot, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>读取配置里某个自定义 Task 声明的 Slot；条目缺失或没有 Slot 行时判定失败。</summary>
    private static string ExtractTaskSlot(string yaml, string taskName)
    {
        int tasks = yaml.IndexOf("\n  Tasks:", StringComparison.Ordinal);
        RepoAssert.True(tasks >= 0, "pipeline config missing Tasks list");
        int index = yaml.IndexOf("- TaskName: " + taskName, tasks, StringComparison.Ordinal);
        RepoAssert.True(index >= 0, "pipeline config missing TaskName " + taskName);

        int nextEntry = yaml.IndexOf("- TaskName: ", index + 1, StringComparison.Ordinal);
        int searchEnd = nextEntry >= 0 ? nextEntry : yaml.Length;
        int slotIndex = yaml.IndexOf("Slot:", index, StringComparison.Ordinal);
        RepoAssert.True(slotIndex >= 0 && slotIndex < searchEnd,
            "pipeline config entry missing Slot: " + taskName);

        int lineEnd = yaml.IndexOf('\n', slotIndex);
        if (lineEnd < 0 || lineEnd > searchEnd)
            lineEnd = searchEnd;
        return yaml.Substring(slotIndex + "Slot:".Length, lineEnd - slotIndex - "Slot:".Length).Trim();
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

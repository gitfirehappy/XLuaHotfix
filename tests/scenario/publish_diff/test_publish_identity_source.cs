/// <summary>
/// 发布身份契约：发布身份只能由正式 Summary 注入，任何发布入口都不得从包目录或目录名推断身份。
/// </summary>
/// <remarks>
/// 覆盖事实：编辑器发布面板与自动化发布路径都必须在构造 PublishRequest 时显式注入 Identity；
/// 包目录只含发布内容，不承载构建事实。
/// </remarks>
internal static class PublishIdentitySourceTests
{
    public static void Run()
    {
        string panel = PublishIdentitySourceReader.ReadCode(
            "Assets/FYAsset/Scripts/Shared/Build/UI/Editor/PublishTargetPanel.cs");
        Check.Contains(
            panel,
            "PublishSourceCatalog.Read(",
            "发布面板必须从正式 Summary 目录器读取候选");
        Check.Contains(
            panel,
            "source.Document",
            "发布面板必须使用已验证候选携带的 Summary 身份");
        Check.Contains(
            panel,
            "Identity = new PackageBuildIdentity",
            "发布面板必须把 Summary 身份注入 PublishRequest");
        Check.True(
            !panel.Contains("TryReadFromPackageDir"),
            "发布面板不得从包目录推断身份");

        Check.True(
            !panel.Contains("BuildPathManager.PackagesDir"),
            "发布面板不得扫描裸 Build_* 目录作为发布源");

        string catalog = PublishIdentitySourceReader.ReadCode(
            "Assets/FYAsset/Scripts/Shared/Build/Summary/Editor/PublishSourceCatalog.cs");
        Check.Contains(catalog, "Directory.GetFiles(", "目录器必须枚举正式 Summary 文件");
        Check.Contains(catalog, "ArtifactRelativePath", "目录器必须按 Summary 制品路径定位包");
        Check.Contains(catalog, "TryReadContentDigests(", "目录器必须校验后端清单");
        Check.Contains(catalog, "TryScanContentDirectory(", "目录器必须核对清单与物理内容");

        string automation = PublishIdentitySourceReader.ReadCode(
            "Assets/FYAsset/Scripts/Tests/Editor/Build/BuildTestState.cs");
        Check.Contains(
            automation,
            "Identity = identity",
            "自动化发布路径必须注入 Summary 解析出的身份");

        // 失败措辞是字符串字面量：这条断言读原文，其余断言读剥离字面量后的代码。
        string request = PublishIdentitySourceReader.ReadRaw(
            "Assets/FYAsset/Scripts/Shared/Build/Publish/PublishRequest.cs");
        Check.Contains(
            request,
            "发布必须由正式 Summary 提供包身份",
            "缺少注入身份时发布必须直接失败，而不是回退到目录推断");
    }
}

/// <summary>读取仓库内源码文件并剥离注释与字符串字面量，用于符号存在性判断。</summary>
internal static class PublishIdentitySourceReader
{
    private static readonly string Root = FindRoot();

    public static string ReadCode(string relativePath) => Sanitize(ReadRaw(relativePath));

    public static string ReadRaw(string relativePath)
    {
        string path = System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException($"受管源码文件不存在: {relativePath}", path);

        return System.IO.File.ReadAllText(path);
    }

    private static string FindRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Assets"))
                && System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("无法定位仓库根目录");
    }

    /// <summary>剥离行注释、块注释与字符串字面量，避免注释或文本字面量造成误判。</summary>
    /// <remarks>字符比较使用码点常量，避免转义层级在工具链中被二次解释。</remarks>
    private static string Sanitize(string source)
    {
        const char Backslash = (char)92;
        const char Quote = (char)34;
        const char LineFeed = (char)10;
        const char Slash = (char)47;
        const char Star = (char)42;

        var builder = new System.Text.StringBuilder(source.Length);
        bool inLine = false, inBlock = false, inString = false, inVerbatim = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : (char)0;
            if (inLine)
            {
                if (c == LineFeed) { inLine = false; builder.Append(c); }
                continue;
            }

            if (inBlock)
            {
                if (c == Star && next == Slash) { inBlock = false; i++; }
                continue;
            }

            if (inString)
            {
                if (c == Backslash) { i++; continue; }
                if (c == Quote) inString = false;
                continue;
            }

            if (inVerbatim)
            {
                if (c == Quote && next == Quote) { i++; continue; }
                if (c == Quote) inVerbatim = false;
                continue;
            }

            if (c == Slash && next == Slash) { inLine = true; continue; }
            if (c == Slash && next == Star) { inBlock = true; i++; continue; }
            if (c == Quote) { inString = true; continue; }
            builder.Append(c);
        }

        return builder.ToString();
    }
}

using System.Text.RegularExpressions;

/// <summary>
/// T3 目标契约：物理文件名由兼容逻辑名的可读段与 12 位身份哈希构成，人工命名风格配置全部退出。
/// </summary>
internal static class PhysicalNamingTests
{
    /// <summary>示例逻辑内容名；段结构与 BundleNameBuilder.Build 的输出一致。</summary>
    private const string SampleContentName = "ui_serialized_texture2d_asset_main~abc12345";

    public static void Run()
    {
        GateChecks.RunAll(
            ("PhysicalNameEndsWith12CharIdentityHash", VerifyPhysicalNameEndsWith12CharIdentityHash),
            ("PhysicalNameCarriesKindSegment", VerifyPhysicalNameCarriesKindSegment),
            ("NamingStyleConfigRetired", VerifyNamingStyleConfigRetired));
    }

    /// <summary>物理名必须以 12 位小写十六进制身份哈希结尾。</summary>
    private static void VerifyPhysicalNameEndsWith12CharIdentityHash()
    {
        // API 契约：BuildPhysicalName(contentName) 保持单参数形态，返回包内物理文件名。
        string physical = BundleNameBuilder.BuildPhysicalName(SampleContentName);
        GateAssert.True(
            Regex.IsMatch(physical ?? string.Empty, "[_-][0-9a-f]{12}$"),
            $"物理文件名必须以 12 位身份哈希结尾（实际='{physical}'）");
    }

    /// <summary>物理名保留打包模式可读段，供人工粗识别。</summary>
    private static void VerifyPhysicalNameCarriesKindSegment()
    {
        string physical = BundleNameBuilder.BuildPhysicalName(SampleContentName);
        GateAssert.True(
            Regex.IsMatch(physical ?? string.Empty, "_(asset|scene|raw|all|labels|unlabeled)[_-]"),
            $"物理文件名必须保留打包模式可读段（实际='{physical}'）");
    }

    /// <summary>BundleFileNameStyle / FileNameStyle 不得再作为配置存在。</summary>
    private static void VerifyNamingStyleConfigRetired()
    {
        GateAssert.NoSymbol(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineConfig.cs"),
            "BundleFileNameStyle",
            "BuildPipelineConfig 不得再声明物理命名风格枚举");
        GateAssert.TreeHasNoSymbol(
            "Assets/FYAsset/Scripts",
            "FileNameStyle",
            "生产代码不得再引用 FileNameStyle");
        GateAssert.NotContains(
            RepoSource.Read("Assets/Build/BuildPipelineConfig.asset"),
            "FileNameStyle",
            "AB 构建配置资产不得再序列化命名风格");
        GateAssert.NotContains(
            RepoSource.Read("Assets/Build/AABuildPipelineConfig.asset"),
            "FileNameStyle",
            "AA 构建配置资产不得再序列化命名风格");
    }
}

using System;

/// <summary>
/// 指纹稳定性：同一输入结果一致；成员顺序、成员集合、依赖哈希、成员间依赖关系、
/// 平台、压缩模式与构建格式变化都必须改变指纹。
/// </summary>
internal static class FingerprintStabilityTests
{
    private const string Platform = "Standalone";
    private const string Compression = "LZ4";
    private const string BuildFormat = "unity=2022.3.62f3;recipe=1;scriptingBackend=Mono";

    public static void Declare(GateRun run)
    {
        run.Check("SameInputProducesSameFingerprint", SameInputProducesSameFingerprint);
        run.Check("MemberOrderDoesNotChangeFingerprint", MemberOrderDoesNotChangeFingerprint);
        run.Check("MemberSetChangeChangesFingerprint", MemberSetChangeChangesFingerprint);
        run.Check("DependencyHashChangeChangesFingerprint", DependencyHashChangeChangesFingerprint);
        run.Check("DependencyEdgeChangeChangesFingerprint", DependencyEdgeChangeChangesFingerprint);
        run.Check("DependencyOrderDoesNotChangeFingerprint", DependencyOrderDoesNotChangeFingerprint);
        run.Check("PlatformChangeChangesFingerprint", PlatformChangeChangesFingerprint);
        run.Check("CompressionChangeChangesFingerprint", CompressionChangeChangesFingerprint);
        run.Check("BuildFormatChangeChangesFingerprint", BuildFormatChangeChangesFingerprint);
        run.Check("ContentNameChangeChangesFingerprint", ContentNameChangeChangesFingerprint);
        run.Check("ContentHashOnlyMemberIsDeterministic", ContentHashOnlyMemberIsDeterministic);
        run.Check("FingerprintIsStableHexDigest", FingerprintIsStableHexDigest);
    }

    private static void SameInputProducesSameFingerprint()
    {
        string first = Compute("ui_serialized_prefab_all", Members());
        string second = Compute("ui_serialized_prefab_all", Members());
        Check.Equal(first, second, "同一输入的两次指纹计算结果必须一致");
    }

    private static void MemberOrderDoesNotChangeFingerprint()
    {
        BundleBuildInputMember[] members = Members();
        var reversed = new BundleBuildInputMember[members.Length];
        for (int i = 0; i < members.Length; i++)
            reversed[i] = members[members.Length - 1 - i];

        Check.Equal(
            Compute("ui_serialized_prefab_all", members),
            Compute("ui_serialized_prefab_all", reversed),
            "成员顺序不同必须得到同一指纹（成员排序必须是确定性的）");
    }

    private static void MemberSetChangeChangesFingerprint()
    {
        BundleBuildInputMember[] members = Members();
        var fewer = new[] { members[0] };
        var more = new[] { members[0], members[1], members[0], new BundleBuildInputMember("guid-cc", "hash-cc", string.Empty, null) };

        Check.False(
            Compute("ui_serialized_prefab_all", members) == Compute("ui_serialized_prefab_all", fewer),
            "成员集合减少必须改变指纹");
        Check.False(
            Compute("ui_serialized_prefab_all", members) == Compute("ui_serialized_prefab_all", more),
            "成员集合增加必须改变指纹");
    }

    private static void DependencyHashChangeChangesFingerprint()
    {
        var changed = new[]
        {
            new BundleBuildInputMember("guid-aa", "hash-aa-2", string.Empty, null),
            new BundleBuildInputMember("guid-bb", "hash-bb", string.Empty, null)
        };

        Check.False(
            Compute("ui_serialized_prefab_all", Members()) == Compute("ui_serialized_prefab_all", changed),
            "成员 Unity 依赖哈希变化必须改变指纹");
    }

    private static void DependencyEdgeChangeChangesFingerprint()
    {
        var linked = new[]
        {
            new BundleBuildInputMember("guid-aa", "hash-aa", string.Empty, new[] { "guid-bb" }),
            new BundleBuildInputMember("guid-bb", "hash-bb", string.Empty, null)
        };

        Check.False(
            Compute("ui_serialized_prefab_all", Members()) == Compute("ui_serialized_prefab_all", linked),
            "成员间依赖关系变化必须改变指纹");
    }

    private static void DependencyOrderDoesNotChangeFingerprint()
    {
        var forward = new[]
        {
            new BundleBuildInputMember("guid-aa", "hash-aa", string.Empty, new[] { "guid-bb", "guid-cc" }),
            new BundleBuildInputMember("guid-bb", "hash-bb", string.Empty, null)
        };
        var backward = new[]
        {
            new BundleBuildInputMember("guid-aa", "hash-aa", string.Empty, new[] { "guid-cc", "guid-bb" }),
            new BundleBuildInputMember("guid-bb", "hash-bb", string.Empty, null)
        };

        Check.Equal(
            Compute("ui_serialized_prefab_all", forward),
            Compute("ui_serialized_prefab_all", backward),
            "成员依赖列表顺序不同必须得到同一指纹");
    }

    private static void PlatformChangeChangesFingerprint()
    {
        Check.False(
            Compute("c", Members(), platform: Platform) == Compute("c", Members(), platform: "Android"),
            "目标平台变化必须改变指纹");
    }

    private static void CompressionChangeChangesFingerprint()
    {
        Check.False(
            Compute("c", Members(), compression: "LZ4") == Compute("c", Members(), compression: "Uncompressed"),
            "压缩模式变化必须改变指纹");
    }

    private static void BuildFormatChangeChangesFingerprint()
    {
        Check.False(
            Compute("c", Members(), buildFormat: BuildFormat)
                == Compute("c", Members(), buildFormat: "unity=2022.3.62f3;recipe=2;scriptingBackend=Mono"),
            "Unity/构建格式变化必须改变指纹");
    }

    private static void ContentNameChangeChangesFingerprint()
    {
        Check.False(
            Compute("content-a", Members()) == Compute("content-b", Members()),
            "内容名变化必须改变指纹（同名内容不得互相命中）");
    }

    private static void ContentHashOnlyMemberIsDeterministic()
    {
        var members = new[]
        {
            new BundleBuildInputMember(string.Empty, string.Empty, "filehash-aa", null)
        };

        string first = Compute("raw_rawfile_asset_all", members);
        string second = Compute("raw_rawfile_asset_all", members);
        Check.Equal(first, second, "无 GUID 成员使用文件内容哈希时指纹必须稳定");

        var changed = new[]
        {
            new BundleBuildInputMember(string.Empty, string.Empty, "filehash-bb", null)
        };
        Check.False(
            first == Compute("raw_rawfile_asset_all", changed),
            "无 GUID 成员的文件内容哈希变化必须改变指纹");
    }

    private static void FingerprintIsStableHexDigest()
    {
        Check.Matches(Compute("c", Members()), "^[0-9a-f]{32}$", "指纹必须是 32 位小写十六进制 MD5");
        Check.Matches(
            BundleBuildInputFingerprint.Compute("c", null, Platform, Compression, BuildFormat),
            "^[0-9a-f]{32}$",
            "空成员集合也必须得到确定的 32 位小写十六进制指纹");
    }

    private static BundleBuildInputMember[] Members()
    {
        return new[]
        {
            new BundleBuildInputMember("guid-aa", "hash-aa", string.Empty, null),
            new BundleBuildInputMember("guid-bb", "hash-bb", string.Empty, null)
        };
    }

    private static string Compute(
        string contentName,
        BundleBuildInputMember[] members,
        string platform = Platform,
        string compression = Compression,
        string buildFormat = BuildFormat)
    {
        return BundleBuildInputFingerprint.Compute(contentName, members, platform, compression, buildFormat);
    }
}

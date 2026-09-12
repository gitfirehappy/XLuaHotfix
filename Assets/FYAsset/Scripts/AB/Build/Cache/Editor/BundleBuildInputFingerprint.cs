using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 内容中单个成员资产的指纹输入。
/// GUID 与 DependencyHash 由 Unity Editor 采集，ContentHash 只用于无法取得 GUID 的成员。
/// </summary>
/// <remarks>
/// MemberDependencies 只记录同一内容内部的成员 GUID，用于表达成员间依赖关系，不包含完整依赖树。
/// </remarks>
public readonly struct BundleBuildInputMember
{
    /// <summary>资产 GUID；非 Unity 资产为空</summary>
    public readonly string Guid;

    /// <summary>AssetDatabase.GetAssetDependencyHash 的结果；不可用时为空</summary>
    public readonly string DependencyHash;

    /// <summary>无 GUID 成员的文件内容 MD5；有 GUID 时为空</summary>
    public readonly string ContentHash;

    /// <summary>该成员在同一内容内引用的其它成员 GUID；可为 null</summary>
    public readonly string[] MemberDependencies;

    public BundleBuildInputMember(string guid, string dependencyHash, string contentHash, string[] memberDependencies)
    {
        Guid = guid ?? string.Empty;
        DependencyHash = dependencyHash ?? string.Empty;
        ContentHash = contentHash ?? string.Empty;
        MemberDependencies = memberDependencies;
    }

    /// <summary>该成员是否携带任何可用于检测内容变化的输入。</summary>
    public bool HasAnyInput =>
        !string.IsNullOrEmpty(Guid)
        || !string.IsNullOrEmpty(DependencyHash)
        || !string.IsNullOrEmpty(ContentHash);
}

/// <summary>
/// 内容构建输入指纹：由成员输入与构建设置折算成稳定的 MD5 文本指纹。
/// 构建前计算，与缓存条目的 InputFingerprint 比较，一致才允许复用旧产物。
/// </summary>
/// <remarks>
/// 输入按规范文本排序后哈希，因此成员顺序不同不影响结果，成员集合、成员依赖哈希、成员间依赖关系、
/// 平台、压缩模式与构建格式任一项变化都会改变指纹。
/// 文本中不写时间戳、绝对路径与 attempt 目录，保证同一输入在不同机器与不同构建目录得到同一指纹。
/// </remarks>
public static class BundleBuildInputFingerprint
{
    /// <summary>
    /// 指纹算法版本；输入组成或产物命名契约变化时必须递增，使旧缓存自然失效。
    /// 版本 2：物理内容文件名改为逻辑名的短哈希派生，旧缓存里的长名产物不再兼容。
    /// </summary>
    /// <summary>指纹格式版本；物理命名规则变化（12 位身份哈希 + 可读段）后已提升。</summary>
    public const int FormatVersion = 3;

    /// <summary>
    /// 计算内容指纹；成员列表为 null 时按空成员集合计算。
    /// </summary>
    /// <param name="contentName">内容逻辑名，参与指纹以免同名内容互相命中。</param>
    public static string Compute(
        string contentName,
        IReadOnlyList<BundleBuildInputMember> members,
        string platform,
        string compression,
        string buildFormat)
    {
        var memberLines = new List<string>();
        if (members != null)
        {
            for (int i = 0; i < members.Count; i++)
                memberLines.Add(DescribeMember(members[i]));
        }

        memberLines.Sort(StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.Append("fingerprint=").Append(FormatVersion).Append('\n');
        builder.Append("content=").Append(contentName ?? string.Empty).Append('\n');
        builder.Append("platform=").Append(platform ?? string.Empty).Append('\n');
        builder.Append("compression=").Append(compression ?? string.Empty).Append('\n');
        builder.Append("buildFormat=").Append(buildFormat ?? string.Empty).Append('\n');
        for (int i = 0; i < memberLines.Count; i++)
            builder.Append(memberLines[i]).Append('\n');

        return HashGenerator.GenerateStringHash(builder.ToString());
    }

    /// <summary>
    /// 构建配方指纹：影响产物字节的配方事实（指纹格式版本、平台、压缩模式、Unity 与构建格式）。
    /// Summary 据此判定历史包与本轮构建是否同一配方；任一项变化即不构成复用候选。
    /// </summary>
    /// <param name="compression">压缩模式标识，与 <see cref="Compute"/> 使用同一套 token。</param>
    public static string ComputeRecipeFingerprint(string platform, string compression, string buildFormat)
    {
        var builder = new StringBuilder();
        builder.Append("recipe=").Append(FormatVersion).Append('\n');
        builder.Append("platform=").Append(platform ?? string.Empty).Append('\n');
        builder.Append("compression=").Append(compression ?? string.Empty).Append('\n');
        builder.Append("buildFormat=").Append(buildFormat ?? string.Empty);

        return HashGenerator.GenerateStringHash(builder.ToString());
    }

    /// <summary>成员行的规范文本：guid|dependencyHash|contentHash|依赖 GUID 列表（升序去重）。</summary>
    private static string DescribeMember(in BundleBuildInputMember member)
    {
        var dependencies = new List<string>();
        if (member.MemberDependencies != null)
        {
            for (int i = 0; i < member.MemberDependencies.Length; i++)
            {
                string dependency = member.MemberDependencies[i];
                if (!string.IsNullOrEmpty(dependency) && !dependencies.Contains(dependency))
                    dependencies.Add(dependency);
            }
        }

        dependencies.Sort(StringComparer.Ordinal);
        return string.Concat(
            "member=",
            member.Guid,
            "|",
            member.DependencyHash,
            "|",
            member.ContentHash,
            "|",
            string.Join(",", dependencies));
    }
}

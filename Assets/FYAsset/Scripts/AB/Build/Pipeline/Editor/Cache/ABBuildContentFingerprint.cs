using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 内容输入指纹的 Unity 侧采集：把内容成员折算成 GUID、Unity 依赖哈希与成员间依赖关系，
/// 再交给 BundleBuildInputFingerprint 计算跨机器稳定的指纹；构建配方指纹也在此折算。
/// </summary>
/// <remarks>
/// 成员无法提供任何变化检测输入时返回 false，调用方必须放弃该内容的历史制品复用。
/// </remarks>
public static class ABBuildContentFingerprint
{
    /// <summary>
    /// 构建配方版本：分组路线、BuildAssetBundleOptions 或产物命名方式变化时必须递增，使历史包的复用事实自然失效。
    /// </summary>
    public const int BuildRecipeVersion = 1;

    /// <summary>目标平台标识。</summary>
    public static string ResolvePlatformToken(BuildTarget platform)
        => platform.ToString();

    /// <summary>压缩模式标识。</summary>
    public static string ResolveCompressionToken(BundleCompression compression)
        => compression.ToString();

    /// <summary>
    /// Unity 与构建格式标识：Unity 版本、构建配方版本与脚本后端。
    /// 任一项变化都可能改变序列化产物，因此纳入输入指纹与构建配方指纹。
    /// </summary>
    public static string ResolveBuildFormatToken(BuildTarget platform)
    {
        BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(platform);
        return string.Concat(
            "unity=", Application.unityVersion,
            ";recipe=", BuildRecipeVersion,
            ";scriptingBackend=", PlayerSettings.GetScriptingBackend(group));
    }

    /// <summary>
    /// 本次构建的配方指纹：由后端、平台、压缩模式与构建格式共同决定。
    /// 构建与导出两侧共用同一入口，保证写入 Summary 的配方与复用判定用的配方一致。
    /// </summary>
    public static string ComputeRecipeFingerprint(BuildConfig config, BundleCompression compression)
    {
        string platformToken = ResolvePlatformToken(config.TargetPlatform);
        return BundleBuildInputFingerprint.ComputeRecipeFingerprint(
            platformToken,
            ResolveCompressionToken(compression),
            ResolveBuildFormatToken(config.TargetPlatform));
    }

    /// <summary>
    /// 计算内容指纹；成员列表为空或所有成员都无法检测变化时返回 false。
    /// </summary>
    public static bool TryCompute(
        string contentName,
        IReadOnlyList<CollectedAssetInfo> members,
        string platform,
        string compression,
        string buildFormat,
        out string fingerprint)
    {
        fingerprint = null;
        if (members == null || members.Count == 0)
            return false;

        var memberGuids = new HashSet<string>(StringComparer.Ordinal);
        var memberPaths = new List<string>(members.Count);
        for (int i = 0; i < members.Count; i++)
        {
            string assetPath = members[i]?.AssetPath;
            memberPaths.Add(assetPath);
            string guid = GetGuid(assetPath);
            if (guid.Length > 0)
                memberGuids.Add(guid);
        }

        var inputs = new List<BundleBuildInputMember>(members.Count);
        bool hasAnyInput = false;
        for (int i = 0; i < memberPaths.Count; i++)
        {
            string assetPath = memberPaths[i];
            string guid = GetGuid(assetPath);
            string dependencyHash = GetDependencyHash(assetPath, guid);
            string contentHash = guid.Length > 0 ? string.Empty : ComputeContentHash(assetPath);
            string[] memberDependencies = memberGuids.Count > 1
                ? CollectMemberDependencies(assetPath, guid, memberGuids)
                : null;

            var input = new BundleBuildInputMember(guid, dependencyHash, contentHash, memberDependencies);
            if (input.HasAnyInput)
                hasAnyInput = true;

            inputs.Add(input);
        }

        if (!hasAnyInput)
            return false;

        fingerprint = BundleBuildInputFingerprint.Compute(contentName, inputs, platform, compression, buildFormat);
        return true;
    }

    private static string GetGuid(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return string.Empty;

        return AssetDatabase.AssetPathToGUID(assetPath) ?? string.Empty;
    }

    /// <summary>
    /// Unity 依赖哈希：覆盖资产自身导入结果与整棵依赖树的导入结果。
    /// 非 Unity 资产或调用失败时返回空串，由文件内容哈希兜底。
    /// </summary>
    private static string GetDependencyHash(string assetPath, string guid)
    {
        if (string.IsNullOrEmpty(assetPath) || guid.Length == 0)
            return string.Empty;

        try
        {
            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>非 Unity 资产的内容哈希：读不到文件时返回空串。</summary>
    private static string ComputeContentHash(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return string.Empty;

        string fullPath = FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, assetPath);
        return FileHelper.TryCreateDigest(fullPath, Path.GetFileName(assetPath), out FileHelper.FileDigest digest)
            ? digest.Hash
            : string.Empty;
    }

    /// <summary>同一内容内部被该成员引用的成员 GUID；只表达内容内依赖关系，不展开整棵依赖树。</summary>
    private static string[] CollectMemberDependencies(string assetPath, string selfGuid, HashSet<string> memberGuids)
    {
        if (string.IsNullOrEmpty(assetPath) || selfGuid.Length == 0)
            return null;

        string[] dependencies;
        try
        {
            dependencies = AssetDatabase.GetDependencies(assetPath, false);
        }
        catch (Exception)
        {
            return null;
        }

        if (dependencies == null || dependencies.Length == 0)
            return null;

        var result = new List<string>();
        for (int i = 0; i < dependencies.Length; i++)
        {
            string dependencyGuid = GetGuid(dependencies[i]);
            if (dependencyGuid.Length == 0
                || string.Equals(dependencyGuid, selfGuid, StringComparison.Ordinal)
                || !memberGuids.Contains(dependencyGuid)
                || result.Contains(dependencyGuid))
            {
                continue;
            }

            result.Add(dependencyGuid);
        }

        return result.Count > 0 ? result.ToArray() : null;
    }
}

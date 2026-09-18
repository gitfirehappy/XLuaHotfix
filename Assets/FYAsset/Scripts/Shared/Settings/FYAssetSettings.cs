using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FYAsset 运行期与全局设置。
/// Runtime 程序集，打包时包含在 build 中。
/// </summary>
public class FYAssetSettings : ScriptableObject
{
    // ═══ 可配置字段（SO 实例数据） ═══

    [Header("Project")]
    public string ProjectName = "ProjectName";

    [Header("Build")]
    public string BuildOutputRoot = "HotfixOutput";
    public string BuildPackagesFolderName = "Packages";

    [Header("Version")]
    public string BuildIndexJsonPath = "Assets/Build/Bootstrap/BuildIndex.json";

    [Header("Publish")]
    public List<PublishTargetConfig> PublishTargets = new();
    public string CurrentABTargetId = string.Empty;

    /// <summary>依据稳定 TargetId 校验并解析发布目标；目标列表归设置对象所有。</summary>
    public bool TryResolvePublishTarget(
        string targetId,
        out PublishTargetConfig target,
        out string error)
    {
        target = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            error = "当前发布目标未选择。";
            return false;
        }
        if (PublishTargets == null || PublishTargets.Count == 0)
        {
            error = "发布目标列表为空。";
            return false;
        }

        var targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < PublishTargets.Count; i++)
        {
            PublishTargetConfig candidate = PublishTargets[i];
            if (candidate == null)
            {
                error = $"发布目标列表包含空项（索引 {i}）。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(candidate.TargetId)
                || !Guid.TryParse(candidate.TargetId, out _)
                || !targetIds.Add(candidate.TargetId))
            {
                error = $"发布目标 TargetId 必须是非重复 GUID：{candidate.TargetId}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(candidate.Name) || !names.Add(candidate.Name))
            {
                error = $"发布目标名称为空或重复：{candidate.Name}";
                return false;
            }
            if (string.Equals(candidate.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                target = candidate;
        }

        if (target == null)
        {
            error = $"当前发布目标不存在：{targetId}";
            return false;
        }
        if (!target.TryNormalizePublicBaseUrl(out _, out string urlError))
        {
            error = $"当前发布目标 '{target.Name}' 的公开地址无效：{urlError}";
            target = null;
            return false;
        }
        return true;
    }

    // ═══ 纯编译期常量（static const） ═══

    public const string HOTFIX_GROUP_NAME = "HotfixGroup";
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";

    /// <summary>累计 Hotfix 输出根目录名（相对 BuildOutputRoot）：Hotfix 累计、Full 重置</summary>

    // --- 输出文件命名 ---
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";
    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";
    public const string AA_MANIFEST_FILE_NAME = "AAManifest.json";
    public const string AA_MANIFEST_FILE_NAME_BIN = "AAManifest.bin";
    public const string ADDRESSABLES_CATALOG_FILE_NAME = "catalog.json";
    public const string BUNDLES_DIRECTORY_NAME = "bundles";
    public const string STANDALONE_DIRECTORY_NAME = "Standalone";

    // ═══ Singleton ═══

    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= LoadOrCreate();

    public const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetSettings.asset";
    public const string RESOURCE_LOAD_PATH = "FYAssetSettings";

    private static FYAssetSettings LoadOrCreate()
    {
        return FYAssetSettingsLoader.LoadOrCreate<FYAssetSettings>(DEFAULT_ASSET_PATH, RESOURCE_LOAD_PATH);
    }

}

public enum ManifestOutputFormat
{
    JsonOnly = 0,
    JsonAndBinary = 1,
    BinaryOnly = 2
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Shared build-time paths and release targets used by AA and AB backends.
/// </summary>
public sealed class SharedBuildSettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetSharedBuildSettings.asset";

    [Header("Output")]
    public string BuildOutputRoot = "HotfixOutput";

    [Header("Version")]
    public string VersionDataBasePath = "Assets/Build/VersionDataBase.asset";
    public string BuildIndexJsonPath = "Assets/Build/Bootstrap/BuildIndex.json";

    [Header("Collector")]
    [FormerlySerializedAs("CollectorDataFolder")]
    public string AssetCollectionDataFolder = "Assets/FYAsset/CollectorData";

    [FormerlySerializedAs("CollectorSettingPath")]
    public string AssetCollectionSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";

    public List<string> DependencyFilterExtensions = new();

    [Header("AA")]
    public string LuaScriptsIndexPath = "Assets/Build/LuaScriptsIndex.asset";

    [Header("Repository Push")]
    public List<PushTargetConfig> PushTargets = new();
}

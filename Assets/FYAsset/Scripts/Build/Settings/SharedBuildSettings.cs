using System.Collections.Generic;
using UnityEngine;

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
    public string CollectorDataFolder = "Assets/FYAsset/CollectorData";
    public string CollectorSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";

    [Header("AA")]
    public string LuaScriptsIndexPath = "Assets/Build/LuaScriptsIndex.asset";

    [Header("Repository Push")]
    public List<PushTargetConfig> PushTargets = new();
}

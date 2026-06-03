using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Build Repository editor-time configuration.
/// </summary>
public sealed class BuildRepositorySettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetBuildRepositorySettings.asset";

    [Header("Push")]
    public List<PushTargetConfig> PushTargets = new();
}

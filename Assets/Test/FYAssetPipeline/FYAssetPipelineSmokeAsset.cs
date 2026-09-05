using UnityEngine;

/// <summary>
/// Permanent FYAsset pipeline smoke ScriptableObject.
/// Marker is the only runtime assertion surface for the async typed load path.
/// </summary>
public sealed class FYAssetPipelineSmokeAsset : ScriptableObject
{
    public string Marker = "fyasset-pipeline-async:v1";
}

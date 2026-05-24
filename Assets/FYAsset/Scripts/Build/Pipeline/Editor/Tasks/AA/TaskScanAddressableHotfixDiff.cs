using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AA hotfix diff scan Task。只扫描并写入 ArtifactDelta，不修改 Addressables settings。
/// </summary>
public class TaskScanAddressableHotfixDiff : IBuildTask
{
    public string TaskName => "TaskScanAddressableHotfixDiff";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.BuildType
    };
    public string[] WriteKeys => new[] { BuildContextKeys.ArtifactDelta };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);

        if (buildType != BuildType.Hotfix)
        {
            ctx.Set(BuildContextKeys.ArtifactDelta, new ArtifactDelta());
            return BuildTaskResult.Ok(new List<string> { "[AA DIFF] Full build skipped" });
        }

        try
        {
            ArtifactDelta delta = DifferentialProcessor.ScanAddressableHotfixDiff(request.Version);
            ctx.Set(BuildContextKeys.ArtifactDelta, delta ?? new ArtifactDelta());

            if (delta == null || delta.IsEmpty)
            {
                Debug.Log("[TaskScanAddressableHotfixDiff] No artifact changes detected. Continue hotfix build.");
                return BuildTaskResult.Ok(new List<string> { "[AA DIFF] No changes" });
            }

            return BuildTaskResult.Ok(new List<string>
            {
                $"[AA DIFF] Added={delta.Added.Count}, Modified={delta.Modified.Count}, Removed={delta.Removed.Count}"
            });
        }
        catch (System.Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, $"AA hotfix diff scan failed: {ex.Message}", true);
        }
    }
}

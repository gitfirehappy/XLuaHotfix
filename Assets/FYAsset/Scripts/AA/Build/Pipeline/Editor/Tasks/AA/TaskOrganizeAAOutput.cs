using System.Collections.Generic;

/// <summary>
/// AA 输出组织 Task — 将 ServerData 产物整理到 BuildPackageRequest 指向的最终包目录。
/// </summary>
public class TaskOrganizeAAOutput : IBuildTask
{
    public string TaskName => "TaskOrganizeAAOutput";
    public string[] DependsOn => new[] { "TaskBuildAddressablesContent" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.AAServerDataPath
    };
    public string[] WriteKeys => new[] { BuildContextKeys.OutputPath };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        string serverDataPath = ctx.Require<string>(BuildContextKeys.AAServerDataPath);

        AddressablesBuildOutputOrganizer.OrganizeBuildOutput(serverDataPath, request.OutputDir);
        ctx.Set(BuildContextKeys.OutputPath, request.OutputDir);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AA ORGANIZE] {serverDataPath} -> {request.OutputDir}"
        });
    }
}

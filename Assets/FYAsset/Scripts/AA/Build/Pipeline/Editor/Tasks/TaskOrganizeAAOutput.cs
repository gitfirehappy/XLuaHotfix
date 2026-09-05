using System.Collections.Generic;

/// <summary>
/// AA 输出组织 Task — 规范化直接写入最终包目录的 catalog 文件。
/// </summary>
public class TaskOrganizeAAOutput : IBuildTask
{
    public string TaskName => "TaskOrganizeAAOutput";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        AABuildOutputOrganizer.NormalizeBuildOutput(request.OutputDir);
        ctx.Set(BuildContextKeys.OutputPath, request.OutputDir);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[AA ORGANIZE] Catalog normalized in {request.OutputDir}"
        });
    }
}

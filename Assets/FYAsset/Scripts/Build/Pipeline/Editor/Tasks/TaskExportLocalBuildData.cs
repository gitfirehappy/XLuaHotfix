using System.Collections.Generic;

/// <summary>
/// 本地启动数据导出 Task — 仅整包构建导出 BuildIndex 与 baseline manifest 到 StreamingAssets。
/// 挂在 AA/AB Task 图尾部；Hotfix 构建保持跳过。
/// </summary>
public class TaskExportLocalBuildData : IBuildTask
{
    public string TaskName => "TaskExportLocalBuildData";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.BuildType,
        BuildContextKeys.OutputPath
    };
    public string[] WriteKeys => new string[0];

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);

        if (buildType != BuildType.Full)
        {
            return BuildTaskResult.Ok(new List<string>
            {
                "[LOCAL BUILD DATA] Hotfix build skipped"
            });
        }

        string outputPath = ctx.Require<string>(BuildContextKeys.OutputPath);
        if (!string.Equals(outputPath, request.OutputDir, System.StringComparison.Ordinal))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"本地构建数据导出必须基于 BuildPackageRequest 输出目录。Expected: {request.OutputDir}, Actual: {outputPath}", true);

        if (!FileHelper.DirectoryExists(request.OutputDir))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"本地构建数据导出前最终输出目录不存在: {request.OutputDir}", true);

        LocalStatusExporter.ExportData(request.Version);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[LOCAL BUILD DATA] Version: {request.Version.GetFullVersionString()}"
        });
    }
}

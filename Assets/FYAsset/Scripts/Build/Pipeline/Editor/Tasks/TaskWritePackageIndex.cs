using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 写入远端最新包指针 PackageIndex。
/// 它不是 Full baseline 数据；Full 和 Hotfix 正式构建都要更新它，Diff Preview 会在本 Task 前 stop-after。
/// </summary>
public class TaskWritePackageIndex : IBuildTask
{
    public string TaskName => "TaskWritePackageIndex";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new[] { BuildContextKeys.BuildPackageRequest };
    public string[] WriteKeys => new string[0];

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        if (request == null)
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "BuildPackageRequest is null.", true);

        var data = new PackageIndex
        {
            LatestPackage = request.PackageName,
            LatestVersion = request.Version,
            BackendMode = BackendModeNames.FromBackendMode(request.BackendMode)
        };

        string directory = Path.GetDirectoryName(request.PackageIndexPath);
        if (!string.IsNullOrEmpty(directory))
            FileHelper.EnsureDirectory(directory);

        SerializationUtility.WriteToFile(request.PackageIndexPath, data);
        Debug.Log($"[{nameof(TaskWritePackageIndex)}] PackageIndex 已更新: Package={request.PackageName}, Version={request.Version.GetFullVersionString()}, Backend={data.BackendMode}, Path={request.PackageIndexPath}");

        return BuildTaskResult.Ok(new List<string>
        {
            $"[PACKAGE INDEX] {request.PackageName}"
        });
    }
}

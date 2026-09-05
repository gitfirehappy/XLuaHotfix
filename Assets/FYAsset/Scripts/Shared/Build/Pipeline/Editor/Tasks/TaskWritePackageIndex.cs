using System;
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
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        if (request == null)
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "BuildPackageRequest is null.", true);

        if (ctx.Get<bool>(BuildContextKeys.DeferPackagePublication))
        {
            return BuildTaskResult.Ok(new List<string>
            {
                "[PACKAGE INDEX] 写入时机由 BuildProjectRunner 在发布阶段编排"
            });
        }

        Publish(request);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[PACKAGE INDEX] {request.PackageName}"
        });
    }

    public static void Publish(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var data = new PackageIndex
        {
            LatestPackage = request.PackageName,
            LatestVersion = request.Version,
            BackendMode = request.BackendKey
        };

        string directory = Path.GetDirectoryName(request.PackageIndexPath);
        if (!string.IsNullOrEmpty(directory))
            FileHelper.EnsureDirectory(directory);

        FileHelper.WriteAllTextAtomic(request.PackageIndexPath, SerializationUtility.SerializeToJson(data, true));
        Debug.Log($"[{nameof(TaskWritePackageIndex)}] PackageIndex 已更新: Package={request.PackageName}, Version={request.Version.GetReleaseVersionString()}, Backend={data.BackendMode}, Path={request.PackageIndexPath}");
    }
}

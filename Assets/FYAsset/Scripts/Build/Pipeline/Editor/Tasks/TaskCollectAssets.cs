using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 管线 Task：执行 CollectorSetting 扫描并写入 CollectedAssets / SharePolicies。
/// </summary>
public class TaskCollectAssets : IBuildTask
{
    public string TaskName => "TaskCollectAssets";
    public string[] DependsOn => new[] { "TaskPrepareContext" };
    public string[] ReadKeys => new string[0];
    public string[] WriteKeys => new[] { BuildContextKeys.CollectedAssets, BuildContextKeys.SharePolicies };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        CollectorSetting setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
            FYAssetSettings.Instance.CollectorSettingPath);
        if (setting == null)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.SettingNull,
                $"未找到 CollectorSetting: {FYAssetSettings.Instance.CollectorSettingPath}");
        }

        List<string> warnings = new List<string>();

        List<BuildMessage> validationMessages = CollectorSettingValidator.Validate(setting);
        if (AppendMessages(validationMessages, warnings))
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"CollectorSetting 校验失败，共 {validationMessages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        // 执行全量扫描
        ScanResult scanResult = CollectionScanner.Scan(setting);
        bool hasError = false;

        // 扫描消息分类归档
        hasError = AppendMessages(scanResult.Messages, warnings);

        // Error 级别 -> 阻断管线，携带 Warning 列表
        if (hasError)
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"Collection 扫描失败，共 {scanResult.Messages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        // 写入 BuildContext 供下游 Task 消费
        ctx.Set(BuildContextKeys.CollectedAssets, scanResult.Assets);
        ctx.Set(BuildContextKeys.SharePolicies, CollectSharePolicies(setting));

        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }

    private static bool AppendMessages(List<BuildMessage> messages, List<string> warnings)
    {
        bool hasError = false;
        if (messages == null)
            return false;

        foreach (BuildMessage message in messages)
        {
            warnings.Add($"[{message.Code}] {message.Message} ({message.Source})");
            if (message.Severity == BuildSeverity.Error)
                hasError = true;
        }

        return hasError;
    }

    private static Dictionary<string, SharePolicyConfig> CollectSharePolicies(CollectorSetting setting)
    {
        Dictionary<string, SharePolicyConfig> policies = new Dictionary<string, SharePolicyConfig>();
        if (setting?.Packages == null)
            return policies;

        foreach (CollectorPackage package in setting.Packages)
        {
            if (package == null || string.IsNullOrEmpty(package.PackageName) || package.SharePolicy == null)
                continue;

            policies[package.PackageName] = package.SharePolicy;
        }

        return policies;
    }
}

using System.Collections.Generic;

/// <summary>
/// AB 构建管线：读取并校验采集配置，扫描显式资源并冻结后续分析输入。
/// 不自动注入 Resources 或全局 Shader。
/// </summary>
public class CollectABAssetsTask : IBuildTask
{
    public string TaskName => "CollectABAssets";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        AssetCollectionSetting setting = CollectorMutationUtility.LoadSetting();
        if (setting == null)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.SettingNull,
                $"未找到 AssetCollectionSetting: {FYAssetABSettings.Instance.AssetCollectionSettingPath}");
        }

        var warnings = new List<string>();
        List<BuildMessage> validationMessages = AssetCollectionSettingValidator.Validate(setting);
        if (AppendMessages(validationMessages, warnings))
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"AssetCollectionSetting 校验失败，共 {validationMessages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        ScanResult scanResult = CollectionScanner.Scan(setting);
        if (AppendMessages(scanResult.Messages, warnings))
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.CollectAssetsFailed,
                $"Collection 扫描失败，共 {scanResult.Messages.Count} 个问题。");
            result.Warnings = warnings;
            return result;
        }

        if (scanResult.Assets == null || scanResult.Assets.Count == 0)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.NoCollectedAssets,
                "Collection 扫描没有产出任何 Asset。请检查 Collector 配置。",
                false);
        }

        var snapshot = new ABCollectionSnapshot(
            scanResult.Assets,
            setting.SharePolicy,
            setting.RawFileRules,
            setting.GetEffectiveIgnorePatterns(),
            FYAssetABSettings.Instance.DependencyFilterExtensions);
        ctx.Set(ABBuildContextKeys.CollectionSnapshot, snapshot);

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
}

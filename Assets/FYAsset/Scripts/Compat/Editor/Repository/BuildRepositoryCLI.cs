#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建发布命令行入口。
/// 暴露 status / diff / push（baseline 语义；历史审计走 git log）。
/// </summary>
public static class BuildRepositoryCLI
{
    public static void Status()
    {
        var args = ParseArgs();
        string channelKey = GetChannelKey(args);
        BuildBaselineState state;
        try
        {
            state = BuildBaselineStore.Load(channelKey);
        }
        catch (BuildBaselineException ex)
        {
            WriteLine($"Baseline ERROR: {ex.Message}");
            EditorApplication.Exit(1);
            return;
        }

        WriteLine($"Channel: {channelKey}");
        WriteLine($"Latest: {(state.Latest?.Version != null ? state.Latest.Version.GetReleaseVersionString() + " | " + state.Latest.PackageName : "(none)")}");
        WriteLine($"LatestFull: {(state.LatestFull?.Version != null ? state.LatestFull.Version.GetReleaseVersionString() : "(none)")}");
        WriteLine($"Artifacts: {(state.Latest?.Artifacts != null ? state.Latest.Artifacts.Count : 0)}");
        WriteLine($"History: git log -- BuildData/Baselines/{channelKey}/baseline.json");
        EditorApplication.Exit(0);
    }

    public static void Diff()
    {
        try
        {
            var args = ParseArgs();
            var version = LoadCurrentVersion();
            string backend = GetBackend(args);
            var request = BuildPackageRequest.Create(version, BuildType.Hotfix, backend);
            if (string.Equals(backend, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase))
            {
                var preview = ABRepositoryPreview.RunDiffPreview(request);
                WriteABPreview(preview);
                if (args.TryGetValue("-json", out string jsonPath) && !string.IsNullOrEmpty(jsonPath))
                    FileHelper.WriteAllTextAtomic(jsonPath, SerializationUtility.SerializeToJson(preview, true));
            }
            else
            {
                var delta = AARepositoryPreview.Run(request);
                WriteDelta(delta);
                if (args.TryGetValue("-json", out string jsonPath) && !string.IsNullOrEmpty(jsonPath))
                    FileHelper.WriteAllTextAtomic(jsonPath, SerializationUtility.SerializeToJson(delta, true));
            }
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            WriteLine($"Diff failed: {ex.Message}");
            EditorApplication.Exit(1);
        }
    }

    public static void Push()
    {
        var args = ParseArgs();
        var targetId = GetArg(args, "-target", string.Empty);
        var target = CreatePushTarget(targetId);
        var receipt = BuildPublisher.PushLatest(GetChannelKey(args), target);
        WriteLine(SerializationUtility.SerializeToJson(receipt, true));
        EditorApplication.Exit(receipt != null && receipt.Success ? 0 : 1);
    }


    private static string GetBackend(Dictionary<string, string> args)
    {
        string name = GetArg(args, "-backend", string.Empty);
        return string.Equals(name, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase)
            ? BackendModeNames.AB
            : BackendModeNames.AA;
    }

    private static string GetChannelKey(Dictionary<string, string> args)
    {
        return BuildBaselineStore.GetChannelKey(GetArg(args, "-channel", string.Empty), GetBackend(args));
    }

    private static IPushTarget CreatePushTarget(string targetId)
    {
        // -target 缺省时取配置首项（与面板默认选择一致）。
        PushTargetConfig config = !string.IsNullOrEmpty(targetId)
            ? PushTargetUtility.FindConfig(targetId)
            : (FYAssetSettings.Instance.PushTargets != null && FYAssetSettings.Instance.PushTargets.Count > 0
                ? FYAssetSettings.Instance.PushTargets[0]
                : null);
        if (config == null)
            throw new InvalidOperationException($"Push target not found: '{targetId}' (configure under FYAssetSettings.PushTargets)");
        // 扩展 target（如 CloudflarePages）由 Compat 注入工厂提供。
        return CompatPushTargetFactory.CreateFull(config);
    }

    private static VersionNumber LoadCurrentVersion()
    {
        var versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        if (versionData == null)
            throw new InvalidOperationException($"VersionRecord not found: {FYAssetSettings.Instance.VersionRecordPath}");
        return versionData.CurrentVersion;
    }

    private static Dictionary<string, string> ParseArgs()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("-"))
                continue;
            int equalsIndex = args[i].IndexOf('=');
            if (equalsIndex > 0)
            {
                string key = args[i].Substring(0, equalsIndex);
                string value = equalsIndex + 1 < args[i].Length ? args[i].Substring(equalsIndex + 1) : string.Empty;
                result[key] = value;
                continue;
            }
            string argValue = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : string.Empty;
            result[args[i]] = argValue;
            if (!string.IsNullOrEmpty(argValue))
                i++;
        }
        return result;
    }

    private static string GetArg(Dictionary<string, string> args, string key, string defaultValue)
    {
        return args.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : defaultValue;
    }

    private static void WriteDelta(ArtifactDelta delta)
    {
        var builder = new StringBuilder();
        AppendDelta(builder, delta);
        WriteLine(builder.ToString());
    }

    private static void WriteABPreview(ABRepositoryPreviewResult preview)
    {
        preview ??= new ABRepositoryPreviewResult();
        var builder = new StringBuilder();
        builder.AppendLine("HEAD Diff (current vs Latest baseline)");
        AppendDelta(builder, preview.HeadDelta ?? new ArtifactDelta());
        builder.AppendLine("Hotfix Delivery (current vs Full baseline)");
        builder.AppendLine($"DeliveryAvailable: {preview.DeliveryAvailable}");
        if (!string.IsNullOrEmpty(preview.DeliveryMessage))
            builder.AppendLine($"DeliveryMessage: {preview.DeliveryMessage}");
        builder.AppendLine($"DeliveryBundles: {(preview.DeliveryBundles != null ? preview.DeliveryBundles.Count : 0)}");
        builder.AppendLine($"DeliverySizeBytes: {preview.DeliverySizeBytes}");
        if (preview.DeliveryBundles != null)
        {
            for (int i = 0; i < preview.DeliveryBundles.Count; i++)
            {
                var bundle = preview.DeliveryBundles[i];
                if (bundle != null)
                    builder.AppendLine($"{bundle.BundleName} | {bundle.FileSize}");
            }
        }
        WriteLine(builder.ToString());
    }

    private static void AppendDelta(StringBuilder builder, ArtifactDelta delta)
    {
        delta ??= new ArtifactDelta();
        builder.AppendLine($"Added: {delta.Added.Count}");
        for (int i = 0; i < delta.Added.Count; i++)
            builder.AppendLine(delta.Added[i].Name);
        builder.AppendLine($"Modified: {delta.Modified.Count}");
        for (int i = 0; i < delta.Modified.Count; i++)
            builder.AppendLine(delta.Modified[i].Name);
        builder.AppendLine($"Removed: {delta.Removed.Count}");
        for (int i = 0; i < delta.Removed.Count; i++)
            builder.AppendLine(delta.Removed[i]);
    }

    private static void WriteLine(string text)
    {
        Console.WriteLine(text);
    }
}
#endif

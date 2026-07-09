#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build Repository 命令行入口。
/// 暴露 status / health / diff / push / list-commits。
/// </summary>
public static class BuildRepositoryCLI
{
    public static void Status()
    {
        var args = ParseArgs();
        var channelKey = GetChannelKey(args);
        var status = BuildRepositoryFacade.GetStatus(channelKey);
        var health = BuildRepositoryFacade.GetHealth(channelKey);
        WriteLine($"Channel: {status.ChannelKey}");
        WriteLine($"HEAD: {(status.HasHead ? status.HeadVersion : "(none)")}");
        WriteLine($"Package: {status.PackageName}");
        WriteLine($"Artifacts: {status.ArtifactCount}");
        WriteHealth(health);
        EditorApplication.Exit(health != null && health.HasFatalIssue ? 1 : 0);
    }

    public static void Health()
    {
        var args = ParseArgs();
        var health = BuildRepositoryFacade.GetHealth(GetChannelKey(args));
        WriteHealth(health);
        EditorApplication.Exit(health != null && health.HasFatalIssue ? 1 : 0);
    }

    public static void Diff()
    {
        try
        {
            var args = ParseArgs();
            var version = LoadCurrentVersion();
            var backend = GetBackend(args);
            var request = BuildPackageRequest.Create(version, BuildType.Hotfix, backend);
            if (backend == BackendMode.ABManifest)
            {
                var preview = RepositoryPreviewRunner.RunABPreviewDetailed(request);
                WriteABPreview(preview);
                if (args.TryGetValue("-json", out string jsonPath) && !string.IsNullOrEmpty(jsonPath))
                    FileHelper.WriteAllTextAtomic(jsonPath, SerializationUtility.SerializeToJson(preview, true));
            }
            else
            {
                var delta = RepositoryPreviewRunner.RunAAPreview(request);
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
        var fromVersion = GetVersion(args, "-from");
        var toVersion = GetVersion(args, "-to");
        var targetId = GetArg(args, "-target", string.Empty);
        var target = CreatePushTarget(targetId);
        var receipt = BuildRepositoryFacade.Push(GetChannelKey(args), fromVersion, toVersion, target);
        WriteLine(SerializationUtility.SerializeToJson(receipt, true));
        EditorApplication.Exit(receipt != null && receipt.Success ? 0 : 1);
    }

    public static void ListCommits()
    {
        var args = ParseArgs();
        var commits = BuildRepositoryFacade.ListCommits(GetChannelKey(args));
        for (int i = 0; i < commits.Count; i++)
        {
            var commit = commits[i];
            WriteLine($"{commit.Version?.GetReleaseVersionString()} | {commit.BuildType} | {commit.CreatedAtUtc} | {commit.PackageName}");
        }
        EditorApplication.Exit(0);
    }

    private static IPushTarget CreatePushTarget(string targetId)
    {
        var settings = FYAssetSettings.Instance;
        for (int i = 0; i < settings.PushTargets.Count; i++)
        {
            var config = settings.PushTargets[i];
            if (config != null && string.Equals(config.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return new LocalDirectoryPushTarget(config);
        }
        throw new InvalidOperationException($"Unknown push target: {targetId}");
    }

    private static BackendMode GetBackend(Dictionary<string, string> args)
    {
        string value = GetArg(args, "-backend", "AA");
        return string.Equals(value, "AB", StringComparison.OrdinalIgnoreCase) ? BackendMode.ABManifest : BackendMode.AA;
    }

    private static string GetChannelKey(Dictionary<string, string> args)
    {
        return BuildRepositoryFacade.GetChannelKey(GetArg(args, "-channel", string.Empty), GetBackend(args));
    }

    private static VersionNumber GetVersion(Dictionary<string, string> args, string key)
    {
        string value = GetArg(args, key, string.Empty);
        if (string.IsNullOrEmpty(value))
            return null;
        return VersionNumber.Parse(value);
    }

    private static VersionNumber LoadCurrentVersion()
    {
        var versionData = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        if (versionData == null)
            throw new InvalidOperationException($"VersionDataBase not found: {FYAssetSettings.Instance.VersionDataBasePath}");
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
        builder.AppendLine("HEAD Diff (current vs Repository HEAD)");
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

    private static void WriteHealth(RepositoryHealthReport health)
    {
        if (health == null)
        {
            WriteLine("Health: (unavailable)");
            return;
        }

        WriteLine($"Health: {health.Summary}");
        WriteLine($"Fatal: {health.FatalCount}");
        for (int i = 0; i < health.FatalIssues.Count; i++)
            WriteLine($"  {health.FatalIssues[i]}");
        WriteLine($"Warnings: {health.WarningCount}");
        for (int i = 0; i < health.Warnings.Count; i++)
            WriteLine($"  {health.Warnings[i]}");
    }

    private static void WriteLine(string text)
    {
        Console.WriteLine(text);
    }
}
#endif

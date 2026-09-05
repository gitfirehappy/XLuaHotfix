#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 用于 AB report/package 同步的安全 batchmode self-check。
/// 仅使用临时目录，并且始终执行清理。
/// </summary>
public static class ABBuildReportStoreSelfCheck
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), nameof(ABBuildReportStoreSelfCheck) + "_" + Guid.NewGuid().ToString("N"));
        string reportsDirectory = Path.Combine(root, "Reports");
        string existingPackage = Path.Combine(root, "Packages", "Build_20260711010101_1.0.0");
        string missingPackage = Path.Combine(root, "Packages", "Build_20260711020202_1.0.1");

        try
        {
            Directory.CreateDirectory(existingPackage);

            string matchingReport = WriteReport(reportsDirectory, "matching.json", existingPackage, true);
            string unrelatedReport = WriteReport(reportsDirectory, "unrelated.json", missingPackage, true);

            List<string> matches = ABBuildReportStore.ListReportPathsByPackagePath(existingPackage, reportsDirectory);
            Require(matches.Count == 1 && PathsEqual(matches[0], matchingReport), "Expected exactly one matching report.");

            var failedDeletes = new List<string>();
            int deleted = ABBuildReportStore.DeleteReportsByPackagePath(existingPackage, reportsDirectory, failedDeletes);
            Require(deleted == 1, "Expected one matching report to be deleted.");
            Require(failedDeletes.Count == 0, "Expected no report deletion failures.");
            Require(!File.Exists(matchingReport), "Matching report still exists.");
            Require(File.Exists(unrelatedReport), "Unrelated report was deleted.");

            Require(!ABBuildReportStore.HasPackageConflict(CreateReport(existingPackage, true)), "Existing successful package reported a conflict.");
            Require(ABBuildReportStore.HasPackageConflict(CreateReport(missingPackage, true)), "Missing successful package did not report a conflict.");
            Require(!ABBuildReportStore.HasPackageConflict(CreateReport(missingPackage, false)), "Failed build without a package reported a conflict.");

            VerifyReverseReferences();
            string legacyReportPath = Path.Combine(reportsDirectory, "legacy.json");
            ABBuildReport legacyReport = CreateReport(existingPackage, true);
            legacyReport.Bundles.Add(new ABBuildReportBundle { BundleName = "legacy", ReferencedBy = null });
            ABBuildReportStore.Write(legacyReport, legacyReportPath);
            Require(ABBuildReportStore.Read(legacyReportPath).Bundles[0].ReferencedBy != null,
                "Legacy report did not normalize a missing ReferencedBy list.");

            Debug.Log($"[{nameof(ABBuildReportStoreSelfCheck)}] PASS - report 存储和 Bundle 引用验证通过。");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    private static string WriteReport(string reportsDirectory, string fileName, string packagePath, bool success)
    {
        string path = Path.Combine(reportsDirectory, fileName);
        ABBuildReportStore.Write(CreateReport(packagePath, success), path);
        return path;
    }

    private static ABBuildReport CreateReport(string packagePath, bool success)
    {
        return new ABBuildReport
        {
            Header = new ABBuildReportHeader
            {
                PackagePath = packagePath,
                Success = success
            }
        };
    }

    private static void VerifyReverseReferences()
    {
        var bundles = new List<ABBuildReportBundle>
        {
            new ABBuildReportBundle { BundleName = "A", Dependencies = new List<string> { "B" } },
            new ABBuildReportBundle { BundleName = "B" }
        };
        ABBuildReportBuilder.FillReferencedBy(bundles);
        Require(bundles[1].ReferencedBy.Count == 1 && bundles[1].ReferencedBy[0] == "A",
            "Reverse bundle reference was not generated.");
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif

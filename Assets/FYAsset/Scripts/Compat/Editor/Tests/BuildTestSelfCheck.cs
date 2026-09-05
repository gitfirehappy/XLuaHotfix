#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 轻量自检：夹具矩阵、路径安全与 snapshot/restore 临时目录。
/// </summary>
public static class BuildTestSelfCheck
{
    public static void Run()
    {
        BuildTestFixtures.EnsurePermanentFixtures();
        BuildTestFixtures.AssertPreflight();

        string temp = Path.Combine(Path.GetTempPath(), "FYAssetBuildTestSelfCheck_" + Guid.NewGuid().ToString("N"));
        try
        {
            string runRoot = Path.Combine(temp, "run");
            Directory.CreateDirectory(runRoot);
            Directory.CreateDirectory(Path.Combine(runRoot, "backup", "project"));
            Directory.CreateDirectory(Path.Combine(runRoot, "backup", "targets"));

            // Snapshot/restore roundtrip on a tiny owned tree.
            string owned = Path.Combine(temp, "owned");
            Directory.CreateDirectory(owned);
            string file = Path.Combine(owned, "marker.txt");
            File.WriteAllText(file, "v1");
            string backup = Path.Combine(runRoot, "backup", "project", "owned");
            Directory.CreateDirectory(backup);
            File.Copy(file, Path.Combine(backup, "marker.txt"), true);
            File.WriteAllText(file, "v2");
            File.Copy(Path.Combine(backup, "marker.txt"), file, true);
            if (File.ReadAllText(file) != "v1")
                throw new InvalidOperationException("Snapshot restore self-check failed.");

            if (!BuildTestPaths.IsInsideTestRuns(BuildTestPaths.TestRunsRoot))
                throw new InvalidOperationException("TestRuns root ownership check failed.");

            Debug.Log("[BuildTestSelfCheck] PASS - fixtures + restore self-check.");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }
}
#endif

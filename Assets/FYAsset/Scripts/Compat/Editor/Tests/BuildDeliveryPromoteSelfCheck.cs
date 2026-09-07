#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BuildDeliveryPromoter 自检：验证 attempt → 交付目录的 promote / 回滚契约。
/// 只操作 Path.GetTempPath() 下自带的唯一临时目录，与项目 real build 输出无关。
/// 批跑入口：族 FYAsset/Tests/AB Delivery Promote Self Check 或 -executeMethod BuildDeliveryPromoteSelfCheck.Run。
/// </summary>
public static class BuildDeliveryPromoteSelfCheck
{
    [MenuItem("FYAsset/Tests/AB Delivery Promote Self Check")]
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fyasset-delivery-promote-" + Guid.NewGuid().ToString("N"));
        string attemptRoot = Path.Combine(root, "_attempt");
        try
        {
            CheckNewPackagePromote(root, attemptRoot);
            CheckSwapRollbackRestoresOld(root, attemptRoot);
            CheckSwapCommitKeepsNew(root, attemptRoot);
            CheckAttemptRootGuard(root, attemptRoot);
            Debug.Log("[BuildDeliveryPromoteSelfCheck] PASS");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildDeliveryPromoteSelfCheck] 临时目录清理失败: " + ex.Message);
            }
        }
    }

    private static void CheckNewPackagePromote(string root, string attemptRoot)
    {
        string attempt = NewAttempt(root, attemptRoot, "new-package", "bundle-a");
        string delivery = Path.Combine(root, "HotfixOutput", "Packages", "Build_new");

        var token = BuildDeliveryPromoter.Promote(attempt, delivery, attemptRoot);
        Assert(Directory.Exists(delivery), "promote 后交付目录必须存在");
        Assert(File.Exists(Path.Combine(delivery, "bundles", "bundle-a")), "promote 后 bundle 必须在交付目录");
        Assert(!Directory.Exists(attempt), "promote 后 attempt 目录必须不存在");
        token.Commit();
        Assert(!Directory.Exists(attemptRoot), "commit 后空的 attempt 根必须被清理");
    }

    private static void CheckSwapRollbackRestoresOld(string root, string attemptRoot)
    {
        string delivery = Path.Combine(root, "StreamingAssets", "Standalone");
        Directory.CreateDirectory(Path.Combine(delivery, "bundles"));
        File.WriteAllText(Path.Combine(delivery, "bundles", "old-bundle"), "old");

        string attempt = NewAttempt(root, attemptRoot, "swap-rollback", "new-bundle");
        var token = BuildDeliveryPromoter.Promote(attempt, delivery, attemptRoot);
        Assert(File.ReadAllText(Path.Combine(delivery, "bundles", "new-bundle")) == "new-bundle", "swap 后交付目录必须是新包内容");

        token.Rollback();
        Assert(File.ReadAllText(Path.Combine(delivery, "bundles", "old-bundle")) == "old", "rollback 后旧包必须恢复");
        Assert(!File.Exists(Path.Combine(delivery, "bundles", "new-bundle")), "rollback 后新包内容不得残留");
    }

    private static void CheckSwapCommitKeepsNew(string root, string attemptRoot)
    {
        string delivery = Path.Combine(root, "StreamingAssets", "Standalone");
        Directory.CreateDirectory(Path.Combine(delivery, "bundles"));
        File.WriteAllText(Path.Combine(delivery, "bundles", "old-bundle"), "old");

        string attempt = NewAttempt(root, attemptRoot, "swap-commit", "new-bundle");
        var token = BuildDeliveryPromoter.Promote(attempt, delivery, attemptRoot);
        token.Commit();

        Assert(File.Exists(Path.Combine(delivery, "bundles", "new-bundle")), "commit 后新包必须保留");
        Assert(!File.Exists(Path.Combine(delivery, "bundles", "old-bundle")), "commit 后旧包必须被替换");
    }

    private static void CheckAttemptRootGuard(string root, string attemptRoot)
    {
        // attempt 目录不在声明的 attempt 根下 => 必须拒绝（防止把 live 目录当作 attempt 移动）。
        string stray = Path.Combine(root, "live-looking-dir");
        Directory.CreateDirectory(stray);
        string delivery = Path.Combine(root, "elsewhere", "Build_x");
        try
        {
            BuildDeliveryPromoter.Promote(stray, delivery, attemptRoot);
            throw new InvalidOperationException("promote 必须拒绝 attempt 根之外的源目录。");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string NewAttempt(string root, string attemptRoot, string packageName, string bundleName)
    {
        string attempt = Path.Combine(attemptRoot, packageName);
        Directory.CreateDirectory(Path.Combine(attempt, "bundles"));
        File.WriteAllText(Path.Combine(attempt, "bundles", bundleName), bundleName);
        return attempt;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[BuildDeliveryPromoteSelfCheck] " + message);
    }
}
#endif

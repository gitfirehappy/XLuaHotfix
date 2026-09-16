using System;
using System.Reflection;

/// <summary>
/// 损坏本地包的候选回退和同包修复契约：损坏内容回退到 BuiltIn，修复始终在独立 staging 中完成。
/// </summary>
internal static class HotfixCandidateTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("ShouldUseLocalPackageConsidersPackageCompleteness", VerifyCompletenessInput),
            ("DamagedLocalResetsCandidateToBuiltIn", VerifyDamagedLocalResetsCandidate),
            ("RepairNeverSkipsIsolationInPlace", VerifyRepairNeverSkipsIsolationInPlace),
            ("RepairTargetUsesIndependentStaging", VerifyRepairTargetUsesIndependentStaging));
    }

    /// <summary>
    /// 参数契约：<c>ShouldUseLocalPackage(localPointerTrusted, localIsBuiltInIdentity, localPackageComplete)</c>。
    /// 指针可信但包损坏时必须返回 BuiltIn；当前签名只有两个 bool，无法表达完整性。
    /// </summary>
    private static void VerifyCompletenessInput()
    {
        MethodInfo method = typeof(HotfixStateDecider).GetMethod(
            "ShouldUseLocalPackage", BindingFlags.Public | BindingFlags.Static);
        GateAssert.True(method != null, "HotfixStateDecider.ShouldUseLocalPackage 必须存在");

        ParameterInfo[] parameters = method.GetParameters();
        GateAssert.Equal(3, parameters.Length, "ShouldUseLocalPackage 必须接收指针、内置身份和包完整性");

        GateAssert.Equal(false, (bool)method.Invoke(null, new object[] { true, false, false }),
            "指针可信但本地包损坏时不得使用本地包");
        GateAssert.Equal(true, (bool)method.Invoke(null, new object[] { true, false, true }),
            "指针可信且本地包完整时才使用本地包");
    }

    /// <summary>本地包检查失败时必须重置候选为 BuiltIn，再继续远端检查。</summary>
    private static void VerifyDamagedLocalResetsCandidate()
    {
        string flow = RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs");
        int start = flow.IndexOf(
            "InspectCurrentPackageAsync(IHotfixPipeline pipeline, HotfixContext ctx)",
            StringComparison.Ordinal);
        GateAssert.True(start >= 0, "无法定位 InspectCurrentPackageAsync 方法声明");

        int end = flow.IndexOf("DownloadRemotePackageIndexAsync", start + 1, StringComparison.Ordinal);
        string body = end > start ? flow.Substring(start, end - start) : flow.Substring(start);

        GateAssert.Contains(
            body,
            "ctx.CurrentPackageRoot = ctx.BuiltInPackageRoot",
            "本地包损坏时必须把当前包根重置为内置包根并继续远端检查");
        GateAssert.NotContains(body, "HotfixContentState", "候选来源不得由冗余状态枚举表达");
    }

    /// <summary>同包修复不得以“目标根等于当前根”为前提跳过隔离目录。</summary>
    private static void VerifyRepairNeverSkipsIsolationInPlace()
    {
        GateAssert.NotContains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs"),
            "IsSamePackageRoot(ctx.TargetGUIDRoot, ctx.CurrentPackageRoot)",
            "同包修复不得以“目标根等于当前根”为前提跳过隔离目录");
    }

    /// <summary>目标包写入根必须是独立 staging，不得直接指向当前热更包根。</summary>
    private static void VerifyRepairTargetUsesIndependentStaging()
    {
        GateAssert.NotContains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs"),
            "ctx.TargetGUIDRoot = RuntimePathManager.GetHotfixPackageRoot(ctx.TargetPackageName)",
            "修复/更新目标必须使用独立 staging 根，不得直接指向当前热更包根");
    }
}

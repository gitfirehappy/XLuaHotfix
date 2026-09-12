using System;
using System.Reflection;

/// <summary>
/// T8 目标契约：损坏本地包必须立即回退 BuiltIn 候选；同包修复必须在独立 staging 中完成。
/// </summary>
/// <remarks>
/// 本门禁在 T0 阶段锁定决策输入与 FlowBase 写入根的目标形态（当前实现无法表达损坏回退、
/// 且以“目标根等于当前根”为前提原地写入）。T8 落地后需在同一契约下补充流程级行为验证。
/// </remarks>
internal static class HotfixCandidateTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("DecideCurrentContentConsidersPackageCompleteness", VerifyCompletenessInput),
            ("DamagedLocalResetsCandidateToBuiltIn", VerifyDamagedLocalResetsCandidate),
            ("RepairNeverSkipsIsolationInPlace", VerifyRepairNeverSkipsIsolationInPlace),
            ("RepairTargetUsesIndependentStaging", VerifyRepairTargetUsesIndependentStaging));
    }

    /// <summary>
    /// 参数契约（T8）：<c>DecideCurrentContent(localPointerTrusted, localIsBuiltInIdentity, localPackageComplete)</c>。
    /// 指针可信但包损坏时必须返回 BuiltIn；当前签名只有两个 bool，无法表达完整性。
    /// </summary>
    private static void VerifyCompletenessInput()
    {
        MethodInfo method = typeof(HotfixStateDecider).GetMethod(
            "DecideCurrentContent", BindingFlags.Public | BindingFlags.Static);
        GateAssert.True(method != null, "HotfixStateDecider.DecideCurrentContent 必须存在");

        ParameterInfo[] parameters = method.GetParameters();
        GateAssert.True(
            parameters.Length >= 3,
            "DecideCurrentContent 必须接收本地包完整性输入（当前签名无法表达“指针可信但包损坏”）");

        object damaged = method.Invoke(null, new object[] { true, false, false });
        GateAssert.Equal(
            HotfixContentState.BuiltIn,
            (HotfixContentState)damaged,
            "指针可信但本地包损坏时必须回退 BuiltIn 候选");

        object healthy = method.Invoke(null, new object[] { true, false, true });
        GateAssert.Equal(
            HotfixContentState.Local,
            (HotfixContentState)healthy,
            "指针可信且本地包完整时必须使用 Local 候选");
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
            "ctx.CurrentContent = HotfixContentState.BuiltIn",
            "本地包损坏时必须把候选重置为 BuiltIn 并继续远端检查");
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

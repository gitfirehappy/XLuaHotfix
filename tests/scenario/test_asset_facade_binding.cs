internal static class AssetFacadeBindingTests
{
    public static void Run()
    {
        string facade = RepoSource.Read("Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs");
        string hotfix = RepoSource.Read("Assets/FYAsset/Scripts/Compat/HotfixManager.cs");
        string flow = RepoSource.Read("Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs");
        string aa = RepoSource.Read("Assets/FYAsset/Scripts/AA/Hotfix/AAHotfixManager.cs");
        string ab = RepoSource.Read("Assets/FYAsset/Scripts/AB/Hotfix/ABHotfixManager.cs");

        RepoAssert.Contains(facade, "Bind(BackendMode mode)", "facade exposes explicit one-time binding");
        RepoAssert.Contains(facade, "RuntimeMessage", "facade reports binding/load failures as values");
        RepoAssert.NotContains(facade, "UseABBackend", "facade must not reread runtime settings");
        RepoAssert.NotContains(facade, "PackageManagerBase", "facade must not depend on a shared implementation base");
        RepoAssert.Contains(facade, "Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>",
            "async common load returns tuple");
        RepoAssert.Contains(facade, "(T asset, RuntimeMessage error) LoadAssetSync<T>",
            "sync common load returns tuple");
        RepoAssert.Contains(facade, "void UnloadAsset<T>", "common unload is typed");

        RepoAssert.Contains(hotfix, "InitializeAsync(BackendMode mode)", "hotfix selection is explicit");
        RepoAssert.NotContains(hotfix, "UseABBackend", "hotfix facade must not reread runtime settings");
        RepoAssert.Contains(flow, "RuntimeMessage bindError = BindPackageManager();",
            "facade binding must occur at the final completion boundary");
        int completion = flow.IndexOf("private void CompleteInitialization()", System.StringComparison.Ordinal);
        int bind = flow.IndexOf("BindPackageManager();", completion, System.StringComparison.Ordinal);
        int finished = flow.IndexOf("OnFinished?.Invoke();", completion, System.StringComparison.Ordinal);
        RepoAssert.True(completion >= 0 && bind > completion && finished > bind,
            "binding must happen inside CompleteInitialization before OnFinished");
        // aa-ab-decoupling 后：后端不再绑定 Compat facade（单后端导出集无绑定语义），
        // 基类 BindPackageManager 降为 virtual 默认 null；跨后端互斥依旧由 facade 自身保障（下方仍测）。
        RepoAssert.NotContains(aa, "AssetPackageManager",
            "AA must not reference the Compat facade");
        RepoAssert.NotContains(ab, "AssetPackageManager",
            "AB must not reference the Compat facade");
        RepoAssert.Contains(flow, "protected virtual RuntimeMessage BindPackageManager()",
            "base flow provides the default no-op binding hook");
        RepoAssert.True(flow.IndexOf("return null;", flow.IndexOf("protected virtual RuntimeMessage BindPackageManager()", System.StringComparison.Ordinal), System.StringComparison.Ordinal) >= 0,
            "default binding hook must be a no-op");

        var instance = new AssetPackageManager();
        var (_, preBindError) = instance.LoadAssetSync<FacadeAsset>("before-bind");
        RepoAssert.True(preBindError != null, "load before binding must return an error");
        RepoAssert.True(instance.Bind(BackendMode.AA) == null, "first bind must succeed");
        RepoAssert.True(instance.Bind(BackendMode.AA) == null, "same-mode rebind must succeed");
        RepoAssert.True(instance.Bind(BackendMode.ABManifest) != null, "cross-mode rebind must fail");

        AAPackageManager.LastAddress = null;
        ABPackageManager.LastAddress = null;
        instance.LoadAssetSync<FacadeAsset>("still-aa");
        RepoAssert.Equal("still-aa", AAPackageManager.LastAddress,
            "failed cross-mode rebind must preserve the original mode");
        RepoAssert.True(ABPackageManager.LastAddress == null,
            "failed cross-mode rebind must not route to AB");
    }
}

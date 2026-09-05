internal static class StartupLoadErrorTests
{
    public static void Run()
    {
        string ui = RepoSource.Read("Assets/Game/Scripts/Game/DialogueGame/GameUIManager.cs");
        string launcher = RepoSource.Read("Assets/Global/Scripts/GameLauncher.cs");
        string lua = RepoSource.Read("Assets/XLuaFramework/Scripts/XLuaLoader/XLuaLoader.cs");
        string dialogue = RepoSource.Read("Assets/Dialogue/Scripts/Dialogue/CsharpOnly/DialogueDataManager.cs");

        RepoAssert.Contains(ui, "var (config, error)", "UI startup consumes the facade tuple");
        RepoAssert.Contains(ui, "throw new", "missing UIResourceConfigSO must abort startup");
        RepoAssert.Contains(lua, "var (indexSO, error)", "Lua index startup consumes structured failure");
        RepoAssert.NotContains(lua, "ReleaseScriptCacheByLabel", "dead runtime label-cache entry point is removed");
        RepoAssert.NotContains(launcher, "XluaTypeConfigLoader.InitAsync", "unused runtime type config chain is disconnected");
        RepoAssert.False(RepoSource.Exists("Assets/XLuaFramework/Scripts/LabelBatchManagement/XluaTypeConfigLoader.cs"),
            "unused runtime type config loader is removed");

        RepoAssert.NotContains(dialogue, "Addressables.",
            "Dialogue runtime must not bypass the selected asset facade");
        RepoAssert.Contains(dialogue, "AssetPackageManager.Instance.LoadAssetSync<TextAsset>",
            "Dialogue runtime loads TextAsset synchronously through the facade");
        RepoAssert.Contains(dialogue, "UnloadAsset<TextAsset>",
            "Dialogue runtime releases TextAsset through the facade");
        RepoAssert.NotContains(ui, "Addressables", "S2 does not add a new UI Addressables path");
    }
}

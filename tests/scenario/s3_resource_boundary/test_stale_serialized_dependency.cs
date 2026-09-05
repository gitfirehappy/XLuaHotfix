using System;
using System.IO;

internal static class StaleSerializedDependencyTests
{
    public static void Run()
    {
        VerifyLuaBehaviourAssetsHaveNoRemovedTextAssetField();
        VerifyLuaScriptConfigUsesOnlyModuleIdentity();
        VerifyPlayerConfigurationValuesRemainIntact();
    }

    private static void VerifyLuaBehaviourAssetsHaveNoRemovedTextAssetField()
    {
        foreach (string path in RepoSource.EnumerateFiles(
                     "Assets/SO/Bridge/LuaBehaviourConfigSO", "*.asset"))
        {
            string source = File.ReadAllText(path);
            RepoAssert.NotContains(source, "luaScript:",
                $"removed LuaScriptConfig field must not remain serialized: {RepoSource.ToRelative(path)}");
        }
    }

    private static void VerifyLuaScriptConfigUsesOnlyModuleIdentity()
    {
        string source = RepoSource.Read("Assets/XLuaFramework/Scripts/Bridge/Utils/LuaScriptConfig.cs");
        RepoAssert.NotContains(source, "TextAsset", "LuaScriptConfig must not advertise or store a TextAsset fallback");
        RepoAssert.Contains(source, "return luaScriptName;", "LuaScriptConfig must resolve by module name only");
    }

    private static void VerifyPlayerConfigurationValuesRemainIntact()
    {
        string source = RepoSource.Read("Assets/SO/Bridge/LuaBehaviourConfigSO/Player_LuaBridge.asset");
        RepoAssert.Contains(source, "luaScriptName: PlayerController",
            "Player bridge module identity must be preserved");
        RepoAssert.Contains(source, "luaScriptMode: 0", "Player bridge Class mode must be preserved");
    }
}

using System;
using System.Collections.Generic;
using System.IO;

internal static class UpperPackageBoundaryTests
{
    public static void Run()
    {
        VerifyPackageNeutralLoaderContract();
        VerifyUpperRuntimeUsesOnlyFacade();
        VerifySerializedModeCompatibility();
        VerifyUserFacingNamesAreBackendNeutral();
    }

    private static void VerifyPackageNeutralLoaderContract()
    {
        string loader = RepoSource.Read("Assets/XLuaFramework/Scripts/XLuaLoader/XLuaLoader.cs");
        RepoAssert.Contains(loader, "EditorOnly = 0", "EditorOnly value must be explicit");
        RepoAssert.Contains(loader, "PackageOnly = 1", "PackageOnly must preserve serialized value 1");
        RepoAssert.Contains(loader, "Hybrid = 2", "Hybrid value must remain 2");
        RepoAssert.NotContains(loader, "AddressablesOnly", "legacy AddressablesOnly name must be removed");
        RepoAssert.NotContains(loader, "ContainersAALabels", "dead AA label option must be removed");
        RepoAssert.NotContains(loader, "LoadFromAddressablesSync", "loader helper must use package-neutral naming");
        RepoAssert.NotContains(loader, "containerAAKey", "container parameter must use package-neutral naming");
        RepoAssert.NotContains(loader, "ResourceLocations", "unused Addressables resource-location import must be removed");
    }

    private static void VerifyUpperRuntimeUsesOnlyFacade()
    {
        var roots = new[]
        {
            "Assets/XLuaFramework/Scripts",
            "Assets/UI/Scripts",
            "Assets/Game/Scripts",
            "Assets/Global/Scripts",
            "Assets/Dialogue/Scripts"
        };

        int facadeCalls = 0;
        for (int r = 0; r < roots.Length; r++)
        {
            foreach (string path in RepoSource.EnumerateFiles(roots[r], "*.cs"))
            {
                string relative = RepoSource.ToRelative(path);
                if (relative.Contains("/Editor/", StringComparison.Ordinal)) continue;

                string source = File.ReadAllText(path);
                RepoAssert.NotContains(source, "Addressables.", $"upper runtime must not call Addressables directly: {relative}");
                RepoAssert.NotContains(source, "AAPackageManager", $"upper runtime must not bind AA concrete manager: {relative}");
                RepoAssert.NotContains(source, "ABPackageManager", $"upper runtime must not bind AB concrete manager: {relative}");
                if (source.Contains("AssetPackageManager.Instance", StringComparison.Ordinal)) facadeCalls++;
            }
        }

        // xluaframework-export 接缝落地后：XLuaFramework 一律走 LuaAssetRuntime 服务口（facade 零引用由
        // XLuaFrameworkBoundary 门禁保证），上层项目壳（UI/Game/Dialogue）保持 thin facade 消费。
        RepoAssert.True(facadeCalls >= 2, "UI/Game/Dialogue callers must keep using the thin facade");
    }

    private static void VerifySerializedModeCompatibility()
    {
        string prefab = RepoSource.Read("Assets/Prefab/GameLauncher.prefab");
        string scene = RepoSource.Read("Assets/Scenes/Xlua/Test.unity");
        RepoAssert.Contains(prefab, "loaderMode: 2", "GameLauncher Hybrid serialized value must remain 2");
        RepoAssert.Contains(scene, "testMode: 1", "XLuaLoaderTester package mode serialized value must remain 1");
        RepoAssert.NotContains(prefab, "xluaConfigLabel:", "removed XLua config label field must not remain serialized");
        RepoAssert.NotContains(prefab, "aaLabels:", "removed GameLauncher AA labels must not remain serialized");
        RepoAssert.NotContains(scene, "aaLabels:", "removed tester AA labels must not remain serialized");
    }

    private static void VerifyUserFacingNamesAreBackendNeutral()
    {
        var files = new[]
        {
            "Assets/Game/Scripts/Game/DialogueGame/GameUIManager.cs",
            "Assets/XLuaFramework/Scripts/Bridge/ScriptObjectBridge.cs",
            "Assets/XLuaFramework/Scripts/Bridge/ScriptObjectBridgeConfig.cs",
            "Assets/XLuaFramework/Scripts/Bridge/Anime/AnimBridge.cs",
            "Assets/XLuaFramework/Scripts/Bridge/Editor/LuaBehaviourBridgeEditor.cs",
            "Assets/Test/XLuaLoaderTester.cs"
        };

        for (int i = 0; i < files.Length; i++)
        {
            string source = RepoSource.Read(files[i]);
            RepoAssert.NotContains(source, "Addressable Key", $"user-facing key name must be package-neutral: {files[i]}");
            RepoAssert.NotContains(source, "AA Key", $"user-facing key name must be package-neutral: {files[i]}");
            RepoAssert.NotContains(source, "AddressablesOnly", $"test/runtime enum name must be package-neutral: {files[i]}");
            RepoAssert.NotContains(source, "aaLabels", $"dead AA label options must be removed: {files[i]}");
        }

        string uiManager = RepoSource.Read("Assets/UI/Scripts/UI/BasicTemplate/UIManager.cs");
        RepoAssert.NotContains(uiManager, "UnityEngine.AddressableAssets", "UIManager must not import Addressables");
        RepoAssert.NotContains(uiManager, "UnityEngine.ResourceManagement", "UIManager must not import Addressables handles");
    }
}

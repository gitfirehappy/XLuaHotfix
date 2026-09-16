using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>AB 公共索引与句柄类别契约。</summary>
internal static class ABPublicIndexAndHandleTests
{
    public static void PublicIndexFiltersImplicitEntries()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("Dialogue", "LuaScriptContainer", 0, "ui"));

        var index = new ABAssetIndex(manifest);
        ScenarioAssert.True(index.IsValid, "a manifest without duplicate addresses must build an index");

        ManifestAssetEntry byAddress = index.GetEntryByAddress("Dialogue");
        ScenarioAssert.True(byAddress != null && byAddress.Address == "Dialogue",
            "the address index must resolve the public entry");
        ScenarioAssert.True(index.GetEntryByAddress("implicit") == null,
            "implicit dependencies must not be represented as public manifest entries");

        IReadOnlyList<string> addressesByType = index.GetAddressesByType("LuaScriptContainer");
        ScenarioAssert.Equal(1, addressesByType.Count, "type queries must return public addresses only");
        ScenarioAssert.Equal("Dialogue", addressesByType[0], "type query must return the public address");

        IReadOnlyList<string> addressesByLabel = index.GetAddressesByLabel("ui");
        ScenarioAssert.Equal(1, addressesByLabel.Count, "label queries must return public addresses only");
        ScenarioAssert.Equal("Dialogue", addressesByLabel[0], "label query must return the public address");

        IReadOnlyList<ManifestAssetEntry> entriesByType = index.GetEntriesByType("LuaScriptContainer");
        ScenarioAssert.Equal(1, entriesByType.Count, "type entry queries must return public manifest entries");
    }

    public static void AddressLookupIsCaseInsensitive()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("Dialogue", "LuaScriptContainer", 0));

        var index = new ABAssetIndex(manifest);
        ScenarioAssert.True(index.ContainsAddress("dialogue") && index.ContainsAddress("DIALOGUE"),
            "address membership must be case insensitive");
        ScenarioAssert.True(index.GetEntryByAddress("dIaLoGuE")?.Address == "Dialogue",
            "address lookup must be case insensitive");
        ScenarioAssert.True(index.GetAddressesByType("luascriptcontainer").Count == 1,
            "type lookup must be case insensitive");
    }

    public static void DuplicatePublicAddressFailsConstruction()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("Dialogue", "LuaScriptContainer", 0))
            .AddAsset(Entry("dialogue", "LuaScriptContainer", 0));

        var index = new ABAssetIndex(manifest);
        ScenarioAssert.False(index.IsValid, "duplicate public addresses must fail index construction");
        ScenarioAssert.Equal(RuntimeErrorCodes.DuplicateAddress, index.BuildError?.Code,
            "duplicate public addresses must report a structured DuplicateAddress error");
        ScenarioAssert.True(index.GetEntryByAddress("Dialogue") == null,
            "a rejected index must not expose a partial address index");
        ScenarioAssert.Equal(0, index.GetAddressesByType("LuaScriptContainer").Count,
            "a rejected index must not expose a partial type index");
    }

    public static void SceneTokensCountTowardShutdownGate()
    {
        ResetRegistry();
        try
        {
            (int sceneToken, int sceneGeneration) = HandleRegistry.Alloc(
                "scene.address", HandleKind.Scene, null, null);
            var sink = new FakeSceneUnloadSink();
            var sceneHandle = new SceneHandle(
                sceneToken, sceneGeneration, "Battle", "Assets/Scenes/Battle.unity", default, sink);

            (int assetToken, int assetGeneration) = HandleRegistry.Alloc(
                "asset.address", HandleKind.Asset, null, _ => { });
            var assetHandle = new AssetHandle<UnityEngine.Object>(assetToken, assetGeneration, new UnityEngine.Object());

            ScenarioAssert.Equal(1, HandleRegistry.SceneActiveCount, "scene tokens must be counted separately");
            ScenarioAssert.Equal(1, HandleRegistry.AssetActiveCount, "asset tokens must be counted separately");
            ScenarioAssert.Equal(2, HandleRegistry.ActiveCount, "shutdown must count asset and scene tokens together");
            ScenarioAssert.False(HandleRegistry.Reset(), "Reset must refuse while any token is alive");

            SceneHandle retainedScene = sceneHandle.Retain();
            ScenarioAssert.True(retainedScene.IsValid, "Retain must allocate an independent scene token");
            ScenarioAssert.Equal(2, HandleRegistry.SceneActiveCount, "retained scene must be counted");

            sceneHandle.ReleaseAsync().GetAwaiter().GetResult();
            ScenarioAssert.False(sceneHandle.IsValid, "releasing a non-last scene owner must consume its token");
            ScenarioAssert.True(retainedScene.IsValid, "releasing one scene token must keep the retained token");
            retainedScene.ReleaseAsync().GetAwaiter().GetResult();
            ScenarioAssert.Equal(0, HandleRegistry.SceneActiveCount, "all scene tokens must be released");

            assetHandle.Release();
            ScenarioAssert.Equal(0, HandleRegistry.ActiveCount, "all tokens must be released before shutdown");
            ScenarioAssert.True(HandleRegistry.Reset(), "Reset must succeed once every token is released");
        }
        finally
        {
            HandleRegistry.Reset();
        }
    }

    public static void DefaultSceneHandleIsInvalid()
    {
        ResetRegistry();
        try
        {
            SceneHandle uninitialized = default;
            ScenarioAssert.False(uninitialized.IsValid, "default(SceneHandle) must never be valid");
            ScenarioAssert.True(uninitialized.Scene.path == null, "default(SceneHandle) must not expose a scene");
            uninitialized.ReleaseAsync().GetAwaiter().GetResult();
            ScenarioAssert.Equal(0, HandleRegistry.ActiveCount, "default release must not consume a token");

            SceneHandle failed = new SceneHandle(
                RuntimeMessage.SceneLoadFailed("scene.address", "configured failure"), "Battle", null);
            ScenarioAssert.False(failed.IsValid, "a failed scene handle must not be valid");
            ScenarioAssert.Equal(RuntimeErrorCodes.SceneLoadFailed, failed.Error?.Code,
                "a failed scene handle must keep its structured error");
            ScenarioAssert.True(failed.Retain().Error?.Code == RuntimeErrorCodes.SceneLoadFailed,
                "retaining a failed scene handle must preserve the original error");
        }
        finally
        {
            HandleRegistry.Reset();
        }
    }

    public static void SceneLoaderReleasesContentAfterUnload()
    {
        string loader = ScenarioSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs");
        ScenarioAssert.Contains(loader, "HandleKind.Scene", "scene tokens must use the Scene handle kind");
        ScenarioAssert.Contains(loader, "GetAllScenePaths", "scene paths must come from AssetBundle.GetAllScenePaths");
        ScenarioAssert.Contains(loader, "await WaitForOperationAsync(operation)",
            "scene unload must wait for the asynchronous operation");
        ScenarioAssert.Contains(loader, "ReleaseContentReference(record.ContentFileName)",
            "content references must be released after scene unload");
        ScenarioAssert.Contains(loader, "HandleRegistry.ReleaseAllForEntry(record.Address)",
            "single-mode replacement must settle every token for the replaced address");
    }

    private static void ResetRegistry()
    {
        HandleRegistry.Reset();
        ScenarioAssert.Equal(0, HandleRegistry.ActiveCount, "precondition: registry must start empty");
    }

    private static ManifestAssetEntry Entry(string address, string assetType, int contentIndex, params string[] labels)
    {
        var entry = new ManifestAssetEntry
        {
            Address = address,
            AssetType = assetType,
            AssetPath = "Assets/Test/" + address + ".asset",
            ContentIndex = contentIndex
        };
        entry.Labels.AddRange(labels);
        return entry;
    }
}

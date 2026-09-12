using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AB 公共索引与句柄类别契约。
/// 验证 Public 条目筛选、Address 唯一性、重复 Address 失败，以及 Scene token 参与统一门禁。
/// </summary>
internal static class ABPublicIndexAndHandleTests
{
    public static void PublicIndexFiltersImplicitEntries()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("entry.public", "Dialogue", "LuaScriptContainer", true, 0, "ui"))
            .AddAsset(Entry("entry.implicit", "Dialogue", "LuaScriptContainer", false, 0, "ui"));

        var index = new ABAssetIndex(manifest);
        ScenarioAssert.True(index.IsValid, "a manifest without duplicate public addresses must build an index");

        RuntimeAssetEntry byAddress = index.GetEntryByAddress("Dialogue");
        ScenarioAssert.True(byAddress != null && byAddress.EntryId == "entry.public",
            "the address index must only contain the public entry");

        ScenarioAssert.True(index.GetEntryById("entry.implicit") != null,
            "implicit entries must stay reachable by EntryId for loading and dependencies");

        IReadOnlyList<string> addressesByType = index.GetAddressesByType("LuaScriptContainer");
        ScenarioAssert.Equal(1, addressesByType.Count,
            "type queries must return public addresses only");
        ScenarioAssert.Equal("Dialogue", addressesByType[0], "type query must return the public address");

        IReadOnlyList<string> addressesByLabel = index.GetAddressesByLabel("ui");
        ScenarioAssert.Equal(1, addressesByLabel.Count,
            "label queries must return public addresses only");
        ScenarioAssert.Equal("Dialogue", addressesByLabel[0], "label query must return the public address");

        IReadOnlyList<RuntimeAssetEntry> entriesByType = index.GetEntriesByType("LuaScriptContainer");
        ScenarioAssert.Equal(1, entriesByType.Count, "type entry queries must skip implicit entries");

        ScenarioAssert.True(index.GetEntryById("missing") == null,
            "unknown EntryId must resolve to null");
    }

    public static void AddressLookupIsCaseInsensitive()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("entry.public", "Dialogue", "LuaScriptContainer", true, 0));

        var index = new ABAssetIndex(manifest);

        ScenarioAssert.True(index.ContainsAddress("dialogue") && index.ContainsAddress("DIALOGUE"),
            "address membership must be case insensitive");
        ScenarioAssert.True(index.GetEntryByAddress("dIaLoGuE")?.EntryId == "entry.public",
            "address lookup must be case insensitive");
        ScenarioAssert.True(index.GetAddressesByType("luascriptcontainer").Count == 1,
            "type lookup must be case insensitive");
    }

    public static void DuplicatePublicAddressFailsConstruction()
    {
        var manifest = new ABManifest()
            .AddContent("dialogue.content", AssetContentType.SerializedObject)
            .AddAsset(Entry("entry.a", "Dialogue", "LuaScriptContainer", true, 0))
            .AddAsset(Entry("entry.b", "dialogue", "LuaScriptContainer", true, 0));

        var index = new ABAssetIndex(manifest);

        ScenarioAssert.False(index.IsValid, "duplicate public addresses must fail index construction");
        ScenarioAssert.Equal(RuntimeErrorCodes.DuplicateAddress, index.BuildError?.Code,
            "duplicate public addresses must report a structured DuplicateAddress error");
        ScenarioAssert.True(index.GetEntryByAddress("Dialogue") == null,
            "a rejected index must not expose a partial address index");
        ScenarioAssert.Equal(0, index.GetAddressesByType("LuaScriptContainer").Count,
            "a rejected index must not expose a partial type index");
        ScenarioAssert.Equal(0, index.GetAddressesByLabel("ui").Count,
            "a rejected index must not expose a partial label index");
    }

    public static void SceneTokensCountTowardShutdownGate()
    {
        ResetRegistry();
        try
        {
            (int sceneToken, int sceneGeneration) = HandleRegistry.Alloc(
                "scene.entry", HandleKind.Scene, "battle.content", null, null);
            var sceneHandle = new SceneHandle(
                sceneToken, sceneGeneration, "Battle", "Assets/Scenes/Battle.unity", default, null);

            (int assetToken, int assetGeneration) = HandleRegistry.Alloc(
                "asset.entry", HandleKind.Asset, "asset.content", null, _ => { });
            var assetHandle = new AssetHandle<UnityEngine.Object>(assetToken, assetGeneration, new UnityEngine.Object());

            ScenarioAssert.Equal(1, HandleRegistry.SceneActiveCount,
                "scene tokens must be counted separately for diagnostics");
            ScenarioAssert.Equal(1, HandleRegistry.AssetActiveCount,
                "asset tokens must be counted separately for diagnostics");
            ScenarioAssert.Equal(2, HandleRegistry.ActiveCount,
                "the shutdown gate must count asset and scene tokens together");

            ScenarioAssert.False(HandleRegistry.Reset(),
                "Reset must refuse while any asset or scene token is alive");
            ScenarioAssert.True(sceneHandle.IsValid && assetHandle.IsValid,
                "a refused Reset must keep existing handles valid");

            SceneHandle retainedScene = sceneHandle.Retain();
            ScenarioAssert.True(retainedScene.IsValid, "Retain must allocate an independent scene token");
            ScenarioAssert.Equal(2, HandleRegistry.SceneActiveCount,
                "the retained scene token must be counted as a separate owner");

            sceneHandle.Release();
            ScenarioAssert.True(retainedScene.IsValid,
                "releasing the original scene token must not invalidate the retained token");
            ScenarioAssert.Equal(1, HandleRegistry.SceneActiveCount,
                "only the released scene token may leave the activation count");

            retainedScene.Release();
            retainedScene.Release();
            ScenarioAssert.Equal(0, HandleRegistry.SceneActiveCount,
                "duplicate scene token release must be an idempotent no-op");

            assetHandle.Release();
            ScenarioAssert.Equal(0, HandleRegistry.ActiveCount,
                "all tokens must be released before the shutdown gate opens");
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
            ScenarioAssert.True(uninitialized.Scene.path == null,
                "default(SceneHandle) must not expose a scene");
            uninitialized.Release();
            ScenarioAssert.Equal(0, HandleRegistry.ActiveCount,
                "releasing a default scene handle must not consume any token");

            SceneHandle failed = new SceneHandle(
                RuntimeMessage.SceneLoadFailed("scene.entry", "configured failure"),
                "Battle",
                null);
            ScenarioAssert.False(failed.IsValid, "a failed scene handle must not be valid");
            ScenarioAssert.Equal(RuntimeErrorCodes.SceneLoadFailed, failed.Error?.Code,
                "a failed scene handle must keep its structured error");
            ScenarioAssert.True(failed.Retain().Error?.Code == RuntimeErrorCodes.SceneLoadFailed,
                "retaining a failed scene handle must preserve the original error");
            failed.Release();
            ScenarioAssert.Equal(0, HandleRegistry.ActiveCount,
                "releasing a failed scene handle must not consume any token");
        }
        finally
        {
            HandleRegistry.Reset();
        }
    }

    /// <summary>
    /// Bundle 释放顺序契约：Scene 卸载完成之后才允许释放内容引用。
    /// 该顺序只能在 Unity 实测中运行验证，这里锁定源码中的调用先后关系。
    /// </summary>
    public static void SceneLoaderReleasesContentAfterUnload()
    {
        string loader = ScenarioSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs");

        ScenarioAssert.Contains(loader, "HandleKind.Scene",
            "scene tokens must be allocated with the Scene handle kind");
        ScenarioAssert.Contains(loader, "GetAllScenePaths",
            "scene paths must come from AssetBundle.GetAllScenePaths");

        int unloadSceneCall = loader.IndexOf("SceneManager.UnloadSceneAsync(scene)", StringComparison.Ordinal);
        int releaseAfterUnload = loader.IndexOf("ReleaseContent(scenePath)", StringComparison.Ordinal);
        ScenarioAssert.True(unloadSceneCall >= 0 && releaseAfterUnload > unloadSceneCall,
            "Additive unload must finish the scene unload before releasing content");

        int settleWait = loader.IndexOf("WaitForSceneUnloadAsync(record.Scene)", StringComparison.Ordinal);
        int settleRelease = loader.IndexOf("ReleaseContentReference(record.ContentFileName)", StringComparison.Ordinal);
        ScenarioAssert.True(settleWait >= 0 && settleRelease > settleWait,
            "single-mode replacement must confirm the old scene unload before settling its handle");

        ScenarioAssert.Contains(loader, "HandleRegistry.ReleaseAllForEntry(record.EntryId)",
            "single-mode replacement must settle every live token of the replaced scene entry");
        ScenarioAssert.Contains(loader, "Resources.UnloadUnusedAssets()",
            "unused-asset collection must stay an explicit scene-switch step");
    }

    private static void ResetRegistry()
    {
        // 门禁前置清理：上一次失败留下的 token 不得影响本次断言
        HandleRegistry.Reset();
        ScenarioAssert.Equal(0, HandleRegistry.ActiveCount, "precondition: registry must start empty");
    }

    private static ManifestAssetEntry Entry(
        string entryId,
        string address,
        string primaryType,
        bool isPublic,
        int contentIndex,
        params string[] labels)
    {
        var entry = new ManifestAssetEntry
        {
            EntryId = entryId,
            Address = address,
            PrimaryType = primaryType,
            SourcePath = "Assets/Test/" + entryId + ".asset",
            IsPublic = isPublic,
            ContentType = AssetContentType.SerializedObject,
            ContentIndex = contentIndex
        };
        entry.Labels.AddRange(labels);
        return entry;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

internal static class DialogueFacadeLoadingTests
{
    public static Task LoadsCachesAndUnloadsThroughFacade()
    {
        AssetPackageManager.Reset();
        DialogueCsvReader.Reset();
        FakeAddressables.Reset();

        const string address = "dialogue-facade-success";
        var csvAsset = new TextAsset("valid dialogue");
        var firstParsed = new List<DialogueData> { new DialogueData() };
        var reloadedParsed = new List<DialogueData> { new DialogueData(), new DialogueData() };

        AssetPackageManager.Register(address, csvAsset);
        FakeAddressables.Register(address, csvAsset);
        DialogueCsvReader.EnqueueResult(firstParsed);
        DialogueCsvReader.EnqueueResult(reloadedParsed);

        List<DialogueData> first = DialogueDataManager.LoadDialogueData(address);
        List<DialogueData> cached = DialogueDataManager.LoadDialogueData(address);
        DialogueDataManager.UnloadDialogue(address);
        List<DialogueData> reloaded = DialogueDataManager.LoadDialogueData(address);
        DialogueDataManager.UnloadDialogue(address);

        ScenarioAssert.Same(firstParsed, first,
            "a successful facade TextAsset load must return the parsed dialogue data");
        ScenarioAssert.Same(first, cached,
            "a second request for the same dialogue key must reuse the parsed cache");
        ScenarioAssert.Same(reloadedParsed, reloaded,
            "loading after unload must parse the facade asset again");
        ScenarioAssert.Equal(2, AssetPackageManager.LoadCalls<TextAsset>(address),
            "cache hit must skip facade load, while reload after unload must load again");
        ScenarioAssert.Equal(2, DialogueCsvReader.ParseCalls,
            "cache hit must skip parsing, while reload after unload must parse again");
        ScenarioAssert.Equal(2, AssetPackageManager.UnloadCalls<TextAsset>(address),
            "each cached dialogue lifetime must release one typed facade asset");
        ScenarioAssert.Equal(address, AssetPackageManager.LastLoadAddress,
            "dialogue load must preserve the requested facade address");
        ScenarioAssert.Equal(typeof(TextAsset), AssetPackageManager.LastLoadType,
            "dialogue load must request TextAsset from the facade");
        ScenarioAssert.Equal(address, AssetPackageManager.LastUnloadAddress,
            "dialogue unload must preserve the cached facade address");
        ScenarioAssert.Equal(typeof(TextAsset), AssetPackageManager.LastUnloadType,
            "dialogue unload must release TextAsset through the facade");

        VerifyFacadeFailureRetriesWithoutParseOrUnload();
        return Task.CompletedTask;
    }

    public static Task ParseFailureReleasesFacadeAsset()
    {
        VerifyParseFailureRetries(new List<DialogueData>(), "dialogue-facade-empty-parse");
        VerifyParseFailureRetries(null, "dialogue-facade-null-parse");
        VerifyParseExceptionReleasesAndAllowsRetry();
        return Task.CompletedTask;
    }

    public static Task SourceBoundaryUsesOnlyFacade()
    {
        string source = ScenarioSource.Read(
            "Assets/Dialogue/Scripts/Dialogue/CsharpOnly/DialogueDataManager.cs");

        ScenarioAssert.NotContains(source, "UnityEngine.AddressableAssets",
            "DialogueDataManager must not import the Addressables namespace");
        ScenarioAssert.NotContains(source, "AsyncOperationHandle",
            "DialogueDataManager must not retain backend-specific Addressables handles");
        ScenarioAssert.NotContains(source, "Addressables.",
            "DialogueDataManager must not call Addressables directly");
        ScenarioAssert.NotContains(source, "LoaderMode",
            "DialogueDataManager must not keep the historical backend switch");
        ScenarioAssert.NotContains(source, "LoadDialogueDataIntegrated",
            "DialogueDataManager must not keep the unimplemented integrated branch");
        ScenarioAssert.NotContains(source, "_standaloneHandles",
            "DialogueDataManager must not keep standalone handle state");
        ScenarioAssert.NotContains(source, "_integratedAssets",
            "DialogueDataManager must not keep integrated backend state");
        ScenarioAssert.Contains(source, "AssetPackageManager.Instance.LoadAssetSync<TextAsset>",
            "DialogueDataManager must synchronously load TextAsset through the thin facade");
        ScenarioAssert.Contains(source, "UnloadAsset<TextAsset>",
            "DialogueDataManager must release TextAsset through the thin facade");
        return Task.CompletedTask;
    }

    private static void VerifyParseFailureRetries(List<DialogueData> failedResult, string address)
    {
        AssetPackageManager.Reset();
        DialogueCsvReader.Reset();
        FakeAddressables.Reset();

        var csvAsset = new TextAsset("invalid then valid dialogue");
        var successfulRetry = new List<DialogueData> { new DialogueData() };

        AssetPackageManager.Register(address, csvAsset);
        FakeAddressables.Register(address, csvAsset);
        DialogueCsvReader.EnqueueResult(failedResult);
        DialogueCsvReader.EnqueueResult(successfulRetry);

        List<DialogueData> first = DialogueDataManager.LoadDialogueData(address);
        List<DialogueData> retry = DialogueDataManager.LoadDialogueData(address);
        DialogueDataManager.UnloadDialogue(address);

        ScenarioAssert.Same(failedResult, first,
            "parse failure should preserve the parser result for the synchronous business API");
        ScenarioAssert.Same(successfulRetry, retry,
            "parse failure must not populate the cache, so the next request can retry");
        ScenarioAssert.Equal(2, AssetPackageManager.LoadCalls<TextAsset>(address),
            "a request after parse failure must load the TextAsset again");
        ScenarioAssert.Equal(2, DialogueCsvReader.ParseCalls,
            "a request after parse failure must parse again");
        ScenarioAssert.Equal(2, AssetPackageManager.UnloadCalls<TextAsset>(address),
            "parse failure and the later successful cached lifetime must each release once");
    }

    private static void VerifyFacadeFailureRetriesWithoutParseOrUnload()
    {
        AssetPackageManager.Reset();
        DialogueCsvReader.Reset();
        FakeAddressables.Reset();

        const string address = "dialogue-facade-load-error";
        var legacyAsset = new TextAsset("legacy Addressables path must not be used");
        RuntimeMessage error = RuntimeMessage.LoadFailed(address, "configured facade failure");

        AssetPackageManager.Register<TextAsset>(address, null, error);
        FakeAddressables.Register(address, legacyAsset);
        DialogueCsvReader.EnqueueResult(new List<DialogueData> { new DialogueData() });

        List<DialogueData> first = DialogueDataManager.LoadDialogueData(address);
        List<DialogueData> retry = DialogueDataManager.LoadDialogueData(address);
        DialogueDataManager.UnloadDialogue(address);

        ScenarioAssert.True(first == null && retry == null,
            "a facade load error must return no parsed dialogue data");
        ScenarioAssert.Equal(2, AssetPackageManager.LoadCalls<TextAsset>(address),
            "facade load errors must not populate the cache, so the next request retries");
        ScenarioAssert.Equal(0, DialogueCsvReader.ParseCalls,
            "a facade load error must not invoke the CSV parser");
        ScenarioAssert.Equal(0, AssetPackageManager.UnloadCalls<TextAsset>(address),
            "a facade load error owns no successful ticket and must not unload");
    }

    private static void VerifyParseExceptionReleasesAndAllowsRetry()
    {
        AssetPackageManager.Reset();
        DialogueCsvReader.Reset();
        FakeAddressables.Reset();

        const string address = "dialogue-facade-parse-exception";
        var csvAsset = new TextAsset("throw then retry");
        var successfulRetry = new List<DialogueData> { new DialogueData() };

        AssetPackageManager.Register(address, csvAsset);
        FakeAddressables.Register(address, csvAsset);
        DialogueCsvReader.ThrowNext(new InvalidOperationException("configured parse failure"));

        bool exceptionObserved = false;
        try
        {
            DialogueDataManager.LoadDialogueData(address);
        }
        catch (InvalidOperationException)
        {
            exceptionObserved = true;
        }

        ScenarioAssert.True(exceptionObserved,
            "facade migration must preserve the existing parser exception behavior");
        ScenarioAssert.Equal(1, AssetPackageManager.UnloadCalls<TextAsset>(address),
            "a parser exception after successful load must release the facade ticket");

        DialogueCsvReader.EnqueueResult(successfulRetry);
        List<DialogueData> retry = DialogueDataManager.LoadDialogueData(address);
        DialogueDataManager.UnloadDialogue(address);

        ScenarioAssert.Same(successfulRetry, retry,
            "a parser exception must not cache the failed dialogue key");
        ScenarioAssert.Equal(2, AssetPackageManager.LoadCalls<TextAsset>(address),
            "retry after parser exception must perform a fresh facade load");
        ScenarioAssert.Equal(2, AssetPackageManager.UnloadCalls<TextAsset>(address),
            "the exception cleanup and later cached lifetime must each release once");
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class DialogueDataManager
{
    public enum LoaderMode { Standalone, Integrated }

    public static LoaderMode Mode = LoaderMode.Standalone;

    private static Dictionary<string, List<DialogueData>> _loadedDialogues = new();

    private static Dictionary<string, AsyncOperationHandle<TextAsset>> _standaloneHandles = new();

    private static Dictionary<string, TextAsset> _integratedAssets = new();

    public static List<DialogueData> LoadDialogueData(string csvFileName)
    {
        if (string.IsNullOrEmpty(csvFileName))
        {
            Debug.LogError("[DialogueDataManager] CSV文件名为空");
            return null;
        }

        if (_loadedDialogues.TryGetValue(csvFileName, out var cached))
        {
            return cached;
        }

        if (Mode == LoaderMode.Standalone)
        {
            return LoadDialogueDataStandalone(csvFileName);
        }
        else
        {
            return LoadDialogueDataIntegrated(csvFileName);
        }
    }

    private static List<DialogueData> LoadDialogueDataStandalone(string csvFileName)
    {
        var handle = Addressables.LoadAssetAsync<TextAsset>(csvFileName);
        handle.WaitForCompletion();

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError($"[DialogueDataManager] 未找到或加载失败 Addressables key: {csvFileName}");
            return null;
        }

        var csvAsset = handle.Result;
        var dialogueData = DialogueCsvReader.ParseCsv(csvAsset);

        if (dialogueData != null && dialogueData.Count > 0)
        {
            _loadedDialogues.Add(csvFileName, dialogueData);
            _standaloneHandles.Add(csvFileName, handle);
            Debug.Log($"[DialogueDataManager] 成功通过 Addressables 加载CSV对话：{csvFileName}（{dialogueData.Count}条）");
        }
        else
        {
            Debug.LogError($"[DialogueDataManager] 解析CSV失败：{csvFileName}");
            Addressables.Release(handle);
        }

        return dialogueData;
    }

    private static List<DialogueData> LoadDialogueDataIntegrated(string csvFileName)
    {
        var csvAsset = AAPackageManager.Instance.LoadAssetSync<TextAsset>(csvFileName);

        if (csvAsset == null)
        {
            Debug.LogError($"[DialogueDataManager] 通过 AAPackageManager 加载失败: {csvFileName}");
            return null;
        }

        var dialogueData = DialogueCsvReader.ParseCsv(csvAsset);

        if (dialogueData != null && dialogueData.Count > 0)
        {
            _loadedDialogues.Add(csvFileName, dialogueData);
            _integratedAssets.Add(csvFileName, csvAsset);
            Debug.Log($"[DialogueDataManager] 成功通过 AAPackageManager 加载CSV对话：{csvFileName}（{dialogueData.Count}条）");
        }

        return dialogueData;
    }

    public static List<DialogueData> LoadDialogueData(TextAsset csvAsset)
    {
        if (csvAsset == null)
        {
            Debug.LogError("[DialogueDataManager] CSV资源为空");
            return null;
        }

        return DialogueCsvReader.ParseCsv(csvAsset);
    }

    public static void UnloadDialogue(string csvName)
    {
        if (!_loadedDialogues.Remove(csvName))
        {
            return;
        }

        if (Mode == LoaderMode.Standalone && _standaloneHandles.TryGetValue(csvName, out var handle))
        {
            Addressables.Release(handle);
            _standaloneHandles.Remove(csvName);
            Debug.Log($"[DialogueDataManager] 释放 Addressables 句柄：{csvName}");
        }
        else if (Mode == LoaderMode.Integrated)
        {
            _integratedAssets.Remove(csvName);
            Debug.Log($"[DialogueDataManager] 卸载 Integrated 资源：{csvName}");
        }

        Debug.Log($"[DialogueDataManager] 卸载CSV对话：{csvName}");
    }
}
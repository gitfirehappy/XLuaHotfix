using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 对话数据管理器（缓存CSV加载的对话数据）
/// </summary>
public static class DialogueDataManager
{
    /// <summary>
    /// 缓存：CSV名称/Addressables key -> 对话数据列表
    /// </summary>
    private static Dictionary<string, List<DialogueData>> _loadedDialogues = new();

    /// <summary>
    /// 缓存：CSV名称/Addressables key -> AsyncOperationHandle（用于释放）
    /// </summary>
    private static Dictionary<string, AsyncOperationHandle<TextAsset>> _loadedHandles = new();

    /// <summary>
    /// 通过文件名加载CSV对话数据（文件名即Addressables key）
    /// </summary>
    public static List<DialogueData> LoadDialogueData(string csvFileName)
    {
        if (string.IsNullOrEmpty(csvFileName))
        {
            Debug.LogError("[DialogueDataManager] CSV文件名为空");
            return null;
        }

        if (_loadedDialogues.ContainsKey(csvFileName))
        {
            return _loadedDialogues[csvFileName];
        }

        // 使用 Addressables 按 key 加载 TextAsset（同步等待完成）
        var handle = Addressables.LoadAssetAsync<TextAsset>(csvFileName);
        handle.WaitForCompletion();

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError($"[DialogueDataManager] 未找到或加载失败 Addressables key: {csvFileName}");
            return null;
        }

        var csvAsset = handle.Result;

        // 解析CSV
        var dialogueData = DialogueCsvReader.ParseCsv(csvAsset);
        if (dialogueData != null && dialogueData.Count > 0)
        {
            _loadedDialogues.Add(csvFileName, dialogueData);
            _loadedHandles.Add(csvFileName, handle);
            Debug.Log($"[DialogueDataManager] 成功通过 Addressables 加载CSV对话：{csvFileName}（{dialogueData.Count}条）");
        }
        else
        {
            Debug.LogError($"[DialogueDataManager] 解析CSV失败：{csvFileName}");
            // 解析失败立即释放句柄
            Addressables.Release(handle);
        }

        return dialogueData;
    }

    /// <summary>
    /// 通过TextAsset加载CSV对话数据
    /// </summary>
    public static List<DialogueData> LoadDialogueData(TextAsset csvAsset)
    {
        if (csvAsset == null)
        {
            Debug.LogError("[DialogueDataManager] CSV资源为空");
            return null;
        }

        return DialogueCsvReader.ParseCsv(csvAsset);
    }

    /// <summary>
    /// 卸载指定CSV对话数据（释放对应的 Addressables 句柄）
    /// </summary>
    public static void UnloadDialogue(string csvName)
    {
        if (_loadedDialogues.ContainsKey(csvName))
        {
            _loadedDialogues.Remove(csvName);
            Debug.Log($"[DialogueDataManager] 卸载CSV对话：{csvName}");
        }

        if (_loadedHandles.ContainsKey(csvName))
        {
            var handle = _loadedHandles[csvName];
            Addressables.Release(handle);
            _loadedHandles.Remove(csvName);
            Debug.Log($"[DialogueDataManager] 释放 Addressables 句柄：{csvName}");
        }
    }
}
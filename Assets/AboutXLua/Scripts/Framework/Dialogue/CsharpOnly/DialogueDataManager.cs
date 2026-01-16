using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话数据管理器（缓存CSV加载的对话数据）
/// </summary>
public static class DialogueDataManager
{
    /// <summary>
    /// 缓存：CSV名称 -> 对话数据列表
    /// </summary>
    private static Dictionary<string, List<DialogueData>> _loadedDialogues = new();

    /// <summary>
    /// 通过文件名加载CSV对话数据
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
        
        // 从Resources加载CSV文件
        // TODO: 优化加载方式，例如AA包加载
        var csvAsset = Resources.Load<TextAsset>($"Dialogues/{csvFileName}");
        if (csvAsset == null)
        {
            Debug.LogError($"[DialogueDataManager] 未找到CSV文件: {csvFileName}");
            return null;
        }
        
        // 解析CSV
        var dialogueData = DialogueCsvReader.ParseCsv(csvAsset);
        if (dialogueData != null && dialogueData.Count > 0)
        {
            _loadedDialogues.Add(csvFileName, dialogueData);
            Debug.Log($"[DialogueDataManager] 成功加载CSV对话：{csvFileName}（{dialogueData.Count}条）");
        }
        else
        {
            Debug.LogError($"[DialogueDataManager] 解析CSV失败：{csvFileName}");
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
    /// 卸载指定CSV对话数据
    /// </summary>
    public static void UnloadDialogue(string csvName)
    {
        if (_loadedDialogues.ContainsKey(csvName))
        {
            _loadedDialogues.Remove(csvName);
            Debug.Log($"[DialogueDataManager] 卸载CSV对话：{csvName}");
        }
    }
}
using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// 对话控制器（C#端核心流程）
/// </summary>
public static class DialogueController
{
    private static DialogueModel _model = new DialogueModel();
    private static DialoguePanel _dialoguePanel;
    private static string _currentDialogueFile;

    /// <summary>
    /// 启动对话
    /// </summary>
    public static void Start(string csvFileName)
    {
        if (string.IsNullOrEmpty(csvFileName))
        {
            Debug.LogError("[DialogueController] CSV文件名为空");
            return;
        }

        _currentDialogueFile = csvFileName;
        Debug.Log($"[DialogueController] 启动对话：{csvFileName}");

        // 加载对话数据
        var dialogueData = DialogueDataManager.LoadDialogueData(csvFileName);
        if (dialogueData == null || dialogueData.Count == 0)
        {
            Debug.LogError("[DialogueController] 加载对话数据失败");
            return;
        }

        // 初始化模型
        _model.Init(dialogueData);

        // 初始化UI面板
        InitDialoguePanel();
        Refresh();

        Debug.Log("[DialogueController] 对话系统初始化完成");
    }

    /// <summary>
    /// 刷新对话（核心流程）
    /// </summary>
    private static void Refresh()
    {
        if (_model.IsEnd)
        {
            End();
            return;
        }

        // 隐藏选项
        _dialoguePanel.ClearOptions();

        var currentDialogue = _model.GetCurrentDialogue();
        if (currentDialogue == null)
        {
            End();
            return;
        }

        // 执行即时函数
        var (immediateFuncs, immediateParams) = _model.GetImmediateFunc();
        object conditionResult = null;
        for (int i = 0; i < immediateFuncs.Count; i++)
        {
            var funcName = immediateFuncs[i];
            var param = immediateParams[i].ToArray();
            var result = ExecuteFunction(funcName, param);
            // 条件判断仅取第一个函数返回值
            if (i == 0 && _model.IsConditionType())
                conditionResult = result;
        }

        // 处理条件判断跳转
        if (_model.IsConditionType())
        {
            Debug.Log($"[DialogueController] 条件判断结果：{conditionResult}");
            if (conditionResult != null && conditionResult is string nextID)
            {
                Next(nextID);
                return; // 条件跳转后直接返回，避免重复执行
            }
            else
            {
                Debug.LogError("[DialogueController] 条件判断返回空值，无法继续");
                End();
                return;
            }
        }

        // 显示选项或普通对话
        var options = _model.GetOptions();
        if (options.Count > 0)
        {
            ShowOptions(options);
        }
        else
        {
            UpdateDialogue(currentDialogue);
        }
    }

    /// <summary>
    /// 下一条对话
    /// TODO：绑定点击Panel的点击事件,IPointerClickHandler
    /// </summary>
    public static void Next(string nextID = null)
    {
        var currentDialogue = _model.GetCurrentDialogue();
        if (currentDialogue == null) return;

        // 执行交互函数
        var (interactiveFuncs, interactiveParams) = _model.GetInteractiveFunc();
        for (int i = 0; i < interactiveFuncs.Count; i++)
        {
            var funcName = interactiveFuncs[i];
            var param = interactiveParams[i].ToArray();
            ExecuteFunction(funcName, param);
        }

        // 确定目标ID
        var targetNextID = nextID ?? currentDialogue.NextID ?? "END";
        _model.UpdateCurrentID(targetNextID);
        Refresh();

        Debug.Log($"[DialogueController] 跳转至：{targetNextID}");
    }

    /// <summary>
    /// 选项选中回调
    /// </summary>
    private static void OnOptionSelect(int optionIndex)
    {
        var options = _model.GetOptions();
        if (optionIndex < 1 || optionIndex > options.Count)
        {
            Debug.LogWarning($"[DialogueController] 无效的选项索引：{optionIndex}");
            return;
        }

        var selectedOption = options[optionIndex]; // 选项索引从0开始
        Next(selectedOption.NextID);
    }

    /// <summary>
    /// 执行注册的对话函数
    /// </summary>
    private static object ExecuteFunction(string funcName, params object[] param)
    {
        Debug.Log($"[DialogueController] 执行函数：{funcName}");
        return DialogueFuncRegistry.InvokeFunction(funcName, param);
    }

    /// <summary>
    /// 结束对话
    /// </summary>
    public static void End()
    {
        Debug.Log($"[DialogueController] 结束对话：{_currentDialogueFile ?? ""}");

        // 隐藏UI
        if (_dialoguePanel != null)
        {
            _dialoguePanel.ClearOptions();
            _dialoguePanel.ClearAllCharacters();
            _dialoguePanel.gameObject.SetActive(false);
        }

        // 清理模型
        _model.Cleanup();
        _currentDialogueFile = null;
    }

    /// <summary>
    /// 初始化对话面板
    /// </summary>
    private static void InitDialoguePanel()
    {
        if (_dialoguePanel == null)
        {
            // 从UIManager获取面板
            _dialoguePanel = UIManager.Instance.GetForm<DialoguePanel>();
            if (_dialoguePanel == null)
                Debug.LogError("[DialogueController] 未找到DialoguePanel面板");
        }
    }

    /// <summary>
    /// 更新对话UI
    /// </summary>
    private static void UpdateDialogue(DialogueData dialogueData)
    {
        if (_dialoguePanel == null) return;

        var characterNames = StringUtil.SplitSemicolon(dialogueData.Character);
        var posAndOps = StringUtil.SplitSemicolon(dialogueData.PosAndOp);

        // 更新文本
        _dialoguePanel.contentText.text = dialogueData.Content;
        _dialoguePanel.characterNameText.text = characterNames.Count > 0 ? characterNames[0] : "";

        // 更新角色位置/操作
        _dialoguePanel.UpdateCharacter(characterNames, posAndOps);

        Debug.Log("[DialogueController] 已更新对话文本和角色状态");
    }

    /// <summary>
    /// 显示选项UI
    /// </summary>
    private static void ShowOptions(List<DialogueData> options)
    {
        if (_dialoguePanel == null || options.Count == 0) return;

        // 提取选项文本
        var optionTexts = new List<string>();
        foreach (var option in options)
            optionTexts.Add(option.Content ?? "");

        // 调用Panel创建选项
        _dialoguePanel.CreateOptions(optionTexts, OnOptionSelect);
    }
}
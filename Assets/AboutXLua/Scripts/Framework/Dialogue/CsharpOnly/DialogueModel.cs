using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话模型（管理对话状态、解析函数/参数、处理跳转逻辑）
/// </summary>
public class DialogueModel
{
    private string _currentID = "0";
    private List<DialogueData> _dialogueData;
    private bool _isEnd;
    private List<string> _optionIDs;
    private Dictionary<string, DialogueData> _dialogueCache; // ID -> 对话数据
    private HashSet<string> _visitedIDs; // 防循环引用

    /// <summary>
    /// 初始化对话数据
    /// </summary>
    public void Init(List<DialogueData> data)
    {
        _currentID = "0";
        _dialogueData = data;
        _isEnd = false;
        _optionIDs = null;
        _visitedIDs = new HashSet<string>();

        // 构建ID缓存
        _dialogueCache = new Dictionary<string, DialogueData>();
        foreach (var dialog in _dialogueData)
        {
            if (!_dialogueCache.ContainsKey(dialog.ID))
                _dialogueCache.Add(dialog.ID, dialog);
        }
    }

    /// <summary>
    /// 获取当前对话数据
    /// </summary>
    public DialogueData GetCurrentDialogue()
    {
        _dialogueCache.TryGetValue(_currentID, out var data);
        return data;
    }

    /// <summary>
    /// 是否为条件判断类型（Sign=$）
    /// </summary>
    public bool IsConditionType()
    {
        var current = GetCurrentDialogue();
        return current != null && current.Sign == "$";
    }

    /// <summary>
    /// 获取即时执行函数（>前缀）
    /// </summary>
    public (List<string> funcList, List<List<string>> paramList) GetImmediateFunc()
    {
        var current = GetCurrentDialogue();
        if (current == null) return (new List<string>(), new List<List<string>>());

        var funcList = new List<string>();
        var paramList = new List<List<string>>();

        var funcs = StringUtil.SplitSemicolon(current.Func);
        var paramsArr = StringUtil.SplitSemicolon(current.Params);

        // 过滤>前缀函数
        for (int i = 0; i < funcs.Count; i++)
        {
            var func = funcs[i];
            if (func.StartsWith(">"))
            {
                funcList.Add(func.Substring(1)); // 移除>前缀
                // 匹配对应参数（&分隔）
                var param = i < paramsArr.Count ? paramsArr[i] : "";
                paramList.Add(StringUtil.SplitAmpersand(param));
            }
        }

        // 日志输出
        var paramLogs = new List<string>();
        foreach (var p in paramList)
            paramLogs.Add($"{{{string.Join(", ", p)}}}");
        Debug.Log($"[DialogueModel] 即时函数：{string.Join(", ", funcList)} 参数：{string.Join(", ", paramLogs)}");

        return (funcList, paramList);
    }

    /// <summary>
    /// 获取交互执行函数（<前缀）
    /// </summary>
    public (List<string> funcList, List<List<string>> paramList) GetInteractiveFunc()
    {
        var current = GetCurrentDialogue();
        if (current == null) return (new List<string>(), new List<List<string>>());

        var funcList = new List<string>();
        var paramList = new List<List<string>>();

        var funcs = StringUtil.SplitSemicolon(current.Func);
        var paramsArr = StringUtil.SplitSemicolon(current.Params);

        // 过滤<前缀函数
        for (int i = 0; i < funcs.Count; i++)
        {
            var func = funcs[i];
            if (func.StartsWith("<"))
            {
                funcList.Add(func.Substring(1)); // 移除<前缀
                // 匹配对应参数（&分隔）
                var param = i < paramsArr.Count ? paramsArr[i] : "";
                paramList.Add(StringUtil.SplitAmpersand(param));
            }
        }

        // 日志输出
        var paramLogs = new List<string>();
        foreach (var p in paramList)
            paramLogs.Add($"{{{string.Join(", ", p)}}}");
        Debug.Log($"[DialogueModel] 交互函数：{string.Join(", ", funcList)} 参数：{string.Join(", ", paramLogs)}");

        return (funcList, paramList);
    }

    /// <summary>
    /// 更新当前对话ID（处理END/选项/循环）
    /// </summary>
    public void UpdateCurrentID(string nextID)
    {
        _optionIDs = null;
        if (nextID == "END")
        {
            _isEnd = true;
            _currentID = null;
        }
        else if (nextID.Contains(";"))
        {
            // 多ID视为选项
            _optionIDs = StringUtil.SplitSemicolon(nextID);
        }
        else
        {
            // 检测循环引用
            if (_visitedIDs.Contains(nextID))
            {
                Debug.LogError($"[DialogueModel] 检测到对话ID {nextID} 循环引用，强制结束");
                _isEnd = true;
                _currentID = null;
                return;
            }

            _visitedIDs.Add(nextID);
            _currentID = nextID;
        }
    }

    /// <summary>
    /// 获取选项列表
    /// </summary>
    public List<DialogueData> GetOptions()
    {
        var options = new List<DialogueData>();
        if (_optionIDs == null || _optionIDs.Count == 0) return options;

        foreach (var id in _optionIDs)
        {
            if (_dialogueCache.TryGetValue(id, out var dialog))
                options.Add(dialog);
        }
        return options;
    }

    /// <summary>
    /// 清理数据
    /// </summary>
    public void Cleanup()
    {
        _currentID = null;
        _dialogueData = null;
        _isEnd = false;
        _optionIDs = null;
        _dialogueCache = null;
        _visitedIDs = null;
    }

    // 只读属性
    public bool IsEnd => _isEnd;
    public string CurrentID => _currentID;
}
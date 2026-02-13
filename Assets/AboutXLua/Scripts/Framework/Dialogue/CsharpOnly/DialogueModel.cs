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
    private int _jumpCount = 0; // 跳转计数，防止无限循环
    private const int MAX_JUMP_COUNT = 100;

    /// <summary>
    /// 初始化对话数据
    /// </summary>
    public void Init(List<DialogueData> data)
    {
        _dialogueData = data;
        _isEnd = false;
        _optionIDs = null;
        _jumpCount = 0;

        if (_dialogueData != null && _dialogueData.Count > 0)
        {
            var firstID = _dialogueData[0].ID;
            if (firstID != "0")
            {
                Debug.LogWarning($"[DialogueModel] 首条对话ID不是'0'，而是'{firstID}'，建议使用'0'作为起始ID");
            }
            _currentID = firstID;
        }
        else
        {
            _currentID = "0";
        }

        // 构建ID缓存
        _dialogueCache = new Dictionary<string, DialogueData>();
        foreach (var dialog in _dialogueData)
        {
            if (!_dialogueCache.ContainsKey(dialog.ID))
                _dialogueCache.Add(dialog.ID, dialog);
        }
    }

    /// <summary>
    /// 重置跳转计数（在等待用户输入时调用）
    /// </summary>
    public void ResetJumpCount()
    {
        _jumpCount = 0;
    }

    /// <summary>
    /// 获取当前对话数据
    /// </summary>
    public DialogueData GetCurrentDialogue()
    {
        if (_dialogueCache == null || _currentID == null) return null;
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
    public (List<string> funcList, List<List<object>> paramList) GetImmediateFunc()
    {
        var current = GetCurrentDialogue();
        if (current == null) return (new List<string>(), new List<List<object>>());

        var funcList = new List<string>();
        var paramList = new List<List<object>>();

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
                var paramStr = i < paramsArr.Count ? paramsArr[i] : "";
                var rawParams = StringUtil.SplitAmpersand(paramStr);
                paramList.Add(StringUtil.ParseParamList(rawParams));
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
    public (List<string> funcList, List<List<object>> paramList) GetInteractiveFunc()
    {
        var current = GetCurrentDialogue();
        return GetInteractiveFunc(current);
    }

    /// <summary>
    /// 获取指定对话数据的交互执行函数（<前缀）
    /// </summary>
    public (List<string> funcList, List<List<object>> paramList) GetInteractiveFunc(DialogueData data)
    {
        if (data == null) return (new List<string>(), new List<List<object>>());

        var funcList = new List<string>();
        var paramList = new List<List<object>>();

        var funcs = StringUtil.SplitSemicolon(data.Func);
        var paramsArr = StringUtil.SplitSemicolon(data.Params);

        // 过滤<前缀函数
        for (int i = 0; i < funcs.Count; i++)
        {
            var func = funcs[i];
            if (func.StartsWith("<"))
            {
                funcList.Add(func.Substring(1)); // 移除<前缀
                // 匹配对应参数（&分隔）
                var paramStr = i < paramsArr.Count ? paramsArr[i] : "";
                var rawParams = StringUtil.SplitAmpersand(paramStr);
                paramList.Add(StringUtil.ParseParamList(rawParams));
            }
        }
        
        return (funcList, paramList);
    }

    /// <summary>
    /// 更新当前对话ID（处理END/选项/循环）
    /// </summary>
    public void UpdateCurrentID(string nextID)
    {
        _optionIDs = null;
        _jumpCount++;

        if (_jumpCount > MAX_JUMP_COUNT)
        {
            Debug.LogError($"[DialogueModel] 检测到对话跳转次数过多（>{MAX_JUMP_COUNT}），疑似无限循环，强制结束");
            _isEnd = true;
            _currentID = null;
            return;
        }

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
        // _visitedIDs 不再需要清理，因为Init中会新建，或者也可以清理
        // _visitedIDs = null; 
    }

    // 只读属性
    public bool IsEnd => _isEnd;
    public string CurrentID => _currentID;
}
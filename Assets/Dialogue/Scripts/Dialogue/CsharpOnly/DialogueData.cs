using System;

/// <summary>
/// 单条对话数据模型
/// </summary>
[Serializable]
public class DialogueData
{
    /// <summary> 类型标识：#对话 / %选项 / $条件判断 </summary>
    public string Sign;
    
    /// <summary> 唯一ID </summary>
    public string ID;
    
    /// <summary> 角色名（多角色;分隔） </summary>
    public string Character;
    
    /// <summary> 位置/操作（多角色;分隔，单角色多操作&分隔） </summary>
    public string PosAndOp;
    
    /// <summary> 对话内容 </summary>
    public string Content;
    
    /// <summary> 跳转ID（END结束 / ;分隔为选项 / 单个ID跳转） </summary>
    public string NextID;
    
    /// <summary> 执行函数（>即时 / <交互，多函数;分隔） </summary>
    public string Func;
    
    /// <summary> 函数参数（多函数参数;分隔，单函数参数&分隔） </summary>
    public string Params;
}
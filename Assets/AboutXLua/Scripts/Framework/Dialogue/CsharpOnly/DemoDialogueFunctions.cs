using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DemoDialogueFunctions : IDialogueFuncProvider
{
    [DialogueFunc("TestImmediateFunc")]
    public static void TestImmediateFunc(string param)
    {
        Debug.Log($"即时函数执行，参数: {param}");
    }
    
    [DialogueFunc("TestInteractiveFunc")]
    public static void TestInteractiveFunc(string param)
    {
        Debug.Log($"交互函数执行，参数: {param}");
    }
    
    [DialogueFunc("CheckCondition")]
    public static string CheckCondition(string branchA, string branchB)
    {
        // 简单条件判断，随机返回一个分支
        bool condition = Random.Range(0, 2) == 0;
        string result = condition ? branchA : branchB;
        Debug.Log($"条件判断，返回分支: {result}");
        return result;
    }
    
    [DialogueFunc("ShowSpecialEffect")]
    public static void ShowSpecialEffect(string effectName)
    {
        Debug.Log($"[Demo] 播放特殊特效：{effectName}");
    }

    [DialogueFunc("PlaySound")]
    public static void PlaySound(string soundName)
    {
        Debug.Log($"播放音效: {soundName}");
    }

    [DialogueFunc("StartDialogue")]
    public static void StartDialogue(string fileName)
    {
        Debug.Log($"启动新对话: {fileName}");
        DialogueController.Start(fileName);
    }

    [DialogueFunc("TestList")]
    public static void TestList(List<object> list)
    {
        Debug.Log($"[Demo] TestList: {StringUtil.FormatObject(list)}");
    }

    [DialogueFunc("TestDict")]
    public static void TestDict(Dictionary<object, object> dict)
    {
        Debug.Log($"[Demo] TestDict: {StringUtil.FormatObject(dict)}");
    }
}

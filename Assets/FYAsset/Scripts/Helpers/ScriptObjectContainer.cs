using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打标签用的构建工具，仅编辑器模式下使用
/// </summary>
[CreateAssetMenu(fileName = "SOContainer", menuName = "Addressables/MyWork/SO Container")]
public class ScriptObjectContainer : ScriptableObject
{
    [Header("标签管理")]
    [Tooltip("要应用到SO资源的Addressable标签")]
    public List<string> addressableLabels = new();

    [Header("SO资源")]
    [Tooltip("要管理的ScriptableObject资源列表")]
    public List<ScriptableObject> soAssets = new();
}

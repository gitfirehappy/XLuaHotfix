using System;
using UnityEngine;

/// <summary>
/// 配置转换通道
/// </summary>
[CreateAssetMenu(menuName = "Config/ConfigConvertChannel")]
public class ConfigConvertChannel : ScriptableObject
{
    [Tooltip("输入文件夹路径")]
    public string inputFolder;
    
    [Tooltip("输出文件夹路径")]
    public string outputFolder;
    
    [Tooltip("输入文件格式")]
    public ConfigFormat inputFormat;
    
    [Tooltip("输出文件格式")]
    public ConfigFormat outputFormat;
}
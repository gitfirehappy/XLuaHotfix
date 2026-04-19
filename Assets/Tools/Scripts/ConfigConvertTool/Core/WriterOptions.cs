using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Writer可选的配置类（按需扩展）
/// </summary>
public class WriterOptions
{
    public Encoding Encoding { get; set; } = new UTF8Encoding(false); // 默认UTF-8 without BOM
    public string Indent { get; set; } = "    "; // 缩进符（4个空格）
    public bool AddComment { get; set; } = true; // 是否添加字段注释
    // 其他格式相关配置...
}

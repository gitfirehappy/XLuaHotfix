using System;
using UnityEngine;

/// <summary>
/// BuildIndex 的纯数据类，Json格式存储
/// </summary>
[Serializable]
public class BuildIndexData
{
    /// <summary>Full baseline 唯一身份与目录名，不参与版本兼容判断</summary>
    public string BuildGUID;

    /// <summary>构建时间</summary>
    public string BuildTime;

    /// <summary>是否为 Debug 环境</summary>
    public bool IsDebug;

    /// <summary>目标平台</summary>
    public string Platform;

    /// <summary>构建后端，值为 "AA" 或 "AB"</summary>
    public string BackendMode;

    /// <summary>大版本号</summary>
    public VersionNumber Version;
}

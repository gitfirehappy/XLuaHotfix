using System;
using UnityEngine;

/// <summary>
/// BuildIndex 的纯数据类，用于 JSON 序列化/反序列化
/// </summary>
[Serializable]
public class BuildIndexData
{
    /// <summary>构建唯一标识 (每次构建整包时更新)</summary>
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

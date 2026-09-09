using System;
using UnityEngine;

/// <summary>
/// 安装包内置基线的 JSON 数据，由构建导出并在热更启动时读取。
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

    /// <summary>完整基线版本；Major 用于客户端兼容判断。</summary>
    public VersionNumber Version;
}

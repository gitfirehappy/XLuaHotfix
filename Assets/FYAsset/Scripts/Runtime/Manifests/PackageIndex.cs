using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 下载入口索引，默认从 https://your-domain-name/HotfixOutput/PackageIndex.json 获取。
/// 用于定位最新包体目录，不承载 AA/AB 资源清单。
/// 本身只在导出包中，不会存到用户端
/// </summary>
[System.Serializable]
public class PackageIndex
{
    public string LatestPackage;    // 例如 "Build_20250101123045_1.0.0"
    public VersionNumber LatestVersion;    // 例如 "1.0.0"
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 下载路径定位，默认从 https://your-site-name.netlify.app/HotfixOutput/manifest.json 获取
/// 本身只在导出包中，不会存到用户端
/// </summary>
[System.Serializable]
public class Manifest
{
    public string latestPackage;    // 例如 "Build_20250101_1.0.0"
    public VersionNumber latestversion;    // 例如 "1.0.0"
}

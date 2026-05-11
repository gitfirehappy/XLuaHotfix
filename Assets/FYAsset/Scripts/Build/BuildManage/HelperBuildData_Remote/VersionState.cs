using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class VersionState
{
    public VersionNumber Version;  // 版本号,可用于UI显示 
    public string FileHash;     // 唯一比对标识
    public long TotalSize;  // 更新bundle的总大小
    public List<BundleInfo> Bundles = new(); // 导出的的bundle列表
}

[Serializable]
public class BundleInfo
{
    public string BundleName;   // bundle 文件名（e.g group_assets_label_hash.bundle）
    public string FileHash;         // bundle 文件的 hash
    public long FileSize;           // bundle 文件大小
}
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class VersionState
{
    public VersionNumber version;  // 版本号,可用于UI显示 
    public string hash;     // 唯一比对标识
    public long totalSize;  // 更新bundle的总大小
    public List<BundleInfo> bundles = new(); // 导出的的bundle列表
}

[Serializable]
public class BundleInfo
{
    public string bundleName;   // bundle 文件名（e.g group_assets_label_hash.bundle）
    public string hash;         // bundle 文件的 hash
    public long size;           // bundle 文件大小
}